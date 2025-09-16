using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UserService.Models;
using UserService.Repositories;

namespace UserService.Services
{
    public sealed class UserServiceLogic
    {
        private readonly UserRepository _users;
        private readonly RoleRepository _roles;

        public UserServiceLogic(UserRepository users, RoleRepository roles) { _users = users; _roles = roles; }

        public Task<long> CreateUser(string email, string password, string? phone, CancellationToken ct) =>
            _users.Create(email, BCrypt.Net.BCrypt.HashPassword(password, 11), phone, ct);

        public Task<UserSummary?> GetById(long id, CancellationToken ct) => _users.GetById(id, ct);


    }
}
