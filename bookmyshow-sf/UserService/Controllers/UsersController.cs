using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UserService.Infrastructure;
using UserService.Models;
using static UserService.CompositionRoot;

namespace UserService.Controllers
{
    public static class UsersController
    {
        public static async Task Create(App app, HttpListenerContext ctx, CancellationToken ct)
        {
            var dto = await Json.Read<RegisterDto>(ctx.Request);
            if (dto == null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            { ctx.Response.StatusCode = 400; await Json.Write(ctx.Response, new { error = "email & password required" }); return; }

            var id = await app.UserLogic.CreateUser(dto.Email, dto.Password, dto.Phone, ct);
            ctx.Response.StatusCode = 201;
            await Json.Write(ctx.Response, new { id, email = dto.Email });
        }

        public static async Task GetById(App app, HttpListenerContext ctx, CancellationToken ct)
        {
            var idStr = ctx.Request.Url.Segments[^1];
            if (!long.TryParse(idStr, out var id)) { ctx.Response.StatusCode = 400; await Json.Write(ctx.Response, new { error = "bad id" }); return; }

            var user = await app.UserLogic.GetById(id, ct);
            if (user == null) { ctx.Response.StatusCode = 404; await Json.Write(ctx.Response, new { error = "not found" }); return; }
            await Json.Write(ctx.Response, user);
        }

        public static async Task AssignRole(App app, HttpListenerContext ctx, CancellationToken ct)
        {
            var idStr = ctx.Request.Url.Segments[^2].TrimEnd('/'); // .../users/{id}/roles
            if (!long.TryParse(idStr, out var id)) { ctx.Response.StatusCode = 400; await Json.Write(ctx.Response, new { error = "bad id" }); return; }
            var dto = await Json.Read<AssignRoleDto>(ctx.Request);
            if (dto == null || string.IsNullOrWhiteSpace(dto.RoleCode)) { ctx.Response.StatusCode = 400; await Json.Write(ctx.Response, new { error = "roleCode required" }); return; }

            await app.RoleLogic.AssignRole(id, dto.RoleCode, ct);
            ctx.Response.StatusCode = 204;
        }
    }
}
