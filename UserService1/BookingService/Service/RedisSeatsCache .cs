using Npgsql;
using NpgsqlTypes;
using StackExchange.Redis;
using System.Text.Json;

namespace BookingService.Service
{
    public record SeatDto(long SeatId, long ShowId, decimal Price, string Status, int Version);
    public record SeatsEnvelope(long ShowId, string Etag, DateTimeOffset FetchedAt, List<SeatDto> Seats);
    // Etag is a cache version token you can bump on invalidations.

    // Cache Service ( Cache-aside + stampede protection)
    // Cache-aside: tr Redis; on miss, query Postgres, then cache.
    // Stampede protection: short distributed lock to avoid thundering herd on cache miss.
    // Etag: a cheap version token to identify cache staleness.
    public interface IRedisSeatsCache
    {
        Task<SeatsEnvelope?> GetSeatsAsync(long showId, CancellationToken ct = default);
        Task InvalidateAsync(long showId, CancellationToken ct = default);
    }

    public sealed class RedisSeatsCache : IRedisSeatsCache
    {
        private readonly NpgsqlDataSource _catalog;              // shared DBs
        private readonly IConnectionMultiplexer _redis;
        private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
        private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);       // demo TTL
        private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(5);    // stampede lock

        private static string SeatsKey(long showId) => $"show:{showId}:seats";
        private static string LockKey(long showId) => $"show:{showId}:lock";
        private static string EtagKey(long showId) => $"show:{showId}:etag";   // bumped on writes

        public RedisSeatsCache(NpgsqlDataSource catalog, IConnectionMultiplexer redis)
        {
            _catalog = catalog;
            _redis = redis;
        }
        public async Task<SeatsEnvelope?> GetSeatsAsync(long showId, CancellationToken ct = default)
        {
            var db = _redis.GetDatabase();
            var key = SeatsKey(showId);

            // 1) Try read from cache
            var cached = await db.StringGetAsync(key);
            if (cached.HasValue)
                return JsonSerializer.Deserialize<SeatsEnvelope>(cached!, _json);

            // 2) Stampede protection: acquire short lock
            var lockToken = Guid.NewGuid().ToString("N");
            var gotLock = await db.StringSetAsync(LockKey(showId),lockToken, LockTtl, When.NotExists);
            // true: you set the key → you own the lock.
            // false: key already exists → someone else holds the lock.
            if (!gotLock)
            {
                // Another request is warming the cache; brief backoff then re-check
                await Task.Delay(120, ct);
                cached = await db.StringGetAsync(key);
                if (cached.HasValue)
                    return JsonSerializer.Deserialize<SeatsEnvelope>(cached!, _json);
                // give up gracefully
                return null;
            }

            try
            {
                // 3) Load from Postgres (shared)
                var env = await LoadFromDbAsync(showId, ct);
                if (env is null) return null;

                // 4) Write to cache with TTL (and store etag)
                var payload = JsonSerializer.Serialize(env, _json);
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(key, payload, Ttl, When.Always);
                _ = tran.StringSetAsync(EtagKey(showId), env.Etag, Ttl, When.Always);
                await tran.ExecuteAsync();

                return env;
            }
            finally
            {
                await db.LockReleaseAsync(LockKey(showId), lockToken);
            }
        }

        private async Task<SeatsEnvelope?> LoadFromDbAsync(long showId, CancellationToken ct)
        {
            await using var conn = await _catalog.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT seat_id, show_id, price, status, version
            FROM booking.show_seats
            WHERE show_id = @sid
            ORDER BY seat_id";
            cmd.Parameters.AddWithValue("@sid", NpgsqlDbType.Bigint, showId);

            var list = new List<SeatDto>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new SeatDto(
                    r.GetInt64(0),
                    r.GetInt64(1),
                    r.GetDecimal(2),
                    r.GetString(3),
                    r.GetInt32(4)));
            }

            if (list.Count == 0) return null;

            // Etag can be a cheap hash: total rows + max(version)
            var etag = $"{list.Count}:{list.Max(s => s.Version)}";
            return new SeatsEnvelope(showId, etag, DateTimeOffset.UtcNow, list);
        }

        public async Task InvalidateAsync(long showId, CancellationToken ct = default)
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(new RedisKey[] { SeatsKey(showId), EtagKey(showId) });
        }
    }
}
