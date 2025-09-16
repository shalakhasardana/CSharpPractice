using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace UserService.Controllers
{
    public class UsersController : Controller
    {
        private readonly NpgsqlDataSource _db;
        public UsersController(NpgsqlDataSource db) => _db = db;


        // ----Helpers----------------------------------
        private long GetUserId()
        {
            var v = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return long.Parse(v!);
        }


        private static string[] GetRoleClaims(ClaimsPrincipal user) =>
        user.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        private static string[] GetPermClaims(ClaimsPrincipal user) =>
            user.FindAll("perm").Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // ========= ME =========


        // GET /api/users/me
        [Authorize]
        [HttpGet("me")]
        public async Task<ActionResult<UserMeDto>> Me()
        {
            var uid = GetUserId();
            await using var conn = await _db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT id, email, phone, status, created_at FROM users WHERE id=@id;", conn);
            cmd.Parameters.AddWithValue("@id", uid);

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return NotFound();

            return new UserMeDto
            {
                Id = rd.GetInt64(0),
                Email = rd.GetString(1),
                Phone = rd.IsDBNull(2) ? null : rd.GetString(2),
                Status = rd.GetString(3),
                CreatedAt = rd.GetDateTime(4),
                Roles = GetRoleClaims(User),
                Permissions = GetPermClaims(User)
            };
        }


        // PUT /api/users/me  (update phone and/or change password)
        [Authorize]
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMe([FromBody] UpdateMeRequest req)
        {
            var uid = GetUserId();
            await using var conn = await _db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            string currentHash;
            await using (var getPwd = new NpgsqlCommand(
                "SELECT password  FROM users WHERE id=@id;", conn, tx))
            {
                getPwd.Parameters.AddWithValue("@id", uid);
                currentHash = (string)(await getPwd.ExecuteScalarAsync() ?? "");
            }
            if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, currentHash))
                return Unauthorized("Current password is incorrect.");

            var newHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
            await using var updPwd = new NpgsqlCommand(
                "UPDATE users SET password =@p WHERE id=@id;", conn, tx);
            updPwd.Parameters.AddWithValue("@p", newHash);
            updPwd.Parameters.AddWithValue("@id", uid);
            await updPwd.ExecuteNonQueryAsync();


            // phone update (optional)
            if (req.Phone is not null)
            {
                await using var updPhone = new NpgsqlCommand(
                    "UPDATE users SET phone=@ph WHERE id=@id;", conn, tx);
                updPhone.Parameters.AddWithValue("@ph", (object?)req.Phone ?? DBNull.Value);
                updPhone.Parameters.AddWithValue("@id", uid);
                await updPhone.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return NoContent();
        }


        // ========= ADMIN =========

        // GET /api/users  (Admin only)

        [Authorize(Roles = "Admin")]
        [HttpGet()]
        public async Task<ActionResult<IEnumerable<UserRowDto>>> List([FromQuery] int limit = 50, [FromQuery] int offset = 0)
        {
            await using var conn = await _db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                @"SELECT id, email, phone, status, created_at
              FROM users ORDER BY id DESC LIMIT @lim OFFSET @off;", conn);
            cmd.Parameters.AddWithValue("@lim", limit);
            cmd.Parameters.AddWithValue("@off", offset);

            var list = new List<UserRowDto>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                list.Add(new UserRowDto
                {
                    Id = rd.GetInt64(0),
                    Email = rd.GetString(1),
                    Phone = rd.IsDBNull(2) ? null : rd.GetString(2),
                    Status = rd.GetString(3),
                    CreatedAt = rd.GetDateTime(4)
                });
            }
            return list;
        }


        // GET /api/users/{id}/roles
        [Authorize(Policy = "perm:Manage_Users")]
        [HttpGet("{id:long}/roles")]
        public async Task<ActionResult<IEnumerable<string>>> GetRoles(long id)
        {
            await using var conn = await _db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                @"SELECT r.name
              FROM userroles ur JOIN roles r ON r.id = ur.role_id
              WHERE ur.user_id=@u ORDER BY r.name;", conn);
            cmd.Parameters.AddWithValue("@u", id);

            var roles = new List<string>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) roles.Add(rd.GetString(0));
            return roles;
        }



        public record UpdateMeRequest(string? Phone, string? CurrentPassword, string? NewPassword);

        public record UserRowDto
        {
            public long Id { get; set; }
            public string Email { get; set; } = default!;
            public string? Phone { get; set; }
            public string Status { get; set; } = default!;
            public DateTime CreatedAt { get; set; }
        }

        public record UserMeDto : UserRowDto
        {
            public string[] Roles { get; set; } = Array.Empty<string>();
            public string[] Permissions { get; set; } = Array.Empty<string>();
        }
    }
}
