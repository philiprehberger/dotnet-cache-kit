using Philiprehberger.CacheKit;
using Xunit;

namespace Philiprehberger.CacheKit.Tests;

public class CacheBasicTests
{
    [Fact]
    public void Set_And_Get_Returns_Value()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("key1", "value1");
        Assert.Equal("value1", cache.Get("key1"));
    }

    [Fact]
    public void Get_Missing_Key_Returns_Default()
    {
        var cache = new Cache<string>(maxSize: 10);
        Assert.Null(cache.Get("missing"));
    }

    [Fact]
    public void Has_Returns_True_For_Existing_Key()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("key1", "value1");
        Assert.True(cache.Has("key1"));
    }

    [Fact]
    public void Has_Returns_False_For_Missing_Key()
    {
        var cache = new Cache<string>(maxSize: 10);
        Assert.False(cache.Has("missing"));
    }

    [Fact]
    public void Delete_Removes_Entry()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("key1", "value1");
        Assert.True(cache.Delete("key1"));
        Assert.Null(cache.Get("key1"));
    }

    [Fact]
    public void Clear_Removes_All_Entries()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.Clear();
        Assert.Equal(0, cache.Size);
    }

    [Fact]
    public void Keys_Returns_Non_Expired_Keys()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("a", "1");
        cache.Set("b", "2");
        var keys = cache.Keys();
        Assert.Contains("a", keys);
        Assert.Contains("b", keys);
    }

    [Fact]
    public void Size_Returns_Entry_Count()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("a", "1");
        cache.Set("b", "2");
        Assert.Equal(2, cache.Size);
    }

    [Fact]
    public void TryGet_Returns_True_When_Found()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("key1", "value1");
        Assert.True(cache.TryGet("key1", out var value));
        Assert.Equal("value1", value);
    }

    [Fact]
    public void TryGet_Returns_False_When_Missing()
    {
        var cache = new Cache<string>(maxSize: 10);
        Assert.False(cache.TryGet("missing", out var value));
        Assert.Null(value);
    }
}

public class CacheTtlTests
{
    [Fact]
    public void Expired_Entry_Returns_Default()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("key1", "value1", ttl: TimeSpan.FromMilliseconds(1));
        Thread.Sleep(50);
        Assert.Null(cache.Get("key1"));
    }

    [Fact]
    public void Has_Returns_False_For_Expired_Key()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("key1", "value1", ttl: TimeSpan.FromMilliseconds(1));
        Thread.Sleep(50);
        Assert.False(cache.Has("key1"));
    }

    [Fact]
    public void Default_Ttl_Applied_When_No_Per_Entry_Ttl()
    {
        var cache = new Cache<string>(maxSize: 10, defaultTtl: TimeSpan.FromMilliseconds(1));
        cache.Set("key1", "value1");
        Thread.Sleep(50);
        Assert.Null(cache.Get("key1"));
    }
}

public class CacheEvictionTests
{
    [Fact]
    public void Lru_Eviction_Removes_Oldest_Entry()
    {
        var cache = new Cache<string>(maxSize: 2);
        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.Set("c", "3"); // should evict "a"
        Assert.Null(cache.Get("a"));
        Assert.Equal("2", cache.Get("b"));
        Assert.Equal("3", cache.Get("c"));
    }

    [Fact]
    public void Lru_Access_Refreshes_Order()
    {
        var cache = new Cache<string>(maxSize: 2);
        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.Get("a"); // refresh "a"
        cache.Set("c", "3"); // should evict "b" (least recently used)
        Assert.Equal("1", cache.Get("a"));
        Assert.Null(cache.Get("b"));
        Assert.Equal("3", cache.Get("c"));
    }

    [Fact]
    public void Eviction_Stats_Increment()
    {
        var cache = new Cache<string>(maxSize: 1);
        cache.Set("a", "1");
        cache.Set("b", "2");
        Assert.True(cache.Stats.Evictions > 0);
    }

    [Fact]
    public void OnEvict_Callback_Fires_On_Capacity_Eviction()
    {
        var cache = new Cache<string>(maxSize: 1);
        string? evictedKey = null;
        string? evictedValue = null;
        cache.OnEvict((k, v) => { evictedKey = k; evictedValue = v; });
        cache.Set("a", "1");
        cache.Set("b", "2");
        Assert.Equal("a", evictedKey);
        Assert.Equal("1", evictedValue);
    }
}

public class CacheLfuEvictionTests
{
    [Fact]
    public void Lfu_Evicts_Least_Frequently_Accessed()
    {
        var cache = new Cache<string>(new CacheOptions
        {
            MaxSize = 3,
            EvictionPolicy = EvictionPolicy.Lfu
        });

        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.Set("c", "3");

        // Access "a" twice, "b" once, "c" not at all
        cache.Get("a");
        cache.Get("a");
        cache.Get("b");

        // Adding "d" should evict "c" (0 accesses)
        cache.Set("d", "4");
        Assert.Null(cache.Get("c"));
        Assert.Equal("1", cache.Get("a"));
        Assert.Equal("2", cache.Get("b"));
        Assert.Equal("4", cache.Get("d"));
    }

    [Fact]
    public void Lfu_Policy_Is_Set_Via_Options()
    {
        var cache = new Cache<string>(new CacheOptions
        {
            MaxSize = 2,
            EvictionPolicy = EvictionPolicy.Lfu
        });

        cache.Set("a", "1");
        cache.Set("b", "2");

        // Access "b" to give it higher frequency
        cache.Get("b");
        cache.Get("b");

        // Should evict "a" (fewer accesses)
        cache.Set("c", "3");
        Assert.Null(cache.Get("a"));
        Assert.Equal("2", cache.Get("b"));
    }

    [Fact]
    public void Default_Policy_Is_Lru()
    {
        var cache = new Cache<string>(new CacheOptions { MaxSize = 2 });

        cache.Set("a", "1");
        cache.Set("b", "2");

        // Access "a" many times but it was added first
        cache.Get("a");
        cache.Get("a");
        cache.Get("a");

        // With LRU, "b" is least recently used after "a" accesses
        cache.Set("c", "3");
        Assert.Null(cache.Get("b"));
        Assert.Equal("1", cache.Get("a"));
    }
}

public class CacheWarmAsyncTests
{
    [Fact]
    public async Task WarmAsync_Loads_Entries()
    {
        var cache = new Cache<string>(maxSize: 100);

        var count = await cache.WarmAsync<string>(async () =>
        {
            await Task.CompletedTask;
            return new[]
            {
                new KeyValuePair<string, string>("a", "1"),
                new KeyValuePair<string, string>("b", "2"),
                new KeyValuePair<string, string>("c", "3"),
            };
        });

        Assert.Equal(3, count);
        Assert.Equal("1", cache.Get("a"));
        Assert.Equal("2", cache.Get("b"));
        Assert.Equal("3", cache.Get("c"));
    }

    [Fact]
    public async Task WarmAsync_Overwrites_Existing_Keys()
    {
        var cache = new Cache<string>(maxSize: 100);
        cache.Set("a", "old");

        await cache.WarmAsync<string>(async () =>
        {
            await Task.CompletedTask;
            return new[] { new KeyValuePair<string, string>("a", "new") };
        });

        Assert.Equal("new", cache.Get("a"));
    }

    [Fact]
    public async Task WarmAsync_Applies_Ttl()
    {
        var cache = new Cache<string>(maxSize: 100);

        await cache.WarmAsync<string>(
            async () =>
            {
                await Task.CompletedTask;
                return new[] { new KeyValuePair<string, string>("a", "1") };
            },
            ttl: TimeSpan.FromMilliseconds(1));

        Thread.Sleep(50);
        Assert.Null(cache.Get("a"));
    }

    [Fact]
    public async Task WarmAsync_Applies_Tags()
    {
        var cache = new Cache<string>(maxSize: 100);

        await cache.WarmAsync<string>(
            async () =>
            {
                await Task.CompletedTask;
                return new[]
                {
                    new KeyValuePair<string, string>("a", "1"),
                    new KeyValuePair<string, string>("b", "2"),
                };
            },
            tags: new[] { "warmed" });

        Assert.Equal(2, cache.InvalidateByTag("warmed"));
        Assert.Equal(0, cache.Size);
    }

    [Fact]
    public async Task WarmAsync_Evicts_When_Over_Capacity()
    {
        var cache = new Cache<string>(maxSize: 2);

        var count = await cache.WarmAsync<string>(async () =>
        {
            await Task.CompletedTask;
            return new[]
            {
                new KeyValuePair<string, string>("a", "1"),
                new KeyValuePair<string, string>("b", "2"),
                new KeyValuePair<string, string>("c", "3"),
            };
        });

        Assert.Equal(3, count);
        Assert.Equal(2, cache.Size);
    }
}

public class CacheOnExpiredTests
{
    [Fact]
    public void OnExpired_Fires_On_Ttl_Expiry_Via_Get()
    {
        var cache = new Cache<string>(maxSize: 10);
        string? expiredKey = null;
        string? expiredValue = null;
        cache.OnExpired((k, v) => { expiredKey = k; expiredValue = v; });

        cache.Set("key1", "value1", ttl: TimeSpan.FromMilliseconds(1));
        Thread.Sleep(50);
        cache.Get("key1");

        Assert.Equal("key1", expiredKey);
        Assert.Equal("value1", expiredValue);
    }

    [Fact]
    public void OnExpired_Does_Not_Fire_On_Capacity_Eviction()
    {
        var cache = new Cache<string>(maxSize: 1);
        var expiredFired = false;
        cache.OnExpired((k, v) => { expiredFired = true; });

        cache.Set("a", "1");
        cache.Set("b", "2"); // evicts "a" due to capacity, not TTL

        Assert.False(expiredFired);
    }

    [Fact]
    public void OnExpired_Fires_On_Ttl_Expiry_Via_TryGet()
    {
        var cache = new Cache<string>(maxSize: 10);
        string? expiredKey = null;
        cache.OnExpired((k, v) => { expiredKey = k; });

        cache.Set("key1", "value1", ttl: TimeSpan.FromMilliseconds(1));
        Thread.Sleep(50);
        cache.TryGet("key1", out _);

        Assert.Equal("key1", expiredKey);
    }

    [Fact]
    public void OnExpired_Fires_On_Ttl_Expiry_Via_Has()
    {
        var cache = new Cache<string>(maxSize: 10);
        string? expiredKey = null;
        cache.OnExpired((k, v) => { expiredKey = k; });

        cache.Set("key1", "value1", ttl: TimeSpan.FromMilliseconds(1));
        Thread.Sleep(50);
        cache.Has("key1");

        Assert.Equal("key1", expiredKey);
    }

    [Fact]
    public void OnExpired_Fires_On_Ttl_Expiry_Via_GetMany()
    {
        var cache = new Cache<string>(maxSize: 10);
        string? expiredKey = null;
        cache.OnExpired((k, v) => { expiredKey = k; });

        cache.Set("key1", "value1", ttl: TimeSpan.FromMilliseconds(1));
        Thread.Sleep(50);
        cache.GetMany(new[] { "key1" });

        Assert.Equal("key1", expiredKey);
    }

    [Fact]
    public void Both_OnExpired_And_OnEvict_Fire_On_Ttl_Expiry()
    {
        var cache = new Cache<string>(maxSize: 10);
        var expiredFired = false;
        var evictFired = false;
        cache.OnExpired((k, v) => { expiredFired = true; });
        cache.OnEvict((k, v) => { evictFired = true; });

        cache.Set("key1", "value1", ttl: TimeSpan.FromMilliseconds(1));
        Thread.Sleep(50);
        cache.Get("key1");

        Assert.True(expiredFired);
        Assert.True(evictFired);
    }

    [Fact]
    public void OnEvict_Fires_But_OnExpired_Does_Not_On_Delete()
    {
        var cache = new Cache<string>(maxSize: 10);
        var expiredFired = false;
        cache.OnExpired((k, v) => { expiredFired = true; });

        cache.Set("key1", "value1");
        cache.Delete("key1");

        Assert.False(expiredFired);
    }
}

public class CacheTagTests
{
    [Fact]
    public void InvalidateByTag_Removes_Tagged_Entries()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("a", "1", tags: new[] { "group1" });
        cache.Set("b", "2", tags: new[] { "group1" });
        cache.Set("c", "3", tags: new[] { "group2" });

        Assert.Equal(2, cache.InvalidateByTag("group1"));
        Assert.Null(cache.Get("a"));
        Assert.Null(cache.Get("b"));
        Assert.Equal("3", cache.Get("c"));
    }

    [Fact]
    public void GetTagStatistics_Tracks_Hits()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("a", "1", tags: new[] { "users" });
        cache.Get("a");
        cache.Get("a");

        var stats = cache.GetTagStatistics("users");
        Assert.Equal(2, stats.Hits);
    }
}

public class CacheStatsTests
{
    [Fact]
    public void Stats_Tracks_Hits_And_Misses()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("a", "1");
        cache.Get("a");
        cache.Get("missing");

        Assert.Equal(1, cache.Stats.Hits);
        Assert.Equal(1, cache.Stats.Misses);
    }

    [Fact]
    public void HitRate_Computed_Correctly()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("a", "1");
        cache.Get("a");
        cache.Get("a");
        cache.Get("missing");

        Assert.Equal(2.0 / 3.0, cache.Stats.HitRate, 5);
    }
}

public class CacheGetOrSetTests
{
    [Fact]
    public void GetOrSet_Returns_Existing_Value()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("a", "existing");
        var result = cache.GetOrSet("a", () => "new");
        Assert.Equal("existing", result);
    }

    [Fact]
    public void GetOrSet_Creates_Value_On_Miss()
    {
        var cache = new Cache<string>(maxSize: 10);
        var result = cache.GetOrSet("a", () => "created");
        Assert.Equal("created", result);
        Assert.Equal("created", cache.Get("a"));
    }

    [Fact]
    public async Task GetOrSetAsync_Creates_Value_On_Miss()
    {
        var cache = new Cache<string>(maxSize: 10);
        var result = await cache.GetOrSetAsync("a", async () =>
        {
            await Task.CompletedTask;
            return "async-created";
        });
        Assert.Equal("async-created", result);
    }
}

public class CacheDeleteWhereTests
{
    [Fact]
    public void DeleteWhere_Removes_Matching_Entries()
    {
        var cache = new Cache<int>(maxSize: 10);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);

        var removed = cache.DeleteWhere((key, value) => value > 1);
        Assert.Equal(2, removed);
        Assert.Equal(1, cache.Get("a"));
        Assert.Equal(default, cache.Get("b"));
    }
}

public class CacheGetManyTests
{
    [Fact]
    public void GetMany_Returns_Found_Entries()
    {
        var cache = new Cache<string>(maxSize: 10);
        cache.Set("a", "1");
        cache.Set("b", "2");

        var result = cache.GetMany(new[] { "a", "b", "c" });
        Assert.Equal(2, result.Count);
        Assert.Equal("1", result["a"]);
        Assert.Equal("2", result["b"]);
    }
}

public class CacheMemoryBudgetTests
{
    [Fact]
    public void Evicts_When_Memory_Budget_Exceeded()
    {
        var cache = new Cache<string>(new CacheOptions
        {
            MaxSize = 100,
            MaxMemoryBytes = 100
        });

        cache.Set("a", "1", estimatedSize: 60);
        cache.Set("b", "2", estimatedSize: 60); // total 120 > 100, should evict "a"

        Assert.Null(cache.Get("a"));
        Assert.Equal("2", cache.Get("b"));
    }
}

public class CacheDisposableTests
{
    [Fact]
    public void Dispose_Does_Not_Throw()
    {
        var cache = new Cache<string>(new CacheOptions
        {
            MaxSize = 10,
            BackgroundCleanupInterval = TimeSpan.FromMinutes(1)
        });
        cache.Dispose();
    }

    [Fact]
    public void Double_Dispose_Does_Not_Throw()
    {
        var cache = new Cache<string>(new CacheOptions
        {
            MaxSize = 10,
            BackgroundCleanupInterval = TimeSpan.FromMinutes(1)
        });
        cache.Dispose();
        cache.Dispose();
    }
}
