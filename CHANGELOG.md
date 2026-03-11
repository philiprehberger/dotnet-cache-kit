# Changelog

## 0.1.1 (2026-03-10)

- Add README to NuGet package so it displays on nuget.org

## 0.1.0 (2026-03-09)

- Initial release
- Generic `Cache<V>` with configurable max size
- LRU eviction with LinkedList ordering
- TTL support with lazy expiration
- Tag-based invalidation
- Thread-safe with lock-based synchronization
