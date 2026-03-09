# Philiprehberger.CacheKit

Thread-safe in-memory LRU cache for .NET — TTL expiration, tag-based invalidation, and configurable max size.

## Install

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
| `Size` | Current number of entries |

## License

MIT
