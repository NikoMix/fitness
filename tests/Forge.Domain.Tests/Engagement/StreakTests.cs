using Forge.Domain.Engagement;
using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Engagement;

/// <summary>
/// The engagement record holds a preference and a set of protected periods, and nothing else.
/// </summary>
/// <remarks>
/// The first test is the important one. It is written as an assertion about the type's public
/// surface rather than about behaviour, because the failure being guarded against is somebody
/// reintroducing a daily counter in good faith. A behavioural test would pass happily alongside a
/// new <c>CurrentDays</c> property; this one does not.
/// </remarks>
public sealed class StreakTests
{
    private static readonly DateOnly Monday = new(2026, 8, 17);

    [Fact]
    public void The_engagement_record_stores_no_day_counter()
    {
        var members = typeof(Streak)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        members.ShouldNotContain("CurrentDays");
        members.ShouldNotContain("BestDays");
        members.ShouldNotContain("FreezesRemaining");
        members.ShouldNotContain("LastCountedDate");
    }

    [Fact]
    public void A_streak_belongs_to_exactly_one_profile()
    {
        typeof(IProfileOwned).IsAssignableFrom(typeof(Streak)).ShouldBeTrue();

        var owner = Guid.CreateVersion7();
        var streak = new Streak { UserProfileId = owner };

        new ProfileScope(owner).Owns(streak).ShouldBeTrue();
        new ProfileScope(Guid.CreateVersion7()).Owns(streak).ShouldBeFalse();
        ProfileScope.None.Owns(streak).ShouldBeFalse();
    }

    [Fact]
    public void A_rest_day_changes_nothing_at_all()
    {
        var streak = new Streak();

        // There is no method that could record a rest day as an event, because rest is not an
        // event. The record is identical before and after any amount of not training.
        streak.ProtectedPeriods.ShouldBeEmpty();
        streak.IsProtectedOn(Monday).ShouldBeFalse();
        streak.AllowsSupportiveReminders(Monday).ShouldBeTrue();
    }

    [Fact]
    public void Marking_illness_protects_every_day_from_that_point()
    {
        var streak = new Streak();

        streak.Protect(new ProtectedPeriod(Monday, null, TrainingInterruption.Illness));

        streak.IsProtectedOn(Monday).ShouldBeTrue();
        streak.IsProtectedOn(Monday.AddDays(30)).ShouldBeTrue();
        streak.IsProtectedOn(Monday.AddDays(-1)).ShouldBeFalse();
        streak.ProtectionOn(Monday)!.Reason.ShouldBe(TrainingInterruption.Illness);
    }

    [Fact]
    public void Re_marking_the_same_reason_extends_rather_than_appends()
    {
        var streak = new Streak();

        // A screen that re-marks on every open must not grow the row without bound.
        streak.Protect(new ProtectedPeriod(Monday, null, TrainingInterruption.Illness));
        streak.Protect(new ProtectedPeriod(Monday.AddDays(1), null, TrainingInterruption.Illness));
        streak.Protect(new ProtectedPeriod(Monday.AddDays(2), null, TrainingInterruption.Illness));

        streak.ProtectedPeriods.Count.ShouldBe(1);
        streak.ProtectedPeriods[0].Start.ShouldBe(Monday);
        streak.ProtectedPeriods[0].IsOpenEnded.ShouldBeTrue();
    }

    [Fact]
    public void Ending_protection_closes_the_running_period_without_erasing_it()
    {
        var streak = new Streak();
        streak.Protect(new ProtectedPeriod(Monday, null, TrainingInterruption.Injury));

        streak.EndProtection(Monday.AddDays(6));

        streak.IsProtectedOn(Monday.AddDays(6)).ShouldBeTrue();
        streak.IsProtectedOn(Monday.AddDays(7)).ShouldBeFalse();
        streak.ProtectedPeriods.Count.ShouldBe(1);
    }

    [Fact]
    public void Reminders_are_suppressed_while_a_period_is_protected()
    {
        var streak = new Streak();
        streak.Protect(new ProtectedPeriod(Monday, null, TrainingInterruption.Illness));

        // The one day a streak app would most want to nudge somebody is the day they said they
        // are ill. That nudge is the behaviour this feature exists in order not to have.
        streak.AllowsSupportiveReminders(Monday).ShouldBeFalse();

        streak.EndProtection(Monday);
        streak.AllowsSupportiveReminders(Monday.AddDays(1)).ShouldBeTrue();
    }

    [Fact]
    public void Turning_gamification_off_suppresses_reminders_too()
    {
        var streak = new Streak();

        streak.SetGamificationEnabled(false);

        streak.AllowsSupportiveReminders(Monday).ShouldBeFalse();
    }

    [Fact]
    public void A_period_cannot_end_before_it_starts()
    {
        var streak = new Streak();

        Should.Throw<ArgumentException>(() =>
            streak.Protect(new ProtectedPeriod(Monday, Monday.AddDays(-1), TrainingInterruption.Deload)));
    }

    [Fact]
    public void Protection_can_be_cleared_when_it_was_entered_by_mistake()
    {
        var streak = new Streak();
        streak.Protect(new ProtectedPeriod(Monday, null, TrainingInterruption.LifeHappened));

        streak.ClearProtection();

        streak.ProtectedPeriods.ShouldBeEmpty();
        streak.IsProtectedOn(Monday).ShouldBeFalse();
    }

    [Fact]
    public void Every_interruption_reason_is_worded_without_blame()
    {
        foreach (var reason in Enum.GetValues<TrainingInterruption>())
        {
            var label = new ProtectedPeriod(Monday, null, reason).ReasonLabel;

            label.ShouldNotBeNullOrWhiteSpace();
            EngagementEthicsPolicy.IsPublishable($"Protected for {label}.").ShouldBeTrue(label);
        }
    }
}
