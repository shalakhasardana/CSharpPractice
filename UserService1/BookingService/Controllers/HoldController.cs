using Contracts;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using static BookingService.Models;

namespace BookingService.Controllers
{
    public class HoldController : Controller
    {
        private readonly NpgsqlDataSource _db;
        private readonly ILogger<HoldController> _logger;
        private readonly IPublishEndpoint _bus;
        public HoldController(NpgsqlDataSource db, ILogger<HoldController> logger, IPublishEndpoint bus)
        {
            _db = db;
            _logger = logger; // ← now _logger i
            _bus = bus;
        }

        [HttpPost("holds")]
        public async Task<ActionResult<CreateHoldResponse>> CreateHold([FromBody] CreateHoldRequest req, CancellationToken ct)
        {
            var holdId = Guid.NewGuid();

            await using var conn = await _db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            await using var cmd = conn.CreateCommand();

            // Step1: Insert hold row

            cmd.CommandText = @"INSERT INTO booking.holds (id, show_id, user_id, expires_at, status)
                        VALUES (@hid, @sid, @uid, now() + (@secs || ' seconds')::interval, 'ACTIVE')
                        RETURNING expires_at;";

            cmd.Parameters.AddWithValue("@hid", NpgsqlDbType.Uuid, holdId);
            cmd.Parameters.AddWithValue("@sid", NpgsqlDbType.Bigint, req.ShowId);
            cmd.Parameters.AddWithValue("@uid", NpgsqlDbType.Bigint, (object?)req.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@secs", NpgsqlDbType.Integer, req.HoldSeconds);
            var expiresAt = (System.DateTime)await cmd.ExecuteScalarAsync(ct);
            cmd.Parameters.Clear();

            // Step2: Try to flip extact seat versions to HELD

            var seatIds = req.Seats.Select(s => s.SeatId).ToArray();
            var versions = req.Seats.Select(s => s.Version).ToArray();


            cmd.CommandText = @"
            WITH wanted(seat_id, ver) AS (
              SELECT *
              FROM unnest(@seatIds::bigint[], @versions:: int[])
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

            cmd.Parameters.AddWithValue("@hid", NpgsqlDbType.Uuid, holdId);
            cmd.Parameters.AddWithValue("@sid", NpgsqlDbType.Bigint, req.ShowId);
            cmd.Parameters.Add("@seatIds", NpgsqlDbType.Array | NpgsqlDbType.Bigint).Value = seatIds;
            cmd.Parameters.Add("@versions", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = versions;

            _logger.LogInformation("SQL:\n{Sql}\nParams:\n{Params}",
                cmd.CommandText, FormatParams(cmd.Parameters));

            var sqlWithValues = cmd.CommandText;
            foreach (NpgsqlParameter p in cmd.Parameters)
            {
                var val = p.Value is Array arr
                    ? $"ARRAY[{string.Join(",", arr.Cast<object>())}]"
                    : p.Value?.ToString();

                sqlWithValues = sqlWithValues.Replace(p.ParameterName, val);
            }

            _logger.LogInformation(sqlWithValues);


            var locked = new List<long>();

            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                    locked.Add(r.GetInt64(0));
            }



            if (locked.Count != seatIds.Length)
            {
                await tx.RollbackAsync(ct);

                // report which ones failed
                var lockedSet = locked.ToHashSet();
                var failed = seatIds.Where(id => !lockedSet.Contains(id)).ToArray();


                return Conflict(new
                {
                    message = "Some seats changed since you viewed them. Please refresh",
                    unavailableSeatIds = failed,
                    lockedSeatIds = locked
                });

            }

            await tx.CommitAsync(ct);
            return Ok(new CreateHoldResponse(holdId, expiresAt, locked.ToArray()));
        }

        static string FormatParams(NpgsqlParameterCollection ps)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Npgsql.NpgsqlParameter p in ps)
            {
                // Trim big arrays; don’t dump PII or secrets
                string val = p.Value switch
                {
                    Array a when a.Length > 10 => $"[{a.Length} items]",
                    null => "NULL",
                    _ => p.Value.ToString()!
                };

                sb.AppendLine($"{p.ParameterName}  "
                    + $"DbType={p.DbType}  NpgsqlDbType={p.NpgsqlDbType}  Value={val}");
            }
            return sb.ToString();
        }

        [HttpPost("bookings/confirm")]
        public async Task<ActionResult<ConfirmBookingResponse>> Confirm([FromBody] ConfirmBookingRequest req, CancellationToken ct)
        {
            await using var conn = await _db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            await using var cmd = conn.CreateCommand();

            // validate hold
            cmd.CommandText = @"SELECT show_id, expires_at, status FROM booking.holds WHERE id=@hid FOR UPDATE;";
            cmd.Parameters.AddWithValue("@hid", NpgsqlDbType.Uuid, req.HoldId);
            await using (var r1 = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await r1.ReadAsync(ct)) return NotFound("Hold not found");
                if (r1.GetString(2) != "ACTIVE" || r1.GetFieldValue<DateTimeOffset>(1) <= DateTimeOffset.UtcNow)
                    return Conflict("Hold expired or not active");
            }

            cmd.Parameters.Clear();
            // create booking
            cmd.CommandText = @"
            INSERT INTO booking.bookings (code, show_id, user_id, status, amount_total, paid_at)
                SELECT
                  SUBSTR(encode(gen_random_bytes(8),'hex'),1,8)              AS code,
                  h.show_id,
                  @uid                                                       AS user_id,
                  'CONFIRMED'                                                AS status,
                  COALESCE(SUM(ss.price), 0)                                 AS amount_total,
                  now()                                                      AS paid_at
                FROM booking.holds h
                JOIN booking.show_seats ss ON ss.hold_id = h.id
                WHERE h.id = @hid
                GROUP BY h.show_id
                RETURNING id, code, amount_total;";

            cmd.Parameters.AddWithValue("@hid", NpgsqlDbType.Uuid, req.HoldId);
            cmd.Parameters.AddWithValue("@uid", NpgsqlDbType.Bigint, (object?)req.UserId ?? DBNull.Value);

            long bookingId; string code; decimal amount;
            await using (var r2 = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await r2.ReadAsync(ct)) return Conflict("No seats locked for this hold");
                bookingId = r2.GetInt64(0); code = r2.GetString(1); amount = r2.GetDecimal(2);
            }
            cmd.Parameters.Clear();


            // copy seats
            cmd.CommandText = @"
            INSERT INTO booking.booking_seats (booking_id, show_id, seat_id, price_paid)
            SELECT @bid, ss.show_id, ss.seat_id, ss.price
            FROM booking.show_seats ss
            WHERE ss.hold_id=@hid;";
            cmd.Parameters.AddWithValue("@bid", NpgsqlDbType.Bigint, bookingId);
            cmd.Parameters.AddWithValue("@hid", NpgsqlDbType.Uuid, req.HoldId);
            await cmd.ExecuteNonQueryAsync(ct);
            cmd.Parameters.Clear();


            cmd.Parameters.Clear();
            const string sql = @"
            SELECT ss.seat_id, ss.show_id
            FROM booking.show_seats ss
            WHERE hold_id = @hid;";
             
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@hid", NpgsqlDbType.Uuid, req.HoldId);
            var seatIds = new List<long>();
            long? showId = null;

            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    seatIds.Add(r.GetInt64(0));         // seat_id
                    showId ??= r.GetInt64(1);           // show_id (same for all rows)
                }
            }

            if (seatIds.Count == 0)
                return Conflict("No seats held for this hold.");

            long[] seatIdsArray = seatIds.ToArray();
            long showIdValue = showId!.Value;

            // finalize seats + hold

            cmd.CommandText = @"UPDATE booking.show_seats SET status='BOOKED', hold_id=NULL, version=version+1 WHERE hold_id=@hid;";
            cmd.Parameters.AddWithValue("@hid", NpgsqlDbType.Uuid, req.HoldId);
            await cmd.ExecuteNonQueryAsync(ct);
            cmd.Parameters.Clear();

            cmd.CommandText = @"UPDATE booking.holds SET status='CONFIRMED' WHERE id=@hid;";
            cmd.Parameters.AddWithValue("@hid", NpgsqlDbType.Uuid, req.HoldId);
            await cmd.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
            await _bus.Publish(new BookingConfirmed(
                bookingId: bookingId,
            code: code,
            showId: showIdValue,
            userId: req.UserId,
            amount: amount,
            seatCodes: seatIdsArray,
            occurredAtUtc: DateTimeOffset.UtcNow
            ), ct);
            return Ok(new ConfirmBookingResponse(bookingId, code, amount));

        }
    }
}
