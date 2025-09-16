using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UserService.Routing
{
    public delegate Task Handler(HttpListenerContext ctx, CancellationToken ct);

    public sealed class MiniRouter
    {
        private readonly List<(string method, Regex path, Handler handler)> _routes = new();

        public MiniRouter Map(string method, string template, Handler handler)
        {
            // convert "/api/users/{id}" to regex ^/api/users/(?<id>[^/]+)$
            var re = "^" + Regex.Replace(template, "{([a-zA-Z0-9_]+)}", "(?<$1>[^/]+)") + "$";
            _routes.Add((method.ToUpperInvariant(), new Regex(re, RegexOptions.Compiled), handler));
            return this;
        }

        public bool TryResolve(string method, string path, out Handler? handler, out Match? match)
        {
            foreach (var r in _routes)
            {
                if (r.method == method.ToUpperInvariant())
                {
                    var m = r.path.Match(path);
                    if (m.Success) { handler = r.handler; match = m; return true; }
                }
            }
            handler = null; match = null; return false;
        }
    }
}
