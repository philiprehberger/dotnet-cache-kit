using Xunit;
using Philiprehberger.CacheKit;

namespace Philiprehberger.CacheKit.Tests;

public class TagStatisticsTests
{
    [Fact]
    public void TagStats_TracksHitsForTag()
    {
        var cache = new Cache<string>(maxSize: 100);

        cache.Set("key1", "value1", tags: new[] { "tagA" });
        cache.Get("key1");
        cache.Get("key1");

        var stats = cache.GetTagStatistics("tagA");
        Assert.Equal(2, stats.Hits);
    }

    [Fact]
    public void TagStats_TracksMissesForExpiredTaggedEntry()
    {
        var cache = new Cache<string>(maxSize: 100);

        cache.Set("key1", "value1", ttl: TimeSpan.FromMilliseconds(1), tags: new[] { "tagA" });
        Thread.Sleep(50);
        cache.Get("key1"); // expired = miss

        var stats = cache.GetTagStatistics("tagA");
        Assert.Equal(1, stats.Misses);
    }

    [Fact]
    public void TagStats_TracksEvictionsForTag()
    {
        var cache = new Cache<string>(maxSize: 2);

        cache.Set("key1", "value1", tags: new[] { "tagA" });
        cache.Set("key2", "value2", tags: new[] { "tagB" });
        cache.Set("key3", "value3"); // evicts key1

        var stats = cache.GetTagStatistics("tagA");
        Assert.Equal(1, stats.Evictions);
    }

    [Fact]
    public void TagStats_ReturnsZeroForUnknownTag()
    {
        var cache = new Cache<string>(maxSize: 100);

        var stats = cache.GetTagStatistics("nonexistent");
        Assert.Equal(0, stats.Hits);
        Assert.Equal(0, stats.Misses);
        Assert.Equal(0, stats.Evictions);
    }

    [Fact]
    public void TagStats_TracksMultipleTagsPerEntry()
    {
        var cache = new Cache<string>(maxSize: 100);

        cache.Set("key1", "value1", tags: new[] { "tagA", "tagB" });
        cache.Get("key1");

        var statsA = cache.GetTagStatistics("tagA");
        var statsB = cache.GetTagStatistics("tagB");
        Assert.Equal(1, statsA.Hits);
        Assert.Equal(1, statsB.Hits);
    }
}
