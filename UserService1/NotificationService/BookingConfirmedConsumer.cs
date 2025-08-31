using Contracts;
using MassTransit;

namespace NotificationService
{
    public class BookingConfirmedConsumer : IConsumer<BookingConfirmed>
    {
        private readonly ILogger<BookingConfirmedConsumer> _log;

        public BookingConfirmedConsumer(ILogger<BookingConfirmedConsumer> log) => _log = log;

        public async Task Consume(ConsumeContext<BookingConfirmed> context)
        {
            var e = context.Message;

            // TODO: send email/SMS here
            _log.LogInformation("Booking {Code} for user {UserId} confirmed. Seats: {Seats}",
                e.Code, e.UserId, e.SeatCodes.ToString());

            await Task.CompletedTask;
        }
    }
}
