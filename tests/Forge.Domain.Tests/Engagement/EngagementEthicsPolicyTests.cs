using Forge.Domain.Engagement;
using Shouldly;

namespace Forge.Domain.Tests.Engagement;

public sealed class EngagementEthicsPolicyTests
{
    [Fact]
    public void Streak_break_copy_is_encouraging_not_shaming()
    {
        EngagementEthicsPolicy.IsSupportiveCopy(EngagementEthicsPolicy.SupportiveStreakBreakMessage).ShouldBeTrue();
        EngagementEthicsPolicy.SupportiveStreakBreakMessage.ShouldNotContain("failed", Case.Insensitive);
        EngagementEthicsPolicy.SupportiveStreakBreakMessage.ShouldNotContain("lazy", Case.Insensitive);
    }

    [Fact]
    public void Dark_pattern_terms_are_rejected()
    {
        EngagementEthicsPolicy.IsSupportiveCopy("Last chance before your streak expires").ShouldBeFalse();
        EngagementEthicsPolicy.IsSupportiveCopy("Start again when you are ready").ShouldBeTrue();
    }

    [Fact]
    public void Disablement_copy_protects_core_functionality()
    {
        EngagementEthicsPolicy.GamificationDisablementMessage.ShouldContain("without changing", Case.Insensitive);
        EngagementEthicsPolicy.GamificationDisablementMessage.ShouldContain("workout logging", Case.Insensitive);
        EngagementEthicsPolicy.IsSupportiveCopy(EngagementEthicsPolicy.GamificationDisablementMessage).ShouldBeTrue();
    }
}
