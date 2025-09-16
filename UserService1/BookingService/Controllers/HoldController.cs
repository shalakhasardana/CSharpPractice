using BookingService.Service;
using BookingService.Shard1;
using Contracts;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using static BookingService.Models;

namespace BookingService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HoldController : Controller
    {
        private readonly IHoldService _svc;

        public HoldController(IHoldService svc) => _svc = svc;

        [HttpPost("holds")]
        public async Task<ActionResult<CreateHoldResponse>> CreateHold([FromBody] CreateHoldRequest req, CancellationToken ct)
        {
            try
            {
                var res = await _svc.CreateHoldAsync(req, ct);
                return Ok(res);
            }
            catch (HoldConflictException ex)
            {
                return Conflict(new
                {
                    message = ex.Message,
                    unavailableSeatIds = ex.UnavailableSeatIds,
                    lockedSeatIds = ex.LockedSeatIds
                });
            }
            catch (ConflictException ex)
            {
                return Conflict(ex.Payload ?? new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }


        [HttpPost("bookings/confirm")]
        public async Task<ActionResult<ConfirmBookingResponse>> Confirm(
                [FromBody] ConfirmBookingRequest req, CancellationToken ct)
        {
            try
            {
                var res = await _svc.ConfirmAsync(req, ct);
                return Ok(res);
            }
            catch (NotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (ConflictException ex)
            {
                return Conflict(ex.Payload ?? new { message = ex.Message });
            }
        }
    }
}
