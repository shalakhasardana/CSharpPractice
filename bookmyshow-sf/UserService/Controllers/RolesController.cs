using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UserService.Infrastructure;
using static UserService.CompositionRoot;

namespace UserService.Controllers
{
    public static class RolesController
    {
        public static async Task List(App app, HttpListenerContext ctx, CancellationToken ct)
        {
            var rows = await app.RoleLogic.List(ct);
            await Json.Write(ctx.Response, rows);
        }

        public static async Task Create(App app, HttpListenerContext ctx, CancellationToken ct)
        {
            var dto = await Json.Read<Models.CreateRoleDto>(ctx.Request);
            if (dto == null || string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            { ctx.Response.StatusCode = 400; await Json.Write(ctx.Response, new { error = "code & name required" }); return; }

            await app.RoleLogic.Create(dto.Code, dto.Name, ct);
            ctx.Response.StatusCode = 201;
        }
    }
}
