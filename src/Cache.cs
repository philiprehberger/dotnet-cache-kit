namespace Philiprehberger.CacheKit;

internal class CacheEntry<V>
{
    public V Value { get; set; } = default!;
    public DateTime? ExpiresAt { get; set; }
    public HashSet<string> Tags { get; set; } = new();
}

public class Cache<V>
{
    private readonly Dictionary<string, CacheEntry<V>> _items;
    private readonly LinkedList<string> _order = new();
    private readonly int _maxSize;
    private readonly TimeSpan? _defaultTtl;
    private readonly object _lock = new();

    public Cache(int maxSize = 1000, TimeSpan? defaultTtl = null)
    {
        _maxSize = maxSize;
        _defaultTtl = defaultTtl;
        _items = new Dictionary<string, CacheEntry<V>>(maxSize);
    }

    public int Size
    {
        get { lock (_lock) return _items.Count; }
    }

    public void Set(string key, V value, TimeSpan? ttl = null, IEnumerable<string>? tags = null)
    {
        lock (_lock)
        {
            var effectiveTtl = ttl ?? _defaultTtl;
            var expiresAt = effectiveTtl.HasValue ? DateTime.UtcNow + effectiveTtl.Value : (DateTime?)null;
            var tagSet = tags != null ? new HashSet<string>(tags) : new HashSet<string>();

            if (_items.ContainsKey(key))
            {
                _items[key] = new CacheEntry<V> { Value = value, ExpiresAt = expiresAt, Tags = tagSet };
                _order.Remove(key);
                _order.AddFirst(key);
            }
            else
            {
                if (_items.Count >= _maxSize)
                    Evict();

                _items[key] = new CacheEntry<V> { Value = value, ExpiresAt = expiresAt, Tags = tagSet };
                _order.AddFirst(key);
            }
        }
    }

    public V? Get(string key)
    {
        lock (_lock)
        {
            if (!_items.TryGetValue(key, out var entry))
                return default;

            if (entry.ExpiresAt.HasValue && DateTime.UtcNow > entry.ExpiresAt.Value)
            {
                Remove(key);
                return default;
            }

            _order.Remove(key);
            _order.AddFirst(key);
            return entry.Value;
        }
    }

    public bool TryGet(string key, out V? value)
    {
        lock (_lock)
        {
            if (!_items.TryGetValue(key, out var entry))
            {
                value = default;
                return false;
            }

            if (entry.ExpiresAt.HasValue && DateTime.UtcNow > entry.ExpiresAt.Value)
            {
                Remove(key);
                value = default;
                return false;
            }

            _order.Remove(key);
            _order.AddFirst(key);
            value = entry.Value;
            return true;
        }
    }

    public bool Has(string key)
    {
        lock (_lock)
        {
            if (!_items.TryGetValue(key, out var entry)) return false;
            if (entry.ExpiresAt.HasValue && DateTime.UtcNow > entry.ExpiresAt.Value)
            {
                Remove(key);
                return false;
            }
            return true;
        }
    }

    public bool Delete(string key)
    {
        lock (_lock) return Remove(key);
    }

    public int InvalidateByTag(string tag)
    {
        lock (_lock)
        {
            var keysToRemove = _items
                .Where(kv => kv.Value.Tags.Contains(tag))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in keysToRemove)
                Remove(key);

            return keysToRemove.Count;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
            _order.Clear();
        }
    }

    public List<string> Keys()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            return _items
                .Where(kv => !kv.Value.ExpiresAt.HasValue || now <= kv.Value.ExpiresAt.Value)
                .Select(kv => kv.Key)
                .ToList();
        }
    }

    private bool Remove(string key)
    {
        if (_items.Remove(key))
        {
            _order.Remove(key);
            return true;
        }
        return false;
    }

    private void Evict()
    {
        var now = DateTime.UtcNow;
        var expired = _items.FirstOrDefault(kv => kv.Value.ExpiresAt.HasValue && now > kv.Value.ExpiresAt.Value);
        if (expired.Key != null)
        {
            Remove(expired.Key);
            return;
        }

        if (_order.Last != null)
            Remove(_order.Last.Value);
    }
}
