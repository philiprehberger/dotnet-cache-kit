# Philiprehberger.CacheKit

[![CI](https://github.com/philiprehberger/dotnet-cache-kit/actions/workflows/ci.yml/badge.svg)](https://github.com/philiprehberger/dotnet-cache-kit/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Philiprehberger.CacheKit.svg)](https://www.nuget.org/packages/Philiprehberger.CacheKit)
[![License](https://img.shields.io/github/license/philiprehberger/dotnet-cache-kit)](LICENSE)
[![Sponsor](https://img.shields.io/badge/sponsor-GitHub%20Sponsors-ec6cb9)](https://github.com/sponsors/philiprehberger)

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

// Check existence
bool exists = cache.Has("user:1");

// Delete
cache.Delete("user:1");

// Get all valid keys
var keys = cache.Keys();

// Clear everything
cache.Clear();

// Get or create (atomic)
var user = cache.GetOrSet("user:1", () => LoadUserFromDb("1"), ttl: TimeSpan.FromMinutes(10));

// Delete entries matching a predicate
int removed = cache.DeleteWhere((key, value) => key.StartsWith("session:"));

// Cache statistics
var stats = cache.Stats;
Console.WriteLine($"Hits: {stats.Hits}, Misses: {stats.Misses}");
Console.WriteLine($"Evictions: {stats.Evictions}, Hit Rate: {stats.HitRate:P1}");
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

| Method | Description |
|--------|-------------|
| `Set(key, value, ttl?, tags?)` | Store a value with optional TTL and tags |
| `Get(key)` | Get value or default if missing/expired |
| `TryGet(key, out value)` | Try to get value, returns bool |
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
| `Stats` | Get cache statistics (hits, misses, evictions, hit rate) |
| `Size` | Current number of entries |

## Development

```bash
dotnet build src/Philiprehberger.CacheKit.csproj --configuration Release
```

## License

[MIT](LICENSE)
