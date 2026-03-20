using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Philiprehberger.CacheKit;

/// <summary>
/// Represents a snapshot of cache performance statistics.
/// </summary>
public record CacheStats
{
    /// <summary>
    /// Gets the total number of cache hits.
    /// </summary>
    public long Hits { get; init; }

    /// <summary>
    /// Gets the total number of cache misses.
    /// </summary>
    public long Misses { get; init; }

    /// <summary>
    /// Gets the total number of entries evicted from the cache.
    /// </summary>
    public long Evictions { get; init; }

    /// <summary>
    /// Gets the cache hit rate as a value between 0.0 and 1.0.
    /// Returns 0.0 when no lookups have been performed.
    /// </summary>
    public double HitRate => Hits + Misses == 0 ? 0.0 : (double)Hits / (Hits + Misses);
}

internal class CacheEntry<V>
{
    public V Value { get; set; } = default!;
    public DateTime? ExpiresAt { get; set; }
    public HashSet<string> Tags { get; set; } = new();
}

/// <summary>
/// A thread-safe, in-memory LRU cache with support for TTL expiration, tag-based invalidation, and eviction callbacks.
/// </summary>
/// <typeparam name="V">The type of values stored in the cache.</typeparam>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="Cache{V}"/> class.
    /// </summary>
    /// <param name="maxSize">The maximum number of entries the cache can hold before eviction occurs.</param>
    /// <param name="defaultTtl">An optional default time-to-live applied to entries that don't specify their own TTL.</param>
    public Cache(int maxSize = 1000, TimeSpan? defaultTtl = null)
    {
        _maxSize = maxSize;
        _defaultTtl = defaultTtl;
        _items = new Dictionary<string, CacheEntry<V>>(maxSize);
    }

    /// <summary>
    /// Gets the current number of entries in the cache.
    /// </summary>
    public int Size
    {
        get { lock (_lock) return _items.Count; }
    }

    /// <summary>
    /// Gets a snapshot of the current cache performance statistics.
    /// </summary>
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

    /// <summary>
    /// Adds or updates a cache entry with the specified key and value.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="ttl">An optional TTL override for this entry. Falls back to the default TTL if not specified.</param>
    /// <param name="tags">Optional tags for group invalidation.</param>
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

    /// <summary>
    /// Gets the value associated with the specified key, or the default value if the key is not found or has expired.
    /// </summary>
    /// <param name="key">The cache key to look up.</param>
    /// <returns>The cached value if found and not expired; otherwise, the default value for <typeparamref name="V"/>.</returns>
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

    /// <summary>
    /// Gets the value for the specified key, or creates and caches it using the factory function if not present or expired.
    /// </summary>
    /// <param name="key">The cache key to look up or populate.</param>
    /// <param name="factory">A factory function invoked on cache miss to produce the value.</param>
    /// <param name="ttl">An optional TTL override for this entry.</param>
    /// <returns>The cached or newly created value.</returns>
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

    /// <summary>
    /// Determines whether the cache contains a non-expired entry with the specified key.
    /// </summary>
    /// <param name="key">The cache key to check.</param>
    /// <returns>True if the key exists and has not expired; otherwise, false.</returns>
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

    /// <summary>
    /// Removes the entry with the specified key from the cache.
    /// </summary>
    /// <param name="key">The cache key to remove.</param>
    /// <returns>True if the entry was found and removed; otherwise, false.</returns>
    public bool Delete(string key)
    {
        lock (_lock) return Remove(key);
    }

    /// <summary>
    /// Removes all cache entries that match the specified predicate.
    /// </summary>
    /// <param name="predicate">A function that receives the key and value and returns true for entries to remove.</param>
    /// <returns>The number of entries removed.</returns>
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

    /// <summary>
    /// Removes all cache entries associated with the specified tag.
    /// </summary>
    /// <param name="tag">The tag identifying entries to invalidate.</param>
    /// <returns>The number of entries removed.</returns>
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

    /// <summary>
    /// Removes all entries from the cache.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
            _order.Clear();
        }
    }

    /// <summary>
    /// Returns a list of all non-expired cache keys.
    /// </summary>
    /// <returns>A list of keys for entries that have not expired.</returns>
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
