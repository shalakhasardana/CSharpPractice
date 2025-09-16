namespace BookingService
{
    public class Models
    {
        public sealed record SeatPick(long SeatId, int Version);
        public sealed record CreateHoldRequest(long ShowId, SeatPick[] Seats, int HoldSeconds = 120, long? UserId = null);
        public sealed record CreateHoldResponse(Guid HoldId, DateTimeOffset ExpiresAt, long[] LockedSeatIds);

        public record ConfirmBookingRequest(Guid HoldId, long ShowId, long? UserId, string PaymentRef);

        public record ConfirmBookingResponse(long BookingId, string Code, decimal Amount);
    }
}
