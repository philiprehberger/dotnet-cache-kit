# Changelog

## 0.3.0

- Add `GetOrSetAsync` for async cache population
- Add `TryGet` for non-throwing cache lookups
- Add `GetMany` for batch key retrieval
- Add `OnEvict` callback for eviction notifications

## 0.2.3

- Add Development section to README
- Add GenerateDocumentationFile and RepositoryType to .csproj

## [0.2.0] - 2026-03-12

### Added
- Cache statistics via `Stats` property (hits, misses, evictions, hit rate)
- `GetOrSet` method for atomic get-or-create pattern
- `DeleteWhere` method for predicate-based cache entry removal

## 0.1.1 (2026-03-10)

- Add README to NuGet package so it displays on nuget.org

## 0.1.0 (2026-03-09)

- Initial release
- Generic `Cache<V>` with configurable max size
- LRU eviction with LinkedList ordering
- TTL support with lazy expiration
- Tag-based invalidation
- Thread-safe with lock-based synchronization
