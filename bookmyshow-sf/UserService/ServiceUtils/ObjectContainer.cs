using System.Collections.Concurrent;

namespace UserService.ServiceUtils
{
    public sealed class ObjectContainer
    {
        private static readonly Lazy<ObjectContainer> _instance =
            new(() => new ObjectContainer(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static ObjectContainer Instance => _instance.Value;

        private readonly ConcurrentDictionary<Type, Lazy<object>> _map =
           new();

        private ObjectContainer() { }

        // Register a lazy factory (preferred)
        public void RegisterFactory<T>(Func<T> factory, bool overwrite = true)
        {
            var key = typeof(T);
            var lazy = new Lazy<object>(() => factory(),
                LazyThreadSafetyMode.ExecutionAndPublication);

            if (overwrite) _map[key] = lazy;
            else _map.GetOrAdd(key, lazy);
        }

        // Register an already-built instance (will still be returned lazily)
        public void RegisterInstance<T>(T instance, bool overwrite = true)
        {
            RegisterFactory(() => instance!, overwrite);
        }

        public T Get<T>() where T : class
        {
            if (!_map.TryGetValue(typeof(T), out var lazy))
                throw new InvalidOperationException($"{typeof(T).Name} is not registered.");
            return (T)lazy.Value;
        }

        // Test convenience
        public void Clear() => _map.Clear();
    }
}
