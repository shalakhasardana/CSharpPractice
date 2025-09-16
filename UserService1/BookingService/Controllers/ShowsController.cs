using BookingService.Service;
using Microsoft.AspNetCore.Mvc;

namespace BookingService.Controllers
{
    [ApiController]
    [Route("api/shows")]
    public class ShowsController : Controller
    {
        private readonly IRedisSeatsCache _cache;

        public ShowsController(IRedisSeatsCache cache) => _cache = cache;

        [HttpGet("{showId:long}/seats")]
        public async Task<IActionResult> GetSeats(long showId, CancellationToken ct)
        {
            var env = await _cache.GetSeatsAsync(showId, ct);
            if (env is null) return NotFound(new { message = "No seats found for show." });
            return Ok(env);
        }
    }
}
