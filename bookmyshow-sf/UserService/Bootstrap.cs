using Microsoft.Extensions.Configuration;
using UserService.Infrastructure;
using UserService.PackageRoot.Config;
using UserService.Repositories;
using UserService.Services;
using UserService.ServiceUtils;

namespace UserService
{

    /// <summary>
    /// Registers everything lazily in ObjectContainer.
    /// Call Bootstrap.ConfigureProd() once at service startup.
    /// </summary>
    public static class Bootstrap
    {
        public static void ConfigureProd()
        {
            var c = ObjectContainer.Instance;

            // IConfiguration – lazy
            c.RegisterFactory<IConfiguration>(() =>
                new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                    .AddJsonFile("appsettings.azure.json", optional: true, reloadOnChange: false)
                    .AddEnvironmentVariables()
                    .Build());

            // Bind BmsOptions lazily (so we can choose DB provider)
            c.RegisterFactory<BmsOptions>(() =>
            {
                var cfg = c.Get<IConfiguration>();
                var bms = new BmsOptions();
                cfg.GetSection("Bms").Bind(bms);
                return bms;
            });

            // IDbConnectionFactory chosen lazily from BmsOptions
            c.RegisterFactory<IDbConnectionFactory>(() =>
            {
                var bms = c.Get<BmsOptions>();

                if (string.Equals(bms.DbProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
                    return (IDbConnectionFactory)new SqlConnectionFactory(bms.SqlServerConnection);

                // default: Postgres
                return new NpgsqlConnectionFactory(bms.PostgresConnection);
            });

            c.RegisterFactory<AuthService>(() =>new AuthService());

            // Repositories (lazy)
            c.RegisterFactory<UserRepository>(() =>
                new UserRepository(c.Get<IDbConnectionFactory>()));

            c.RegisterFactory<RoleRepository>(() =>
                new RoleRepository(c.Get<IDbConnectionFactory>()));

            // Domain logic (lazy)
            c.RegisterFactory<UserServiceLogic>(() =>
                new UserServiceLogic(c.Get<UserRepository>(), c.Get<RoleRepository>()));

            c.RegisterFactory<RoleService>(() =>
                new RoleService(c.Get<RoleRepository>(), c.Get<UserRepository>()));
        }
    }
}