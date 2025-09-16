using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UserService.Infrastructure;
using UserService.Repositories;
using UserService.Services;

namespace UserService
{
    public class CompositionRoot
    {
        public sealed class App
        {
            public IDbConnectionFactory Db { get; }
            public IConfiguration Cfg { get; }
            public readonly AuthService Auth;
            public readonly UserRepository Users;
            public readonly RoleRepository Roles;
            public readonly UserServiceLogic UserLogic;
            public readonly RoleService RoleLogic;

            public App(IDbConnectionFactory db, IConfiguration cfg)
            {
                Db = db;
                Cfg = cfg;               // reads env vars
                Auth = new AuthService();             // reads env vars (JWT)
                Users = new UserRepository(Db);
                Roles = new RoleRepository(Db);
                UserLogic = new UserServiceLogic(Users, Roles);
                RoleLogic = new RoleService(Roles, Users);
            }
        }
    }
}
