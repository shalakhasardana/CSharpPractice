using BookingService.Shard1;
using Contracts;
using MassTransit;
using Npgsql;
using NpgsqlTypes;
using static BookingService.Models;

namespace BookingService.Service
{
    public interface IHoldService
    {
        Task<CreateHoldResponse> CreateHoldAsync(CreateHoldRequest req, CancellationToken ct);
        Task<ConfirmBookingResponse> ConfirmAsync(ConfirmBookingRequest req, CancellationToken ct);
    }

    public sealed class NotFoundException : Exception
    {
        public NotFoundException(string msg) : base(msg) { }
    }

    public class ConflictException : Exception
    {
        public object? Payload { get; }
        public ConflictException(string msg, object? payload = null) : base(msg) => Payload = payload;
    }


    // Specialized for seat hold conflicts
    public sealed class HoldConflictException : ConflictException
    {
        public long[] UnavailableSeatIds { get; }
        public long[] LockedSeatIds { get; }
        public HoldConflictException(string msg, long[] unavailable, long[] locked)
            : base(msg, new { unavailableSeatIds = unavailable, lockedSeatIds = locked })
        {
            UnavailableSeatIds = unavailable;
            LockedSeatIds = locked;
        }
    }

    public class HoldService : IHoldService
    {
        private readonly IShardResolver _resolver;
        private readonly IShardDb _shards;
        private readonly NpgsqlDataSource _catalog; // shared (non-sharded) DB
        private readonly IPublishEndpoint _bus;
        private readonly ILogger<HoldService> _logger;

        public HoldService(
            IShardResolver resolver,
            IShardDb shards,
            NpgsqlDataSource catalog,
            IPublishEndpoint bus,
            ILogger<HoldService> logger)
        {
            _resolver = resolver;
            _shards = shards;
            _catalog = catalog;
            _bus = bus;
            _logger = logger;
        }

        public async Task<CreateHoldResponse> CreateHoldAsync(CreateHoldRequest req, CancellationToken ct)
        {
            var holdId = Guid.NewGuid();
            var shardId = await _resolver.ResolveShardForShowAsync(req.ShowId, ct);

            await using var conn = await _shards.OpenAsync(shardId, ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            // Step 1: create hold row on shard
            cmd.CommandText = @"INSERT INTO booking.holds (id, show_id, user_id, expires_at, status)
                                VALUES (@hid, @sid, @uid, now() + (@secs || ' seconds')::interval, 'ACTIVE')
                                RETURNING expires_at;";
            cmd.Parameters.AddWithValue("@hid", NpgsqlDbType.Uuid, holdId);
            cmd.Parameters.AddWithValue("@sid", NpgsqlDbType.Bigint, req.ShowId);
            cmd.Parameters.AddWithValue("@uid", NpgsqlDbType.Bigint, (object?)req.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@secs", NpgsqlDbType.Integer, req.HoldSeconds);

            var expiresAt = (DateTime)await cmd.ExecuteScalarAsync(ct);
            cmd.Parameters.Clear();

            // Step 2: flip exact seat versions to HELD in the shared catalog DB
            var seatIds = req.Seats.Select(s => s.SeatId).ToArray();
            var versions = req.Seats.Select(s => s.Version).ToArray();

            await using var connShared = await _catalog.OpenConnectionAsync(ct);
            await using var cmd1 = connShared.CreateCommand();

            cmd1.CommandText = @"
                WITH wanted(seat_id, ver) AS (
                  SELECT *
                  FROM unnest(@seatIds::bigint[], @versions::int[])
                ),
                upd AS (
                  UPDATE booking.show_seats ss
                  SET status = 'HELD',
                      hold_id = @hid,
                      version = ss.version + 1
                  FROM wanted w
                  WHERE ss.show_id=@sid
                    AND ss.seat_id = w.seat_id
                    AND ss.version = w.ver
                    AND ss.status='AVAILABLE'
                  RETURNING ss.seat_id
                )
                SELECT seat_id FROM upd;";

            cmd1.Parameters.AddWithValue("@hid", NpgsqlDbType.Uuid, holdId);
            cmd1.Parameters.AddWithValue("@sid", NpgsqlDbType.Bigint, req.ShowId);
            cmd1.Parameters.Add("@seatIds", NpgsqlDbType.Array | NpgsqlDbType.Bigint).Value = seatIds;
            cmd1.Parameters.Add("@versions", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = versions;

            _logger.LogInformation("SQL:\n{Sql}\nParams:\n{Params}",
                cmd1.CommandText, FormatParams(cmd1.Parameters));

            var locked = new List<long>();
            await using (var r = await cmd1.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                    locked.Add(r.GetInt64(0));
            }

            if (locked.Count != seatIds.Length)
            {
                await tx.RollbackAsync(ct);

                var lockedSet = locked.ToHashSet();
                var failed = seatIds.Where(id => !lockedSet.Contains(id)).ToArray();

                throw new HoldConflictException(
                    "Some seats changed since you viewed them. Please refresh.",
                    failed,
                    locked.ToArray()
                );
            }

            await tx.CommitAsync(ct);
            return new CreateHoldResponse(holdId, expiresAt, locked.ToArray());
        }

        private static string FormatParams(NpgsqlParameterCollection ps)
        {
            var sb = new System.Text.StringBuilder();
            foreach (NpgsqlParameter p in ps)
            {
                string val = p.Value switch
                {
                    Array a when a.Length > 10 => $"[{a.Length} items]",
                    null => "NULL",
                    _ => p.Value.ToString()!
                };
                sb.AppendLine($"{p.ParameterName}  DbType={p.DbType}  NpgsqlDbType={p.NpgsqlDbType}  Value={val}");
            }
            return sb.ToString();
        }

        public async Task<ConfirmBookingResponse> ConfirmAsync(ConfirmBookingRequest req, CancellationToken ct)
        {
            var shardId = await _resolver.ResolveShardForShowAsync(req.ShowId, ct);

            await using var shardConn = await _shards.OpenAsync(shardId, ct);
            await using var shardTx = await shardConn.BeginTransactionAsync(ct);
            await using var shardCmd = shardConn.CreateCommand();
            shardCmd.Transaction = shardTx;

            // 1) Validate hold on shard
            shardCmd.CommandText = @"
                SELECT show_id, expires_at, status
                FROM booking.holds
                WHERE id=@hid
                FOR UPDATE;";
            shardCmd.Parameters.AddWithValue("@hid", NpgsqlDbType.Uuid, req.HoldId);

            long holdShowId;
            DateTimeOffset expiresAt;
            string holdStatus;

            await using (var r1 = await shardCmd.ExecuteReaderAsync(ct))
            {
                if (!await r1.ReadAsync(ct))
                    throw new NotFoundException("Hold not found");

                holdShowId = r1.GetInt64(r1.GetOrdinal("show_id"));
                expiresAt = r1.GetFieldValue<DateTimeOffset>(r1.GetOrdinal("expires_at"));
                holdStatus = r1.GetString(r1.GetOrdinal("status"));
            }
            shardCmd.Parameters.Clear();

            if (holdStatus != "ACTIVE" || expiresAt <= DateTimeOffset.UtcNow)
                throw new ConflictException("Hold expired or not active");

            // 2) Pull held seats (and prices) from CATALOG DB
            await using var catalogConn = await _catalog.OpenConnectionAsync(ct);
            await using var catalogTx = await catalogConn.BeginTransactionAsync(ct);
            await using var catalogCmd = catalogConn.CreateCommand();
            catalogCmd.Transaction = catalogTx;

            catalogCmd.CommandText = @"
                SELECT ss.seat_id, ss.show_id, ss.price
                FROM booking.show_seats ss
                WHERE ss.hold_id=@hid;";
            catalogCmd.Parameters.AddWithValue("@hid", NpgsqlDbType.Uuid, req.HoldId);

            var seatIds = new List<long>();
            var seatPrices = new List<decimal>();
            long catalogShowId = 0;

            await using (var r2 = await catalogCmd.ExecuteReaderAsync(ct))
            {
                while (await r2.ReadAsync(ct))
                {
                    seatIds.Add(r2.GetInt64(0));
                    catalogShowId = catalogShowId == 0 ? r2.GetInt64(1) : catalogShowId;
                    seatPrices.Add(r2.GetDecimal(2));
                }
            }
            catalogCmd.Parameters.Clear();

            if (seatIds.Count == 0)
            {
                await catalogTx.RollbackAsync(ct);
                await shardTx.RollbackAsync(ct);
                throw new ConflictException("No seats held for this hold.");
            }

            if (catalogShowId != holdShowId)
            {
                await catalogTx.RollbackAsync(ct);
                await shardTx.RollbackAsync(ct);
                throw new ConflictException("Hold/show mismatch.");
            }

            // 3) Compute totals & create booking on shard
            var amount = seatPrices.Aggregate(0m, (acc, p) => acc + p);
            var snow = new Snowflake(shardId);
            var bookingId = snow.NextId();

            shardCmd.CommandText = @"
                INSERT INTO booking.bookings (id, code, show_id, user_id, status, amount_total, paid_at)
                VALUES (
                    @bid,
                    SUBSTR(encode(gen_random_bytes(8),'hex'),1,8),
                    @sid,
                    @uid,
                    'CONFIRMED',
                    @amt,
                    now()
                )
                RETURNING code, amount_total;";
            shardCmd.Parameters.AddWithValue("@bid", NpgsqlDbType.Bigint, bookingId);
            shardCmd.Parameters.AddWithValue("@sid", NpgsqlDbType.Bigint, holdShowId);
            shardCmd.Parameters.AddWithValue("@uid", NpgsqlDbType.Bigint, (object?)req.UserId ?? DBNull.Value);
            shardCmd.Parameters.AddWithValue("@amt", NpgsqlDbType.Numeric, amount);

            string code;
            decimal amountTotal;
            await using (var r3 = await shardCmd.ExecuteReaderAsync(ct))
            {
                if (!await r3.ReadAsync(ct))
                {
                    await catalogTx.RollbackAsync(ct);
                    await shardTx.RollbackAsync(ct);
                    throw new ConflictException("Could not create booking.");
                }
                code = r3.GetString(0);
                amountTotal = r3.GetDecimal(1);
            }
            shardCmd.Parameters.Clear();

            // 4) Insert booking_seats on shard
            shardCmd.CommandText = @"
                INSERT INTO booking.booking_seats (booking_id, show_id, seat_id, price_paid)
                SELECT @bid, @sid, x.seat_id, x.price
                FROM UNNEST(@seat_ids::bigint[], @prices::numeric[]) AS x(seat_id, price);";
            shardCmd.Parameters.AddWithValue("@bid", NpgsqlDbType.Bigint, bookingId);
            shardCmd.Parameters.AddWithValue("@sid", NpgsqlDbType.Bigint, holdShowId);
            shardCmd.Parameters.AddWithValue("@seat_ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint, seatIds.ToArray());
            shardCmd.Parameters.AddWithValue("@prices", NpgsqlDbType.Array | NpgsqlDbType.Numeric, seatPrices.ToArray());
            await shardCmd.ExecuteNonQueryAsync(ct);
            shardCmd.Parameters.Clear();

            // 5) Finalize seats in catalog
            catalogCmd.CommandText = @"
                UPDATE booking.show_seats
                SET status='BOOKED', hold_id=NULL, version=version+1
                WHERE hold_id=@hid;";
            catalogCmd.Parameters.AddWithValue("@hid", NpgsqlDbType.Uuid, req.HoldId);
            await catalogCmd.ExecuteNonQueryAsync(ct);
            catalogCmd.Parameters.Clear();

            // 6) Mark hold as converted
            shardCmd.CommandText = @"UPDATE booking.holds SET status='CONVERTED' WHERE id=@hid;";
            shardCmd.Parameters.AddWithValue("@hid", NpgsqlDbType.Uuid, req.HoldId);
            await shardCmd.ExecuteNonQueryAsync(ct);
            shardCmd.Parameters.Clear();

            // 7) Commit both (best-effort)
            try
            {
                await catalogTx.CommitAsync(ct);
                await shardTx.CommitAsync(ct);
            }
            catch
            {
                try { await catalogTx.RollbackAsync(ct); } catch { }
                try { await shardTx.RollbackAsync(ct); } catch { }
                throw;
            }

            // 8) Publish event
            await _bus.Publish(new BookingConfirmed(
                bookingId: bookingId,
                code: code,
                showId: holdShowId,
                userId: req.UserId,
                amount: amountTotal,
                seatCodes: seatIds.ToArray(),
                occurredAtUtc: DateTimeOffset.UtcNow
            ), ct);

            return new ConfirmBookingResponse(bookingId, code, amountTotal);
        }
    }
    }
