using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
    private Action<string, V>? _onEvict;

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
                RemoveWithEvict(key, entry.Value);
                _misses++;
                return default;
            }

            _order.Remove(key);
            _order.AddFirst(key);
            _hits++;
            return entry.Value;
        }
    }

    /// <summary>
    /// Tries to get a value. Returns true if found and not expired, false otherwise.
    /// </summary>
    /// <param name="key">The cache key to look up.</param>
    /// <param name="value">The cached value if found; otherwise, the default value.</param>
    /// <returns>True if the key was found and not expired; false otherwise.</returns>
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
                RemoveWithEvict(key, entry.Value);
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

                RemoveWithEvict(key, entry.Value);
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
                RemoveWithEvict(key, entry.Value);
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
            var entriesToRemove = _items
                .Where(kv => predicate(kv.Key, kv.Value.Value))
                .Select(kv => new { kv.Key, kv.Value.Value })
                .ToList();

            foreach (var entry in entriesToRemove)
                RemoveWithEvict(entry.Key, entry.Value);

            return entriesToRemove.Count;
        }
    }

    public int InvalidateByTag(string tag)
    {
        lock (_lock)
        {
            var entriesToRemove = _items
                .Where(kv => kv.Value.Tags.Contains(tag))
                .Select(kv => new { kv.Key, kv.Value.Value })
                .ToList();

            foreach (var entry in entriesToRemove)
                RemoveWithEvict(entry.Key, entry.Value);

            return entriesToRemove.Count;
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

    /// <summary>
    /// Gets the value for the key, or creates it asynchronously if not present or expired.
    /// </summary>
    /// <param name="key">The cache key to look up or populate.</param>
    /// <param name="factory">An async factory function invoked on cache miss.</param>
    /// <param name="ttl">Optional TTL override for this entry.</param>
    /// <param name="tags">Optional tags for group invalidation.</param>
    /// <returns>The cached or newly created value.</returns>
    public async Task<V> GetOrSetAsync(string key, Func<Task<V>> factory, TimeSpan? ttl = null, IEnumerable<string>? tags = null)
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

                RemoveWithEvict(key, entry.Value);
            }

            _misses++;
        }

        var value = await factory();

        lock (_lock)
        {
            var effectiveTtl = ttl ?? _defaultTtl;
            var expiresAt = effectiveTtl.HasValue ? DateTime.UtcNow + effectiveTtl.Value : (DateTime?)null;
            var tagSet = tags != null ? new HashSet<string>(tags) : new HashSet<string>();

            if (_items.Count >= _maxSize)
                Evict();

            _items[key] = new CacheEntry<V> { Value = value, ExpiresAt = expiresAt, Tags = tagSet };
            _order.AddFirst(key);
        }

        return value;
    }

    /// <summary>
    /// Gets multiple values by keys. Missing or expired keys are omitted from results.
    /// </summary>
    /// <param name="keys">The cache keys to look up.</param>
    /// <returns>A dictionary of found key-value pairs.</returns>
    public Dictionary<string, V> GetMany(IEnumerable<string> keys)
    {
        lock (_lock)
        {
            var result = new Dictionary<string, V>();
            foreach (var key in keys)
            {
                if (!_items.TryGetValue(key, out var entry))
                {
                    _misses++;
                    continue;
                }

                if (entry.ExpiresAt.HasValue && DateTime.UtcNow > entry.ExpiresAt.Value)
                {
                    RemoveWithEvict(key, entry.Value);
                    _misses++;
                    continue;
                }

                _order.Remove(key);
                _order.AddFirst(key);
                _hits++;
                result[key] = entry.Value;
            }
            return result;
        }
    }

    /// <summary>
    /// Registers a callback that fires when entries are evicted due to LRU eviction, TTL expiry, tag invalidation, or predicate deletion.
    /// </summary>
    /// <param name="callback">The callback receiving the evicted key and value.</param>
    public void OnEvict(Action<string, V> callback)
    {
        lock (_lock)
        {
            _onEvict = callback;
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

    private void RemoveWithEvict(string key, V value)
    {
        if (Remove(key))
        {
            _onEvict?.Invoke(key, value);
        }
    }

    private void Evict()
    {
        var now = DateTime.UtcNow;
        var expired = _items.FirstOrDefault(kv => kv.Value.ExpiresAt.HasValue && now > kv.Value.ExpiresAt.Value);
        if (expired.Key != null)
        {
            RemoveWithEvict(expired.Key, expired.Value.Value);
            _evictions++;
            return;
        }

        if (_order.Last != null)
        {
            var lastKey = _order.Last.Value;
            if (_items.TryGetValue(lastKey, out var entry))
            {
                RemoveWithEvict(lastKey, entry.Value);
                _evictions++;
            }
        }
    }
}
