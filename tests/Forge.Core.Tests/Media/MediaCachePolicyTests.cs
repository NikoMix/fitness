using Forge.Core.Abstractions.Media;
using NSubstitute;
using Shouldly;

namespace Forge.Core.Tests.Media;

public sealed class MediaCachePolicyTests
{
    [Fact]
    public void SelectEvictionCandidates_evicts_oldest_entries_until_download_fits()
    {
        var now = DateTimeOffset.UtcNow;
        var policy = new MediaCachePolicy(storageCapBytes: 10_000);
        MediaCacheEntry[] entries =
        [
            Entry("old", 4_000, now.AddHours(-3)),
            Entry("middle", 3_000, now.AddHours(-2)),
            Entry("new", 2_000, now.AddHours(-1))
        ];

        var evictions = policy.SelectEvictionCandidates(entries, incomingBytes: 4_000);

        evictions.Select(entry => entry.AssetKey).ShouldBe(["old"]);
    }

    [Fact]
    public void SelectEvictionCandidates_protects_asset_being_replaced()
    {
        var now = DateTimeOffset.UtcNow;
        var policy = new MediaCachePolicy(storageCapBytes: 8_000);
        MediaCacheEntry[] entries =
        [
            Entry("keep", 5_000, now.AddHours(-5)),
            Entry("remove", 2_000, now.AddHours(-1))
        ];

        var evictions = policy.SelectEvictionCandidates(entries, incomingBytes: 3_000, protectedAssetKey: "keep");

        evictions.Select(entry => entry.AssetKey).ShouldBe(["remove"]);
    }

    [Fact]
    public void CanEverFit_rejects_assets_larger_than_storage_cap()
    {
        var policy = new MediaCachePolicy(storageCapBytes: 5_000);

        policy.CanEverFit(5_001).ShouldBeFalse();
        policy.SelectEvictionCandidates([Entry("old", 5_000, DateTimeOffset.UtcNow)], 5_001).ShouldBeEmpty();
    }

    [Fact]
    public async Task IMediaCache_reports_storage_cap_failures_without_throwing()
    {
        var cache = Substitute.For<IMediaCache>();
        cache.DownloadAsync(Arg.Any<MediaAssetDownloadRequest>(), TestContext.Current.CancellationToken)
            .Returns(new MediaDownloadResult(MediaDownloadStatus.RejectedByStorageCap, null, "Too large."));

        var result = await cache.DownloadAsync(
            new MediaAssetDownloadRequest("oversize", "Bench Press", new Uri("https://example.test/bench.mp4"), "bench.mp4", 50_000_000),
            TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.Status.ShouldBe(MediaDownloadStatus.RejectedByStorageCap);
        result.Message.ShouldNotBeNullOrWhiteSpace();
    }

    private static MediaCacheEntry Entry(string key, long bytes, DateTimeOffset lastAccessedAt) =>
        new(key, key, key + ".mp4", bytes, lastAccessedAt);
}
