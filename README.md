# Philiprehberger.CacheKit

[![CI](https://github.com/philiprehberger/dotnet-cache-kit/actions/workflows/ci.yml/badge.svg)](https://github.com/philiprehberger/dotnet-cache-kit/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Philiprehberger.CacheKit.svg)](https://www.nuget.org/packages/Philiprehberger.CacheKit)
[![Last updated](https://img.shields.io/github/last-commit/philiprehberger/dotnet-cache-kit)](https://github.com/philiprehberger/dotnet-cache-kit/commits/main)

Thread-safe in-memory LRU cache for .NET — TTL expiration, tag-based invalidation, and configurable max size.

## Installation

```bash
dotnet add package Philiprehberger.CacheKit
```

## Usage

```csharp
using Philiprehberger.CacheKit;

// Create a cache
var cache = new Cache<string>(maxSize: 100, defaultTtl: TimeSpan.FromMinutes(5));

// Set/Get
cache.Set("user:1", "Alice");
var name = cache.Get("user:1");  // "Alice"

// TryGet pattern
if (cache.TryGet("user:1", out var value))
    Console.WriteLine(value);

// TTL per entry
cache.Set("session", "abc123", ttl: TimeSpan.FromMinutes(30));

// Tags for group invalidation
cache.Set("post:1", "Hello", tags: new[] { "posts", "user:1" });
cache.Set("post:2", "World", tags: new[] { "posts", "user:1" });
cache.InvalidateByTag("user:1");  // removes both
```

### Background Cleanup

Enable periodic removal of expired entries to proactively free memory instead of waiting for access-triggered eviction:

```csharp
using var cache = new Cache<string>(new CacheOptions
{
    MaxSize = 10000,
    DefaultTtl = TimeSpan.FromMinutes(5),
    BackgroundCleanupInterval = TimeSpan.FromMinutes(1)
});

cache.Set("session:1", "data");
// Expired entries are automatically removed every minute
// Dispose the cache to stop the background timer
```

### Size-Based Eviction

Set a memory budget and provide size hints to evict LRU entries when the budget is exceeded:

```csharp
using var cache = new Cache<byte[]>(new CacheOptions
{
    MaxSize = 10000,
    MaxMemoryBytes = 50 * 1024 * 1024  // 50 MB
});

cache.Set("image:1", imageBytes, estimatedSize: imageBytes.Length);
cache.Set("image:2", otherBytes, estimatedSize: otherBytes.Length);

// When total estimated size exceeds 50 MB, LRU entries are evicted
var stats = cache.Stats;
Console.WriteLine($"Current memory usage: {stats.CurrentEstimatedSize} bytes");
```

### Per-Tag Statistics

Track hits, misses, and evictions for entries grouped by tag:

```csharp
var cache = new Cache<string>(maxSize: 1000);

cache.Set("user:1", "Alice", tags: new[] { "users" });
cache.Set("user:2", "Bob", tags: new[] { "users" });
cache.Get("user:1");
cache.Get("user:2");

var tagStats = cache.GetTagStatistics("users");
Console.WriteLine($"Tag 'users' — Hits: {tagStats.Hits}, Misses: {tagStats.Misses}, Evictions: {tagStats.Evictions}");
```

### Async Usage

Use `GetOrSetAsync` to populate cache entries from async sources like databases or HTTP calls:

```csharp
var user = await cache.GetOrSetAsync(
    "user:42",
    async () => await httpClient.GetFromJsonAsync<string>("/api/users/42"),
    ttl: TimeSpan.FromMinutes(5),
    tags: new[] { "users" }
);
```

### Batch Operations

Retrieve multiple entries at once with `GetMany`. Missing or expired keys are omitted:

```csharp
var keys = new[] { "user:1", "user:2", "user:3" };
var found = cache.GetMany(keys);
// found is Dictionary<string, string> with only the keys that exist
```

### Eviction Callbacks

Register a callback to be notified when entries are evicted:

```csharp
cache.OnEvict((key, value) =>
{
    Console.WriteLine($"Evicted: {key} = {value}");
});
```

## API

### `Cache<V>`

| Method | Description |
|--------|-------------|
| `Set(key, value, ttl?, tags?, estimatedSize?)` | Store a value with optional TTL, tags, and size hint |
| `Get(key)` | Get value or default if missing/expired |
| `TryGet(key, out value)` | Try to get value, returns bool without throwing |
| `Has(key)` | Check if key exists and is not expired |
| `Delete(key)` | Remove a key |
| `InvalidateByTag(tag)` | Remove all entries with a given tag |
| `Keys()` | Get all non-expired keys |
| `Clear()` | Remove all entries |
| `GetOrSet(key, factory, ttl?)` | Get value or create it atomically using factory |
| `GetOrSetAsync(key, factory, ttl?, tags?)` | Async version of GetOrSet with optional tags |
| `GetMany(keys)` | Get multiple values; missing keys omitted from result |
| `OnEvict(callback)` | Register callback for eviction notifications |
| `DeleteWhere(predicate)` | Remove all entries matching predicate, returns count |
| `GetTagStatistics(tag)` | Get per-tag hits, misses, and eviction counts |
| `Stats` | Get cache statistics (hits, misses, evictions, current estimated size, hit rate) |
| `Size` | Current number of entries |
| `Dispose()` | Stop background cleanup timer and release resources |

### `CacheOptions`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxSize` | `int` | `1000` | Maximum number of entries before LRU eviction |
| `DefaultTtl` | `TimeSpan?` | `null` | Default time-to-live for entries without explicit TTL |
| `BackgroundCleanupInterval` | `TimeSpan?` | `null` | Interval for background expired-entry cleanup |
| `MaxMemoryBytes` | `long?` | `null` | Memory budget in bytes for size-based eviction |

### `CacheStats`

| Property | Type | Description |
|----------|------|-------------|
| `Hits` | `long` | Total cache hits |
| `Misses` | `long` | Total cache misses |
| `Evictions` | `long` | Total entries evicted |
| `CurrentEstimatedSize` | `long` | Total estimated size of all entries in bytes |
| `HitRate` | `double` | Hit rate between 0.0 and 1.0 |

### `TagStats`

| Property | Type | Description |
|----------|------|-------------|
| `Hits` | `long` | Hits for entries with this tag |
| `Misses` | `long` | Misses for entries with this tag |
| `Evictions` | `long` | Evictions for entries with this tag |

## Development

```bash
dotnet build src/Philiprehberger.CacheKit.csproj --configuration Release
```

## Support

If you find this project useful:

⭐ [Star the repo](https://github.com/philiprehberger/dotnet-cache-kit)

🐛 [Report issues](https://github.com/philiprehberger/dotnet-cache-kit/issues?q=is%3Aissue+is%3Aopen+label%3Abug)

💡 [Suggest features](https://github.com/philiprehberger/dotnet-cache-kit/issues?q=is%3Aissue+is%3Aopen+label%3Aenhancement)

❤️ [Sponsor development](https://github.com/sponsors/philiprehberger)

🌐 [All Open Source Projects](https://philiprehberger.com/open-source-packages)

💻 [GitHub Profile](https://github.com/philiprehberger)

🔗 [LinkedIn Profile](https://www.linkedin.com/in/philiprehberger)

## License

[MIT](LICENSE)
