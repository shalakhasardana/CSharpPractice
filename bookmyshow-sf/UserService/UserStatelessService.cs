using Microsoft.Extensions.Configuration;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using System.Fabric;
using System.Net;
using UserService.Infrastructure;
using UserService.PackageRoot.Config;
using UserService.Routing;
using static UserService.CompositionRoot;

namespace UserService
{
    /// <summary>
    /// An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    internal sealed class UserStatelessService : StatelessService
    {
        private readonly App _app = default!;
        private readonly MiniRouter _router = new();

        public UserStatelessService(StatelessServiceContext context, IConfiguration cfg)
            : base(context)
        {
            // Bind options (appsettings + env)
            var bms = new BmsOptions();
            cfg.GetSection("Bms").Bind(bms);

            // Choose DB provider by config
            IDbConnectionFactory dbFactory =
                string.Equals(bms.DbProvider, "SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? new SqlConnectionFactory(bms.SqlServerConnection)
                    : new NpgsqlConnectionFactory(bms.PostgresConnection);

            // Your CompositionRoot.App can be extended to take a factory (or wrap it)
            _app = new App(dbFactory, cfg); // adapt ctor in CompositionRoot if needed

            _router
                .Map("GET", "/api/health", (c, t) => Controllers.HealthController.Health(c, t))
                .Map("POST", "/api/auth/login", (c, t) => Controllers.AuthController.Login(_app, c, t))
                .Map("POST", "/api/users", (c, t) => Controllers.UsersController.Create(_app, c, t))
                .Map("GET", "/api/users/{id}", (c, t) => Controllers.UsersController.GetById(_app, c, t))
                .Map("GET", "/api/roles", (c, t) => Controllers.RolesController.List(_app, c, t))
                .Map("POST", "/api/roles", (c, t) => Controllers.RolesController.Create(_app, c, t))
                .Map("POST", "/api/users/{id}/roles", (c, t) => Controllers.UsersController.AssignRole(_app, c, t));

        }

        /// <summary>
        /// Optional override to create listeners (e.g., TCP, HTTP) for this service replica to handle client or user requests.
        /// </summary>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            yield return new ServiceInstanceListener(
                ctx => new HttpListenerCommunicationListener(ctx, "UserServiceHttp", HandleAsync));
        }

        private async Task HandleAsync(HttpListenerContext http, CancellationToken ct)
        {
            try
            {
                if (_router.TryResolve(http.Request.HttpMethod, http.Request.Url.AbsolutePath, out var h, out var match) && h != null)
                {
                    if (match != null) http.Request.Headers["__route_params"] = match.Value; // optional hook
                    await h(http, ct);
                }
                else
                {
                    http.Response.StatusCode = 404;
                    await Infrastructure.Json.Write(http.Response, new { error = "not found" });
                }
            }
            catch (Exception ex)
            {
                http.Response.StatusCode = 500;
                await Infrastructure.Json.Write(http.Response, new { error = ex.Message });
            }
            finally { http.Response.OutputStream.Close(); }
        }

        /// <summary>
        /// This is the main entry point for your service instance.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service instance.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            // TODO: Replace the following sample code with your own logic 
            //       or remove this RunAsync override if it's not needed in your service.

            long iterations = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ServiceEventSource.Current.ServiceMessage(this.Context, "Working-{0}", ++iterations);

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }
}
