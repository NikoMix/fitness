using Forge.Core.Abstractions.Health;
using Shouldly;

namespace Forge.Core.Tests.Health;

/// <summary>
/// Covers the sync-time wording. A "last synced" label that is subtly wrong is worse than none at
/// all: it is the line a user reads before deciding whether to trust the numbers next to it.
/// </summary>
public sealed class HealthSyncLabelsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Never_synced_is_stated_plainly()
    {
        HealthSyncLabels.DescribeLastSync(null, Now).ShouldBe("Never synced");
    }

    [Theory]
    [InlineData(0, "Synced just now")]
    [InlineData(59, "Synced just now")]
    [InlineData(60, "Synced 1 minute ago")]
    [InlineData(120, "Synced 2 minutes ago")]
    [InlineData(3600, "Synced 1 hour ago")]
    [InlineData(10800, "Synced 3 hours ago")]
    [InlineData(86400, "Synced 1 day ago")]
    [InlineData(259200, "Synced 3 days ago")]
    public void Elapsed_time_is_described_in_the_largest_sensible_unit(int elapsedSeconds, string expected)
    {
        HealthSyncLabels.DescribeLastSync(Now.AddSeconds(-elapsedSeconds), Now).ShouldBe(expected);
    }

    [Fact]
    public void Singular_and_plural_are_correct_at_the_boundaries()
    {
        HealthSyncLabels.DescribeLastSync(Now.AddMinutes(-1), Now).ShouldBe("Synced 1 minute ago");
        HealthSyncLabels.DescribeLastSync(Now.AddHours(-1), Now).ShouldBe("Synced 1 hour ago");
        HealthSyncLabels.DescribeLastSync(Now.AddDays(-1), Now).ShouldBe("Synced 1 day ago");
    }

    [Fact]
    public void Anything_older_than_a_week_shows_a_date()
    {
        HealthSyncLabels.DescribeLastSync(Now.AddDays(-8), Now).ShouldStartWith("Synced on ");
    }

    [Fact]
    public void A_future_timestamp_is_clamped_rather_than_rendered_as_a_negative()
    {
        // A clock change, time-zone travel or a restored backup can all leave a stored timestamp
        // ahead of now. "Synced in 3 hours" reads as a defect, so it is treated as current.
        HealthSyncLabels.DescribeLastSync(Now.AddHours(3), Now).ShouldBe("Synced just now");
    }

    [Fact]
    public void The_label_never_implies_a_sync_that_did_not_happen()
    {
        HealthSyncLabels.DescribeLastSync(null, Now).ShouldNotContain("Synced ");
    }
}
