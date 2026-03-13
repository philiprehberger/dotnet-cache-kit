namespace Philiprehberger.CacheKit;

public record CacheStats
{
    public long Hits { get; init; }
    public long Misses { get; init; }
    public long Evictions { get; init; }
    public double HitRate => Hits + Misses == 0 ? 0.0 : (double)Hits / (Hits + Misses);
}

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

    private long _hits;
    private long _misses;
    private long _evictions;

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

    public CacheStats Stats
    {
        get
        {
            lock (_lock)
            {
                return new CacheStats
                {
                    Hits = _hits,
                    Misses = _misses,
                    Evictions = _evictions
                };
            }
        }
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
            {
                _misses++;
                return default;
            }

            if (entry.ExpiresAt.HasValue && DateTime.UtcNow > entry.ExpiresAt.Value)
            {
                Remove(key);
                _misses++;
                return default;
            }

            _order.Remove(key);
            _order.AddFirst(key);
            _hits++;
            return entry.Value;
        }
    }

    public bool TryGet(string key, out V? value)
    {
        lock (_lock)
        {
            if (!_items.TryGetValue(key, out var entry))
            {
                _misses++;
                value = default;
                return false;
            }

            if (entry.ExpiresAt.HasValue && DateTime.UtcNow > entry.ExpiresAt.Value)
            {
                Remove(key);
                _misses++;
                value = default;
                return false;
            }

            _order.Remove(key);
            _order.AddFirst(key);
            _hits++;
            value = entry.Value;
            return true;
        }
    }

    public V GetOrSet(string key, Func<V> factory, TimeSpan? ttl = null)
    {
        lock (_lock)
        {
            if (_items.TryGetValue(key, out var entry))
            {
                if (!entry.ExpiresAt.HasValue || DateTime.UtcNow <= entry.ExpiresAt.Value)
                {
                    _order.Remove(key);
                    _order.AddFirst(key);
                    _hits++;
                    return entry.Value;
                }

                Remove(key);
            }

            _misses++;
            var value = factory();

            var effectiveTtl = ttl ?? _defaultTtl;
            var expiresAt = effectiveTtl.HasValue ? DateTime.UtcNow + effectiveTtl.Value : (DateTime?)null;

            if (_items.Count >= _maxSize)
                Evict();

            _items[key] = new CacheEntry<V> { Value = value, ExpiresAt = expiresAt };
            _order.AddFirst(key);

            return value;
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

    public int DeleteWhere(Func<string, V, bool> predicate)
    {
        lock (_lock)
        {
            var keysToRemove = _items
                .Where(kv => predicate(kv.Key, kv.Value.Value))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in keysToRemove)
                Remove(key);

            return keysToRemove.Count;
        }
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
            _evictions++;
            return;
        }

        if (_order.Last != null)
        {
            Remove(_order.Last.Value);
            _evictions++;
        }
    }
}
