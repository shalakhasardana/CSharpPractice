using Microsoft.Extensions.Caching.Memory;

namespace EventService.Cache
{
    public sealed class AppCache
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<AppCache> _log;
        public AppCache(IMemoryCache cache, ILogger<AppCache> log)
        {
            _cache = cache;
            _log = log;
        }

        public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
        {
            if (_cache.TryGetValue<T>(key, out var hit)) return hit!;
            var val = await factory();
            _cache.Set(key, val, ttl);
            return val;
        }

        public void Remove(string key) => _cache.Remove(key);
    }

    public static class CacheKeys
    {
        public static string ShowMeta(long showId) => $"show:{showId}:meta";
        public static string AuditoriumLayout(long audId) => $"auditorium:{audId}:layout";
        public static string ShowPrices(long showId, int v) => $"show:{showId}:prices:v{v}";
        public static string ShowAvailability(long showId, int v) => $"show:{showId}:availability:v{v}";
    }


}
