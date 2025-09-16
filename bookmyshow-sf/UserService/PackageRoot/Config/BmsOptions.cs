using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UserService.PackageRoot.Config
{
    public sealed class BmsOptions
    {
        public string Environment { get; set; } = "dev";
        public string ClusterMode { get; set; } = "Local";
        public bool ReverseProxyEnabled { get; set; } = false;

        public string DbProvider { get; set; } = "Postgres"; // Postgres | SqlServer
        public string PostgresConnection { get; set; } = "";
        public string SqlServerConnection { get; set; } = "";

        public string PublicBaseUrl { get; set; } = "";
    }
}
