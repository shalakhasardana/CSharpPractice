using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UserService.Infrastructure;
using UserService.Models;

namespace UserService.Repositories
{
    public sealed class UserRepository
    {
        private readonly IDbConnectionFactory _db;
        public UserRepository(IDbConnectionFactory db) => _db = db;

        public async Task<long> Create(string email, string hash, string? phone, CancellationToken ct)
        {
            await using var conn = await _db.CreateAsync(ct);
            var id = await conn.ExecuteScalarAsync<long>(
                @"INSERT INTO users(email, password_hash, phone, status)
                  VALUES(@Email, @Hash, @Phone, 'ACTIVE') RETURNING id;",
                new { Email = email.Trim().ToLowerInvariant(), Hash = hash, Phone = phone });
            return id;
        }

        public async Task<UserSummary?> GetById(long id, CancellationToken ct)
        {
            await using var conn = await _db.CreateAsync(ct);
            return await conn.QuerySingleOrDefaultAsync<UserSummary>(
                "SELECT id, email, phone, status FROM users WHERE id=@id", new { id });
        }
    }
}
