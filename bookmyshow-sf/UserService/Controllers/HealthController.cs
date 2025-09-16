using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UserService.Infrastructure;

namespace UserService.Controllers
{
    public static class HealthController
    {
        public static async Task Health(HttpListenerContext ctx, CancellationToken ct)
        {
            await Json.Write(ctx.Response, new { status = "ok", env = System.Environment.GetEnvironmentVariable("BMS_ENV") ?? "dev" });
        }
    }
}
