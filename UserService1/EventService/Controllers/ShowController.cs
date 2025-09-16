
using EventService.Cache;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace EventService.Controllers
{
    [ApiController]
    [Route("api/show")]
    public class ShowController : Controller
    {
        private readonly NpgsqlDataSource _db;

        private readonly AppCache _cache;
        public ShowController(NpgsqlDataSource db, AppCache cache)
        {
            _db = db;
            _cache = cache;
        }

        [HttpGet("{showId:long}")]
        public async Task<IActionResult> GetShow(long showId, CancellationToken ct)
        {
            var key = CacheKeys.ShowMeta(showId);
            var show = await _cache.GetOrCreateAsync(key, TimeSpan.FromMinutes(60), async () =>
            {
                await using var cmd = _db.CreateCommand(
                    "select id, auditorium_id, movie_id, start_at from public.shows where id=@id");
                cmd.Parameters.AddWithValue("id", showId);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) throw new KeyNotFoundException();
                return new
                {
                    Id = r.GetInt64(0),
                    AuditoriumId = r.GetInt64(1),
                    MovieId = r.GetInt64(2),
                    Start = r.GetFieldValue<DateTimeOffset>(3)
                };
            });
            return Ok(show);
        }
    }
}
