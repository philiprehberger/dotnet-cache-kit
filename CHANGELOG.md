# Changelog

## 0.5.0 (2026-03-31)

- Add `WarmAsync<T>` for pre-loading cache entries from a data source on startup
- Add `EvictionPolicy` enum with LRU and LFU eviction strategies
- Add `OnExpired` callback for TTL-based expiration events (distinct from capacity eviction)
- Track per-key access counts for LFU eviction policy

## 0.4.1 (2026-03-31)

- Standardize README to 3-badge format with emoji Support section
- Update CI actions to v5 for Node.js 24 compatibility

## 0.4.0 (2026-03-27)

- Add background expiration cleanup with configurable interval
- Add size-based eviction with estimated memory budget
- Add per-tag statistics tracking
- Add TryGet method for allocation-free lookups

## 0.3.4 (2026-03-26)

- Add Sponsor badge to README
- Fix License section format

## 0.3.3 (2026-03-22)

- Add dates to changelog entries
- Normalize changelog format

## 0.3.2 (2026-03-21)

- Align csproj description with README

## 0.3.1 (2026-03-20)

- Add LangVersion and TreatWarningsAsErrors to csproj

## 0.3.0 (2026-03-16)

- Add `GetOrSetAsync` for async cache population
- Add `TryGet` for non-throwing cache lookups
- Add `GetMany` for batch key retrieval
- Add `OnEvict` callback for eviction notifications

## 0.2.3 (2026-03-16)

- Add Development section to README
- Add GenerateDocumentationFile and RepositoryType to .csproj

## 0.2.0 (2026-03-12)

- Add cache statistics via `Stats` property (hits, misses, evictions, hit rate)
- Add `GetOrSet` method for atomic get-or-create pattern
- Add `DeleteWhere` method for predicate-based cache entry removal

## 0.1.1 (2026-03-10)

- Add README to NuGet package so it displays on nuget.org

## 0.1.0 (2026-03-09)

- Initial release
- Generic `Cache<V>` with configurable max size
- LRU eviction with LinkedList ordering
- TTL support with lazy expiration
- Tag-based invalidation
- Thread-safe with lock-based synchronization
