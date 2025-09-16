using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UserService.Infrastructure;
using UserService.Repositories;
using UserService.Services;
using UserService.ServiceUtils;

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

            public App()
            {
                // Everything is lazy-resolved the first time it’s touched.
                var c = ObjectContainer.Instance;
                Cfg = c.Get<IConfiguration>();
                Db = c.Get<IDbConnectionFactory>();
                Auth = c.Get<AuthService>();
                Users = c.Get<UserRepository>();
                Roles = c.Get<RoleRepository>();
                UserLogic = c.Get<UserServiceLogic>();
                RoleLogic = c.Get<RoleService>();
            }
        }
    }
}
