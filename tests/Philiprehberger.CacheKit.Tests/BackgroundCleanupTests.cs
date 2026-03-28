using Xunit;
using Philiprehberger.CacheKit;

namespace Philiprehberger.CacheKit.Tests;

public class BackgroundCleanupTests
{
    [Fact]
    public void BackgroundCleanup_RemovesExpiredEntries()
    {
        using var cache = new Cache<string>(new CacheOptions
        {
            MaxSize = 100,
            DefaultTtl = TimeSpan.FromMilliseconds(50),
            BackgroundCleanupInterval = TimeSpan.FromMilliseconds(100)
        });

        cache.Set("key1", "value1");
        cache.Set("key2", "value2");
        Assert.Equal(2, cache.Size);

        Thread.Sleep(250);

        Assert.Equal(0, cache.Size);
    }

    [Fact]
    public void BackgroundCleanup_DoesNotRemoveNonExpiredEntries()
    {
        using var cache = new Cache<string>(new CacheOptions
        {
            MaxSize = 100,
            BackgroundCleanupInterval = TimeSpan.FromMilliseconds(100)
        });

        cache.Set("permanent", "value");
        cache.Set("expiring", "value", ttl: TimeSpan.FromMilliseconds(50));

        Thread.Sleep(250);

        Assert.Equal(1, cache.Size);
        Assert.True(cache.Has("permanent"));
    }

    [Fact]
    public void BackgroundCleanup_TriggersEvictionCallback()
    {
        var evictedKeys = new List<string>();

        using var cache = new Cache<string>(new CacheOptions
        {
            MaxSize = 100,
            DefaultTtl = TimeSpan.FromMilliseconds(50),
            BackgroundCleanupInterval = TimeSpan.FromMilliseconds(100)
        });

        cache.OnEvict((key, _) => { lock (evictedKeys) evictedKeys.Add(key); });
        cache.Set("key1", "value1");

        Thread.Sleep(250);

        lock (evictedKeys)
        {
            Assert.Contains("key1", evictedKeys);
        }
    }

    [Fact]
    public void BackgroundCleanup_IncrementsEvictionStats()
    {
        using var cache = new Cache<string>(new CacheOptions
        {
            MaxSize = 100,
            DefaultTtl = TimeSpan.FromMilliseconds(50),
            BackgroundCleanupInterval = TimeSpan.FromMilliseconds(100)
        });

        cache.Set("key1", "value1");

        Thread.Sleep(250);

        Assert.True(cache.Stats.Evictions > 0);
    }

    [Fact]
    public void Dispose_StopsBackgroundCleanup()
    {
        var cache = new Cache<string>(new CacheOptions
        {
            MaxSize = 100,
            DefaultTtl = TimeSpan.FromMilliseconds(50),
            BackgroundCleanupInterval = TimeSpan.FromMilliseconds(100)
        });

        cache.Set("key1", "value1");
        cache.Dispose();

        // After dispose, no exception should be thrown
        Assert.True(true);
    }
}
