using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace UserService.Services
{
    public sealed class AuthService
    {
        private readonly string _issuer = Env("Jwt__Issuer", "bms");
        private readonly string _aud = Env("Jwt__Audience", "bms-users");
        private readonly string _secret = Env("Jwt__Secret", "dev-secret");
        public AuthService()
        {
            _secret = Env("Jwt__Secret", "");
            if (string.IsNullOrWhiteSpace(_secret) || Encoding.UTF8.GetBytes(_secret).Length < 16)
                throw new InvalidOperationException("Jwt__Secret must be at least 16 bytes. Set it via ApplicationParameters.");
    }

        public string Issue(string sub)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(_issuer, _aud, new[] { new Claim("sub", sub) },
                notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.AddHours(12), signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static string Env(string k, string d) => Environment.GetEnvironmentVariable(k) ?? d;
    }
}
