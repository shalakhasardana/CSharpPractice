using Npgsql;

namespace BookingService.Shard1
{
    public interface IShardDb
    {
        ValueTask<NpgsqlConnection> OpenAsync(int shardId, CancellationToken ct = default);
    }

    public sealed class ShardDb : IShardDb, IAsyncDisposable
    {
        private readonly IReadOnlyDictionary<int, NpgsqlDataSource> _sources;

        public ShardDb(IEnumerable<(int Id, string Conn)> shards)
        {
            _sources = shards.ToDictionary(
                s => s.Id,
                s => NpgsqlDataSource.Create(s.Conn));
        }

        public async ValueTask<NpgsqlConnection> OpenAsync(int shardId, CancellationToken ct = default)
        {
            if (!_sources.TryGetValue(shardId, out var ds))
                throw new KeyNotFoundException($"Shard {shardId} not configured.");
            return await ds.OpenConnectionAsync(ct);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var ds in _sources.Values)
                await ds.DisposeAsync();
        }
    }
}