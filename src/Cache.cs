using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    /// Gets the current total estimated size of all cache entries in bytes.
    /// </summary>
    public long CurrentEstimatedSize { get; init; }

    /// <summary>
    /// Gets the cache hit rate as a value between 0.0 and 1.0.
    /// Returns 0.0 when no lookups have been performed.
    /// </summary>
    public double HitRate => Hits + Misses == 0 ? 0.0 : (double)Hits / (Hits + Misses);
}

/// <summary>
/// Represents a snapshot of per-tag statistics.
/// </summary>
public record TagStats
{
    /// <summary>
    /// Gets the number of cache hits for entries with this tag.
    /// </summary>
    public long Hits { get; init; }

    /// <summary>
    /// Gets the number of cache misses for lookups that resulted in entries with this tag.
    /// </summary>
    public long Misses { get; init; }

    /// <summary>
    /// Gets the number of evictions for entries with this tag.
    /// </summary>
    public long Evictions { get; init; }
}

internal class MutableTagStats
{
    public long Hits;
    public long Misses;
    public long Evictions;
}

/// <summary>
/// Configuration options for the cache.
/// </summary>
public class CacheOptions
{
    /// <summary>
    /// Gets or sets the maximum number of entries the cache can hold before eviction occurs.
    /// Defaults to 1000.
    /// </summary>
    public int MaxSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets an optional default time-to-live applied to entries that don't specify their own TTL.
    /// </summary>
    public TimeSpan? DefaultTtl { get; set; }

    /// <summary>
    /// Gets or sets an optional interval for background cleanup of expired entries.
    /// When null (default), no background cleanup is performed and expired entries are only
    /// removed on access.
    /// </summary>
    public TimeSpan? BackgroundCleanupInterval { get; set; }

    /// <summary>
    /// Gets or sets an optional maximum memory budget in bytes. When the total estimated size
    /// of all cache entries exceeds this value, LRU entries are evicted until the budget is met.
    /// Requires callers to provide size hints via the <c>estimatedSize</c> parameter on <c>Set</c>.
    /// </summary>
    public long? MaxMemoryBytes { get; set; }
}

internal class CacheEntry<V>
{
    public V Value { get; set; } = default!;
    public DateTime? ExpiresAt { get; set; }
    public HashSet<string> Tags { get; set; } = new();
    public long EstimatedSize { get; set; }
}

/// <summary>
/// A thread-safe, in-memory LRU cache with support for TTL expiration, tag-based invalidation,
/// size-based eviction, background cleanup, per-tag statistics, and eviction callbacks.
/// </summary>
/// <typeparam name="V">The type of values stored in the cache.</typeparam>
public class Cache<V> : IDisposable
{
    private readonly Dictionary<string, CacheEntry<V>> _items;
    private readonly LinkedList<string> _order = new();
    private readonly int _maxSize;
    private readonly TimeSpan? _defaultTtl;
    private readonly long? _maxMemoryBytes;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, MutableTagStats> _tagStats = new();
    private readonly Timer? _cleanupTimer;

    private long _hits;
    private long _misses;
    private long _evictions;
    private long _currentEstimatedSize;
    private Action<string, V>? _onEvict;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cache{V}"/> class.
    /// </summary>
    /// <param name="maxSize">The maximum number of entries the cache can hold before eviction occurs.</param>
    /// <param name="defaultTtl">An optional default time-to-live applied to entries that don't specify their own TTL.</param>
    public Cache(int maxSize = 1000, TimeSpan? defaultTtl = null)
    {
        _maxSize = maxSize;
        _defaultTtl = defaultTtl;
        _maxMemoryBytes = null;
        _items = new Dictionary<string, CacheEntry<V>>(maxSize);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Cache{V}"/> class with advanced options.
    /// </summary>
    /// <param name="options">The cache configuration options.</param>
    public Cache(CacheOptions options)
    {
        _maxSize = options.MaxSize;
        _defaultTtl = options.DefaultTtl;
        _maxMemoryBytes = options.MaxMemoryBytes;
        _items = new Dictionary<string, CacheEntry<V>>(options.MaxSize);

        if (options.BackgroundCleanupInterval.HasValue)
        {
            var interval = options.BackgroundCleanupInterval.Value;
            _cleanupTimer = new Timer(
                _ => CleanupExpired(),
                null,
                interval,
                interval);
        }
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
                    Evictions = _evictions,
                    CurrentEstimatedSize = _currentEstimatedSize
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
    /// <param name="estimatedSize">Optional estimated size in bytes for memory budget tracking.</param>
    public void Set(string key, V value, TimeSpan? ttl = null, IEnumerable<string>? tags = null, long estimatedSize = 0)
    {
        lock (_lock)
        {
            var effectiveTtl = ttl ?? _defaultTtl;
            var expiresAt = effectiveTtl.HasValue ? DateTime.UtcNow + effectiveTtl.Value : (DateTime?)null;
            var tagSet = tags != null ? new HashSet<string>(tags) : new HashSet<string>();

            if (_items.TryGetValue(key, out var existing))
            {
                _currentEstimatedSize -= existing.EstimatedSize;
                _items[key] = new CacheEntry<V> { Value = value, ExpiresAt = expiresAt, Tags = tagSet, EstimatedSize = estimatedSize };
                _currentEstimatedSize += estimatedSize;
                _order.Remove(key);
                _order.AddFirst(key);
            }
            else
            {
                if (_items.Count >= _maxSize)
                    Evict();

                _items[key] = new CacheEntry<V> { Value = value, ExpiresAt = expiresAt, Tags = tagSet, EstimatedSize = estimatedSize };
                _currentEstimatedSize += estimatedSize;
                _order.AddFirst(key);
            }

            EvictForMemoryBudget();
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
                IncrementTagMisses(key);
                return default;
            }

            if (entry.ExpiresAt.HasValue && DateTime.UtcNow > entry.ExpiresAt.Value)
            {
                IncrementTagMisses(entry.Tags);
                RemoveWithEvict(key, entry);
                _misses++;
                return default;
            }

            _order.Remove(key);
            _order.AddFirst(key);
            _hits++;
            IncrementTagHits(entry.Tags);
            return entry.Value;
        }
    }

    /// <summary>
    /// Tries to get a value. Returns true if found and not expired, false otherwise.
    /// This is an allocation-free lookup that avoids the need for GetOrDefault patterns.
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
                IncrementTagMisses(key);
                value = default;
                return false;
            }

            if (entry.ExpiresAt.HasValue && DateTime.UtcNow > entry.ExpiresAt.Value)
            {
                IncrementTagMisses(entry.Tags);
                RemoveWithEvict(key, entry);
                _misses++;
                value = default;
                return false;
            }

            _order.Remove(key);
            _order.AddFirst(key);
            _hits++;
            IncrementTagHits(entry.Tags);
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
                    IncrementTagHits(entry.Tags);
                    return entry.Value;
                }

                IncrementTagMisses(entry.Tags);
                RemoveWithEvict(key, entry);
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
                RemoveWithEvict(key, entry);
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
                .Select(kv => new { kv.Key, Entry = kv.Value })
                .ToList();

            foreach (var item in entriesToRemove)
                RemoveWithEvict(item.Key, item.Entry);

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
                .Select(kv => new { kv.Key, Entry = kv.Value })
                .ToList();

            foreach (var item in entriesToRemove)
                RemoveWithEvict(item.Key, item.Entry);

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
            _currentEstimatedSize = 0;
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
                    IncrementTagHits(entry.Tags);
                    return entry.Value;
                }

                IncrementTagMisses(entry.Tags);
                RemoveWithEvict(key, entry);
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
                    IncrementTagMisses(key);
                    continue;
                }

                if (entry.ExpiresAt.HasValue && DateTime.UtcNow > entry.ExpiresAt.Value)
                {
                    IncrementTagMisses(entry.Tags);
                    RemoveWithEvict(key, entry);
                    _misses++;
                    continue;
                }

                _order.Remove(key);
                _order.AddFirst(key);
                _hits++;
                IncrementTagHits(entry.Tags);
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

    /// <summary>
    /// Gets statistics for entries associated with the specified tag.
    /// </summary>
    /// <param name="tag">The tag to get statistics for.</param>
    /// <returns>A snapshot of hits, misses, and evictions for the tag.</returns>
    public TagStats GetTagStatistics(string tag)
    {
        if (_tagStats.TryGetValue(tag, out var stats))
        {
            return new TagStats
            {
                Hits = Interlocked.Read(ref stats.Hits),
                Misses = Interlocked.Read(ref stats.Misses),
                Evictions = Interlocked.Read(ref stats.Evictions)
            };
        }

        return new TagStats { Hits = 0, Misses = 0, Evictions = 0 };
    }

    /// <summary>
    /// Releases all resources used by the cache, including the background cleanup timer.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources used by the cache.
    /// </summary>
    /// <param name="disposing">True if called from Dispose; false if called from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _cleanupTimer?.Dispose();
        }

        _disposed = true;
    }

    private void CleanupExpired()
    {
        lock (_lock)
        {
            if (_disposed) return;

            var now = DateTime.UtcNow;
            var expired = _items
                .Where(kv => kv.Value.ExpiresAt.HasValue && now > kv.Value.ExpiresAt.Value)
                .Select(kv => new { kv.Key, Entry = kv.Value })
                .ToList();

            foreach (var item in expired)
            {
                IncrementTagEvictions(item.Entry.Tags);
                RemoveEntry(item.Key, item.Entry);
                _onEvict?.Invoke(item.Key, item.Entry.Value);
                _evictions++;
            }
        }
    }

    private bool Remove(string key)
    {
        if (_items.TryGetValue(key, out var entry))
        {
            _currentEstimatedSize -= entry.EstimatedSize;
            _items.Remove(key);
            _order.Remove(key);
            return true;
        }
        return false;
    }

    private void RemoveEntry(string key, CacheEntry<V> entry)
    {
        _currentEstimatedSize -= entry.EstimatedSize;
        _items.Remove(key);
        _order.Remove(key);
    }

    private void RemoveWithEvict(string key, CacheEntry<V> entry)
    {
        RemoveEntry(key, entry);
        _onEvict?.Invoke(key, entry.Value);
    }

    private void Evict()
    {
        var now = DateTime.UtcNow;
        var expired = _items.FirstOrDefault(kv => kv.Value.ExpiresAt.HasValue && now > kv.Value.ExpiresAt.Value);
        if (expired.Key != null)
        {
            IncrementTagEvictions(expired.Value.Tags);
            RemoveWithEvict(expired.Key, expired.Value);
            _evictions++;
            return;
        }

        if (_order.Last != null)
        {
            var lastKey = _order.Last.Value;
            if (_items.TryGetValue(lastKey, out var entry))
            {
                IncrementTagEvictions(entry.Tags);
                RemoveWithEvict(lastKey, entry);
                _evictions++;
            }
        }
    }

    private void EvictForMemoryBudget()
    {
        if (!_maxMemoryBytes.HasValue) return;

        while (_currentEstimatedSize > _maxMemoryBytes.Value && _order.Last != null)
        {
            var lastKey = _order.Last.Value;
            if (_items.TryGetValue(lastKey, out var entry))
            {
                IncrementTagEvictions(entry.Tags);
                RemoveWithEvict(lastKey, entry);
                _evictions++;
            }
            else
            {
                _order.RemoveLast();
            }
        }
    }

    private MutableTagStats GetOrCreateTagStats(string tag)
    {
        return _tagStats.GetOrAdd(tag, _ => new MutableTagStats());
    }

    private void IncrementTagHits(HashSet<string> tags)
    {
        foreach (var tag in tags)
        {
            var stats = GetOrCreateTagStats(tag);
            Interlocked.Increment(ref stats.Hits);
        }
    }

    private void IncrementTagMisses(HashSet<string> tags)
    {
        foreach (var tag in tags)
        {
            var stats = GetOrCreateTagStats(tag);
            Interlocked.Increment(ref stats.Misses);
        }
    }

    private void IncrementTagMisses(string key)
    {
        // For misses where the key doesn't exist, we can't determine tags
        // Tag misses are only tracked when the entry exists but is expired
    }

    private void IncrementTagEvictions(HashSet<string> tags)
    {
        foreach (var tag in tags)
        {
            var stats = GetOrCreateTagStats(tag);
            Interlocked.Increment(ref stats.Evictions);
        }
    }
}
