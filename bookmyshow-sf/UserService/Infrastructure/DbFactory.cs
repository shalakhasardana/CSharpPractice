using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UserService.Infrastructure
{
    public sealed class DbFactory
    {
        public NpgsqlConnection Create()
        {
            var b = new NpgsqlConnectionStringBuilder
            {
                Host = Env("Database__Host", "localhost"),
                Port = int.TryParse(Env("Database__Port", "5432"), out var p) ? p : 5432,
                Username = Env("Database__User", "postgres"),
                Password = Env("Database__Password", "postgres"),
                Database = Env("Database__Name", "users_db"),
                SslMode = Enum.TryParse(Env("Database__SslMode", "Disable"), out SslMode ssl) ? ssl : SslMode.Disable
            };
            return new NpgsqlConnection(b.ConnectionString);
        }

        private static string Env(string k, string d) => Environment.GetEnvironmentVariable(k) ?? d;
    }
}
