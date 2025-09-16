using Npgsql;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UserService.Infrastructure
{
    public interface IDbConnectionFactory
    {
        Task<DbConnection> CreateAsync(CancellationToken ct = default);
    }

    public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
    {
        private readonly string _cs;
        public NpgsqlConnectionFactory(string cs) => _cs = cs;

        public async Task<DbConnection> CreateAsync(CancellationToken ct = default)
        {
            var conn = new Npgsql.NpgsqlConnection(_cs);
            await conn.OpenAsync(ct);  // open here
            return conn;               // return open connection
        }
    }

    public sealed class SqlConnectionFactory(string cs) : IDbConnectionFactory
    {
        public async Task<DbConnection> CreateAsync(CancellationToken ct = default)
        {
            ///var c = new SqlConnection(cs);
            //await c.OpenAsync(ct);
            //return c;
            await Task.Delay(0);
            return null;
        }
    }
}
