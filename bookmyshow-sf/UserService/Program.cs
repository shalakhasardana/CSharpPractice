using Microsoft.Extensions.Configuration;
using Microsoft.ServiceFabric.Services.Runtime;
using System;
using System.Diagnostics;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;

namespace UserService
{
    internal static class Program
    {
        /// <summary>
        /// This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            try
            {
                // Build config once for the host process
                var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

                // Optional: read resolved config too
                var cfg = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build();

                string Mask(string s) => string.IsNullOrEmpty(s) ? s :
                    s.Length <= 8 ? "********" : s.Substring(0, 4) + "…" + s.Substring(s.Length - 4);

                var keys = new[] {
    "ASPNETCORE_ENVIRONMENT",
    "Bms__Environment","Bms__ClusterMode","Bms__DbProvider",
    "Bms__PostgresConnection","Bms__SqlServerConnection",
    "Jwt__Issuer","Jwt__Audience","Jwt__Secret"
};

                Console.WriteLine("=== BOOT ENV VARS ===");
                foreach (var k in keys)
                {
                    var v = Environment.GetEnvironmentVariable(k);
                    var toShow = (k.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                                  k.Contains("Connection", StringComparison.OrdinalIgnoreCase))
                                 ? Mask(v) : v;
                    Console.WriteLine($"ENV {k} = {toShow}");
                }
                Console.Out.Flush(); // ensure it hits stdout log

                ServiceRuntime.RegisterServiceAsync(
                    serviceTypeName: "UserServiceType",
                    serviceFactory: ctx => new UserStatelessService(ctx, cfg)
                ).GetAwaiter().GetResult();



                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceHostInitializationFailed(e.ToString());
                throw;
            }
        }
    }
}
