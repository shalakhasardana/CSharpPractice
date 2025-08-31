using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace EventService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TheatersController : Controller
    {

        private readonly NpgsqlDataSource _db;
        public TheatersController(NpgsqlDataSource db) => _db = db;


        // GET /api/theaters?cityId=1
        [Authorize(Policy = "perm:Theater_Read")]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TheaterDto>>> List([FromQuery] int? cityId)
        {
            await using var conn = await _db.OpenConnectionAsync();
            var sql = "SELECT id, name, city_id, address FROM theaters";
            if (cityId.HasValue) sql += " WHERE city_id=@c";
            sql += " ORDER BY id DESC";
            await using var cmd = new NpgsqlCommand(sql, conn);
            if (cityId.HasValue) cmd.Parameters.AddWithValue("@c", cityId.Value);

            var list = new List<TheaterDto>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                list.Add(new TheaterDto
                {
                    Id = rd.GetInt32(0),
                    Name = rd.GetString(1),
                    CityId = rd.GetInt32(2),
                    Address = rd.IsDBNull(3) ? null : rd.GetString(3)
                });
            }
            return list;
        }


        // Helper
        public record TheaterDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = default!;
            public int CityId { get; set; }
            public string? Address { get; set; }
        }
    }
}
