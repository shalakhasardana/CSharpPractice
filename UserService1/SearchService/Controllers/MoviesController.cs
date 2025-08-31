using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;

namespace SearchService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MoviesController : ControllerBase
    {
        private readonly NpgsqlDataSource _db;
        public MoviesController(NpgsqlDataSource db) => _db = db;
    

    // POST /api/movies/search
    [HttpPost("search")]
        public async Task<ActionResult<IEnumerable<MovieCard>>> Search(
        [FromBody] MovieSearchRequest req,
        CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.City))
                return BadRequest("city is required");

            const string sql = @"
            SELECT
              m.id,
              m.title,
              m.language,
              m.runtime_min,
              array_agg(DISTINCT g.name) AS genres,
              s.start_at           AS first_showtime
            FROM cities        c
            JOIN theaters      t  ON t.city_id       = c.id
            JOIN auditoriums   au ON au.theater_id   = t.id
            JOIN shows         s  ON s.auditorium_id = au.id
            JOIN movies        m  ON m.id            = s.movie_id
            LEFT JOIN movie_genre mg ON mg.movie_id  = m.id
            LEFT JOIN genres       g  ON g.id        = mg.genre_id
            LEFT JOIN show_prices  sp ON sp.show_id  = s.id
            WHERE c.slug = @city
              AND s.start_at >=  @day::date
            GROUP BY m.id, m.title, m.language, m.runtime_min,s.start_at
            ORDER BY m.title;";

            await using var cmd = _db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("@city", NpgsqlDbType.Text, req.City.Trim().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@day", NpgsqlDbType.Date, req.Date);

            var list = new List<MovieCard>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var genres = reader.IsDBNull(4) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(4);

                list.Add(new MovieCard(
                    MovieId: reader.GetInt64(0),
                    Title: reader.GetString(1),
                    Language: reader.IsDBNull(2) ? null : reader.GetString(2),
                    RuntimeMin: reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    Genres: genres,
                    Showtime: reader.GetFieldValue<DateTimeOffset>(5)
                ));
            }

            return Ok(list);
        }

        // Optional GET: /api/movies/search?city=redmond&date=2025-08-19
        [HttpGet("search")]
        public Task<ActionResult<IEnumerable<MovieCard>>> SearchGet([FromQuery] string city, [FromQuery] DateOnly date, CancellationToken ct)
            => Search(new MovieSearchRequest(city, date), ct);
    }
}