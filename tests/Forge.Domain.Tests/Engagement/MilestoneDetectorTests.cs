using Forge.Domain.Engagement;
using Shouldly;

namespace Forge.Domain.Tests.Engagement;

public sealed class MilestoneDetectorTests
{
    [Fact]
    public void Detector_finds_crossed_milestones_only()
    {
        var previous = new EngagementMetrics(0, 6, 9_000, 4, 0, 3);
        var current = new EngagementMetrics(1, 7, 10_500, 5, 1, 4);

        var milestones = MilestoneDetector.Detect(previous, current);

        milestones.Select(milestone => milestone.Title).ShouldContain("First workout logged");
        milestones.Select(milestone => milestone.Title).ShouldContain("Seven-day rhythm");
        milestones.Select(milestone => milestone.Title).ShouldContain("10,000 kg total volume");
        milestones.Select(milestone => milestone.Title).ShouldContain("Five exercises explored");
        milestones.Select(milestone => milestone.Title).ShouldContain("Personal record");
        milestones.ShouldAllBe(milestone => EngagementEthicsPolicy.IsSupportiveCopy(milestone.Message));
    }
}
