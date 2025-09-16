using Dapper;
using System.Net;
using UserService.Infrastructure;
using UserService.Models;
using static UserService.CompositionRoot;

namespace UserService.Controllers
{
    public static class AuthController
    {
        public static async Task Login(App app, HttpListenerContext ctx, CancellationToken ct)
        {
            var dto = await Json.Read<LoginDto>(ctx.Request);
            if (dto == null) { ctx.Response.StatusCode = 400; await Json.Write(ctx.Response, new { error = "bad payload" }); return; }

            await using var db = await app.Db.CreateAsync(ct);
            var row = await db.QuerySingleOrDefaultAsync<(long id, string password_hash)>(
                "SELECT id, password_hash FROM users WHERE email=@e AND status='ACTIVE'",
                new { e = dto.Email.Trim().ToLowerInvariant() });

            if (row.id == 0 || !BCrypt.Net.BCrypt.Verify(dto.Password, row.password_hash))
            { ctx.Response.StatusCode = 401; await Json.Write(ctx.Response, new { error = "invalid credentials" }); return; }

            var token = app.Auth.Issue(row.id.ToString());
            await Json.Write(ctx.Response, new { access_token = token, token_type = "Bearer" });
        }
    }
}
