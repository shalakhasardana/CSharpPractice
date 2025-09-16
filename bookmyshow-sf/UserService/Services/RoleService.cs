using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UserService.Repositories;

namespace UserService.Services
{
    public sealed class RoleService
    {
        private readonly RoleRepository _roles;
        private readonly UserRepository _users;
        public RoleService(RoleRepository roles, UserRepository users) { _roles = roles; _users = users; }

        public Task<IEnumerable<object>> List(CancellationToken ct) => _roles.List(ct);
        public Task Create(string code, string name, CancellationToken ct) => _roles.Create(code, name, ct);
        public Task AssignRole(long userId, string roleCode, CancellationToken ct) => _roles.Assign(userId, roleCode, ct);
    }
}
