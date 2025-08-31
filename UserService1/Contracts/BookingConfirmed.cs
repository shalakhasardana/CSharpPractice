using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Contracts
{
    public class BookingConfirmed
    {
        // Properties (immutable after construction)
        public long BookingId { get; set; }
        public string Code { get; set; } = string.Empty;
        public long ShowId { get; set; }
        public long? UserId { get; set; }
        public decimal Amount { get; set; }
        public long[] SeatCodes { get; set; } = Array.Empty<long>();
        public DateTimeOffset OccurredAtUtc { get; set; }

        // Parameterless ctor for serializers

        // Convenience ctor
        public BookingConfirmed(
            long bookingId,
            string code,
            long showId,
            long? userId,
            decimal amount,
            long[] seatCodes,
            DateTimeOffset occurredAtUtc)
        {
            BookingId = bookingId;
            Code = code;
            ShowId = showId;
            UserId = userId;
            Amount = amount;
            SeatCodes = seatCodes ?? Array.Empty<long>();
            OccurredAtUtc = occurredAtUtc;
            this.BookingId = BookingId;
        }
    }
}
