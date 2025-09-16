using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UserService.Infrastructure;

namespace UserService.Repositories
{
    public sealed class RoleRepository
    {
        private readonly IDbConnectionFactory _db;
        public RoleRepository(IDbConnectionFactory db) => _db = db;

        public async Task<IEnumerable<object>> List(CancellationToken ct)
        {
            await using var conn = await  _db.CreateAsync(ct); await conn.OpenAsync(ct);
            return await conn.QueryAsync("SELECT id, code, name FROM roles ORDER BY id");
        }

        public async Task Create(string code, string name, CancellationToken ct)
        {
            await using var conn = await _db.CreateAsync(ct); await conn.OpenAsync(ct);
            await conn.ExecuteAsync("INSERT INTO roles(code, name) VALUES(@code,@name)", new { code = code.Trim().ToUpperInvariant(), name });
        }

        public async Task Assign(long userId, string roleCode, CancellationToken ct)
        {
            await using var conn = await _db.CreateAsync(ct); await conn.OpenAsync(ct);
            var roleId = await conn.ExecuteScalarAsync<int>("SELECT id FROM roles WHERE code=@c", new { c = roleCode.Trim().ToUpperInvariant() });
            await conn.ExecuteAsync("INSERT INTO user_roles(user_id, role_id) VALUES(@u,@r) ON CONFLICT DO NOTHING", new { u = userId, r = roleId });
        }
    }
}
