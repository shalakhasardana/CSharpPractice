using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace UserService.Infrastructure
{
    public static class Json
    {
        public static async Task<T?> Read<T>(HttpListenerRequest req)
        {
            using var r = new System.IO.StreamReader(req.InputStream, req.ContentEncoding);
            return JsonSerializer.Deserialize<T>(await r.ReadToEndAsync());
        }

        public static async Task Write(HttpListenerResponse res, object obj)
        {
            var payload = JsonSerializer.Serialize(obj);
            var bytes = Encoding.UTF8.GetBytes(payload);
            res.ContentType = "application/json";
            await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }
    }
}
