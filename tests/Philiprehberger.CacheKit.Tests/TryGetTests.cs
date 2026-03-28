using Xunit;
using Philiprehberger.CacheKit;

namespace Philiprehberger.CacheKit.Tests;

public class TryGetTests
{
    [Fact]
    public void TryGet_ReturnsTrueForExistingKey()
    {
        var cache = new Cache<string>(maxSize: 100);
        cache.Set("key1", "value1");

        var found = cache.TryGet("key1", out var value);

        Assert.True(found);
        Assert.Equal("value1", value);
    }

    [Fact]
    public void TryGet_ReturnsFalseForMissingKey()
    {
        var cache = new Cache<string>(maxSize: 100);

        var found = cache.TryGet("missing", out var value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryGet_ReturnsFalseForExpiredKey()
    {
        var cache = new Cache<string>(maxSize: 100);
        cache.Set("key1", "value1", ttl: TimeSpan.FromMilliseconds(1));
        Thread.Sleep(50);

        var found = cache.TryGet("key1", out var value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryGet_UpdatesLruOrder()
    {
        var cache = new Cache<string>(maxSize: 2);
        cache.Set("key1", "v1");
        cache.Set("key2", "v2");

        // Access key1 to make it most recently used
        cache.TryGet("key1", out _);

        // Adding key3 should evict key2 (least recently used), not key1
        cache.Set("key3", "v3");

        Assert.True(cache.Has("key1"));
        Assert.False(cache.Has("key2"));
    }

    [Fact]
    public void TryGet_WorksWithValueTypes()
    {
        var cache = new Cache<int>(maxSize: 100);
        cache.Set("key1", 42);

        var found = cache.TryGet("key1", out var value);

        Assert.True(found);
        Assert.Equal(42, value);
    }
}
