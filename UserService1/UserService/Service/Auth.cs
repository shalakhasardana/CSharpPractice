using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace UserService.Service
{
    public interface IAuthService
    {
        Task<AuthResponse> RegisterAsync(RegisterRequest req);
        Task<AuthResponse?> LoginAsync(string email, string password);
    }

    public interface ITokenService
    {
        Task<AuthResponse> CreateAsync(long userId, string email,
            IEnumerable<string> roles, IEnumerable<string> permissions);
    }

    // ===== DTOs =====
    public record RegisterRequest(string Email, string Password, string? Phone);
    public record LoginRequest(string Email, string Password);
    public record AuthResponse
    {
        public long UserId { get; set; }
        public string Email { get; set; } = default!;
        public string Token { get; set; } = default!;
        public DateTime ExpiresAtUtc { get; set; }
        public string[] Roles { get; set; } = Array.Empty<string>();
        public string[] Permissions { get; set; } = Array.Empty<string>();
    }

    public class TokenService : ITokenService
    {
        private readonly IConfiguration _cfg;
        public TokenService(IConfiguration cfg)
        {
            _cfg = cfg;
        }
        public Task<AuthResponse> CreateAsync(long userId, string email,
            IEnumerable<string> roles, IEnumerable<string> perms)
        {
            var jwt = _cfg.GetSection("Jwt");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddMinutes(int.Parse(jwt["ExpiresMinutes"]!));

            var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
            // role claims
            foreach (var r in roles.Distinct(StringComparer.OrdinalIgnoreCase))
                claims.Add(new Claim(ClaimTypes.Role, r));
            // permission claims (custom)
            foreach (var p in perms)
                claims.Add(new Claim("perm", p));

            var token = new JwtSecurityToken(
                issuer: jwt["Issuer"],
                audience: jwt["Audience"],
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expires,
                signingCredentials: creds);

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            var res = new AuthResponse
            {
                UserId = userId,
                Email = email,
                Token = tokenString,
                ExpiresAtUtc = expires,
                Roles = roles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Permissions = perms.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
            return Task.FromResult(res);
        }
    }

    public class Auth : IAuthService
    {
        private readonly NpgsqlDataSource _db;
        private readonly ITokenService _token;

        public Auth(NpgsqlDataSource db, ITokenService token)
        {
            _db = db;
            _token = token;
        }

        public async Task<AuthResponse?> LoginAsync(string email, string password)
        {
            await using var conn = await _db.OpenConnectionAsync();

            string hash;
            string status;
            long userId;

            await using (var cmd = new NpgsqlCommand(
                @"SELECT id, email, password , status
              FROM users
              WHERE lower(email) = lower(@e) LIMIT 1;", conn))
            {
                cmd.Parameters.AddWithValue("@e", email);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    throw new ArgumentException("Invalid email or password.");

                userId = reader.GetInt64(0);
                email = reader.GetString(1);
                hash = reader.GetString(2);
                status = reader.GetString(3);
            }

            /*
            if (!BCrypt.Net.BCrypt.Verify(req.Password, hash))
                return Unauthorized("Invalid email or password.");
            */
            if (!string.Equals(status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("User is not active.");


            // load claims & build token
            var roles = await GetRolesAsync(conn, userId);
            var perms = await GetPermissionsAsync(conn, userId);
            return await _token.CreateAsync(userId, email.Trim().ToLowerInvariant(), roles, perms);
        }

        public async Task<AuthResponse> RegisterAsync(RegisterRequest req)
        {

            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                throw new ArgumentException("Email and password are required.");

            var hash = BCrypt.Net.BCrypt.HashPassword(req.Password);

            await using var conn = await _db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                long userId;
                await using (var cmd = new NpgsqlCommand(
                    @"INSERT INTO users (email, password , phone, status)
                  VALUES (lower(@email), @pwd, @phone, 'ACTIVE')
                  RETURNING id;", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@email", req.Email.Trim().ToLowerInvariant());
                    cmd.Parameters.AddWithValue("@pwd", hash);
                    cmd.Parameters.AddWithValue("@phone", (object?)req.Phone ?? DBNull.Value);
                    var res = await cmd.ExecuteScalarAsync();
                    userId = (long)res!;
                }

                // Assign Customer role by default
                int? roleId = null;
                await using (var cmd = new NpgsqlCommand(
                    "SELECT id FROM roles WHERE name = 'Customer' LIMIT 1;", conn, tx))
                {
                    var r = await cmd.ExecuteScalarAsync();
                    roleId = r is null ? (int?)null : Convert.ToInt32(r);
                }
                if (roleId.HasValue)
                {
                    await using var link = new NpgsqlCommand(
                        "INSERT INTO user_roles (user_id, role_id) VALUES (@u,@r) ON CONFLICT DO NOTHING;",
                        conn, tx);
                    link.Parameters.AddWithValue("@u", userId);
                    link.Parameters.AddWithValue("@r", roleId.Value);
                    await link.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                // load claims & build token
                var roles = await GetRolesAsync(conn, userId);
                var perms = await GetPermissionsAsync(conn, userId);
                return await _token.CreateAsync(userId, req.Email.Trim().ToLowerInvariant(), roles, perms);
            }
            catch (PostgresException pg) when (pg.SqlState == "23505") // unique_violation
            {
                await tx.RollbackAsync();
                throw new ArgumentException("Duplicate email exception.");
            }
        }

        private static async Task<string[]> GetRolesAsync(NpgsqlConnection conn, long userId)
        {
            var list = new List<string>();
            await using var cmd = new NpgsqlCommand(@"
            SELECT r.name
            FROM user_roles ur JOIN roles r ON r.id = ur.role_id
            WHERE ur.user_id=@u;", conn);
            cmd.Parameters.AddWithValue("@u", userId);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) list.Add(rd.GetString(0));
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static async Task<string[]> GetPermissionsAsync(NpgsqlConnection conn, long userId)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var cmd = new NpgsqlCommand(@"
            SELECT p.name
            FROM user_roles ur
            JOIN role_permissions rp ON rp.role_id = ur.role_id
            JOIN permissions p ON p.id = rp.permission_id
            WHERE ur.user_id=@u;", conn);
            cmd.Parameters.AddWithValue("@u", userId);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) set.Add(rd.GetString(0));
            return set.ToArray();
        }
    }
}
