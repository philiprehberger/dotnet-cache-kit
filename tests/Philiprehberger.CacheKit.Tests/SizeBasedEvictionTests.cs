using Xunit;
using Philiprehberger.CacheKit;

namespace Philiprehberger.CacheKit.Tests;

public class SizeBasedEvictionTests
{
    [Fact]
    public void SizeEviction_EvictsLruWhenOverBudget()
    {
        using var cache = new Cache<string>(new CacheOptions
        {
            MaxSize = 100,
            MaxMemoryBytes = 1000
        });

        cache.Set("key1", "value1", estimatedSize: 400);
        cache.Set("key2", "value2", estimatedSize: 400);
        Assert.Equal(2, cache.Size);

        // Adding this should evict key1 (LRU) to stay under 1000 bytes
        cache.Set("key3", "value3", estimatedSize: 400);

        Assert.False(cache.Has("key1"));
        Assert.True(cache.Has("key2"));
        Assert.True(cache.Has("key3"));
    }

    [Fact]
    public void SizeEviction_TracksCurrentEstimatedSize()
    {
        using var cache = new Cache<string>(new CacheOptions
        {
            MaxSize = 100,
            MaxMemoryBytes = 5000
        });

        cache.Set("key1", "value1", estimatedSize: 500);
        cache.Set("key2", "value2", estimatedSize: 300);

        Assert.Equal(800, cache.Stats.CurrentEstimatedSize);
    }

    [Fact]
    public void SizeEviction_UpdateEntrySizeOnReplace()
    {
        using var cache = new Cache<string>(new CacheOptions
        {
            MaxSize = 100,
            MaxMemoryBytes = 5000
        });

        cache.Set("key1", "value1", estimatedSize: 500);
        Assert.Equal(500, cache.Stats.CurrentEstimatedSize);

        cache.Set("key1", "updated", estimatedSize: 200);
        Assert.Equal(200, cache.Stats.CurrentEstimatedSize);
    }

    [Fact]
    public void SizeEviction_EvictsMultipleEntriesIfNeeded()
    {
        using var cache = new Cache<string>(new CacheOptions
        {
            MaxSize = 100,
            MaxMemoryBytes = 500
        });

        cache.Set("key1", "v1", estimatedSize: 100);
        cache.Set("key2", "v2", estimatedSize: 100);
        cache.Set("key3", "v3", estimatedSize: 100);

        // This should evict key1 and key2 to make room
        cache.Set("key4", "v4", estimatedSize: 400);

        Assert.False(cache.Has("key1"));
        Assert.False(cache.Has("key2"));
        Assert.True(cache.Has("key4"));
    }

    [Fact]
    public void SizeEviction_NoEvictionWithoutMaxMemory()
    {
        var cache = new Cache<string>(maxSize: 100);

        cache.Set("key1", "v1", estimatedSize: 999999);
        cache.Set("key2", "v2", estimatedSize: 999999);

        Assert.Equal(2, cache.Size);
    }
}
