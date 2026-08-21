using Forge.Domain.Dashboard;
using Forge.Domain.Measurement;
using Forge.Domain.Onboarding;
using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Dashboard;

public sealed class TodayFocusPlannerTests
{
    [Fact]
    public void With_no_profile_the_hero_action_is_setup()
    {
        var focus = TodayFocusPlanner.Plan(new TodayFocusInputs(
            ProfileCompletionCalculator.Evaluate(null, null),
            HasScheduledSession: false,
            TrainingRingProgress: 0d,
            RecentActivityCount: 0));

        focus.Kind.ShouldBe(TodayFocusKind.FinishSetup);
        focus.PrimaryAction.ShouldBe(TodayFocusAction.FinishSetup);
        focus.Headline.ShouldBe("Set up Forge");
    }

    [Fact]
    public void A_skipped_onboarding_profile_leads_with_the_specific_missing_answers()
    {
        var focus = TodayFocusPlanner.Plan(new TodayFocusInputs(
            ProfileCompletionCalculator.Evaluate(SkippedProfile(), null),
            HasScheduledSession: false,
            TrainingRingProgress: 0d,
            RecentActivityCount: 0));

        focus.Kind.ShouldBe(TodayFocusKind.FinishSetup);
        focus.Headline.ShouldBe("Finish setting up Forge");
        focus.Message.ShouldContain("your height");
        focus.Message.ShouldContain("today's weight");
        focus.ShowsSetupNudge.ShouldBeFalse("setup is already the hero, so a second prompt would be noise");
    }

    [Fact]
    public void A_nearly_complete_profile_demotes_setup_to_a_quiet_nudge()
    {
        var profile = FullProfile();
        profile.ExperienceLevel = TrainingExperienceLevel.Unspecified;

        var focus = TodayFocusPlanner.Plan(new TodayFocusInputs(
            ProfileCompletionCalculator.Evaluate(profile, LatestMetric(profile)),
            HasScheduledSession: false,
            TrainingRingProgress: 0d,
            RecentActivityCount: 0));

        focus.Kind.ShouldBe(TodayFocusKind.StartFirstWorkout);
        focus.ShowsSetupNudge.ShouldBeTrue();
        focus.SetupNudge.ShouldContain("training background");
    }

    [Fact]
    public void A_complete_profile_with_no_history_is_asked_for_a_first_set()
    {
        var focus = PlanFor(hasScheduledSession: false, ringProgress: 0d, recentActivity: 0);

        focus.Kind.ShouldBe(TodayFocusKind.StartFirstWorkout);
        focus.PrimaryAction.ShouldBe(TodayFocusAction.StartWorkout);
        focus.ShowsSetupNudge.ShouldBeFalse();
        focus.Message.ShouldContain("Nothing is logged yet");
    }

    [Fact]
    public void A_scheduled_session_becomes_the_hero()
    {
        var focus = PlanFor(hasScheduledSession: true, ringProgress: 0d, recentActivity: 4);

        focus.Kind.ShouldBe(TodayFocusKind.StartPlannedSession);
        focus.PrimaryActionLabel.ShouldBe("Start today's session");
    }

    [Fact]
    public void Partially_logged_training_offers_to_continue_rather_than_restart()
    {
        var focus = PlanFor(hasScheduledSession: true, ringProgress: 0.4d, recentActivity: 4);

        focus.Kind.ShouldBe(TodayFocusKind.ContinueLogging);
        focus.PrimaryActionLabel.ShouldBe("Continue training");
    }

    [Fact]
    public void A_completed_ring_switches_the_hero_to_review()
    {
        var focus = PlanFor(hasScheduledSession: true, ringProgress: 1d, recentActivity: 4);

        focus.Kind.ShouldBe(TodayFocusKind.ReviewCompletedDay);
        focus.PrimaryAction.ShouldBe(TodayFocusAction.ReviewToday);
    }

    [Fact]
    public void An_experienced_user_with_nothing_scheduled_is_offered_an_open_workout()
    {
        var focus = PlanFor(hasScheduledSession: false, ringProgress: 0d, recentActivity: 9);

        focus.Kind.ShouldBe(TodayFocusKind.StartOpenWorkout);
        focus.Message.ShouldContain("no planned session");
    }

    [Fact]
    public void Ring_summary_states_when_nothing_has_been_logged()
    {
        TodayFocusPlanner.DescribeRings([0d, 0d, 0d]).ShouldBe("Nothing logged against today's rings yet.");
    }

    [Fact]
    public void Ring_summary_counts_complete_and_started_rings()
    {
        TodayFocusPlanner.DescribeRings([1d, 0.5d, 0d]).ShouldBe("1 of 3 rings complete, 2 started.");
        TodayFocusPlanner.DescribeRings([1d, 1d, 1d]).ShouldBe("All 3 rings are complete.");
    }

    [Fact]
    public void Ring_summary_copes_with_no_rings_at_all()
    {
        TodayFocusPlanner.DescribeRings([]).ShouldBe("No rings to show yet.");
    }

    [Fact]
    public void Empty_state_copy_never_pretends_there_is_data()
    {
        var focus = PlanFor(hasScheduledSession: false, ringProgress: 0d, recentActivity: 0);

        focus.Message.ShouldNotContain("sample", Case.Insensitive);
        focus.Message.ShouldNotContain("example", Case.Insensitive);
        focus.Message.ShouldNotContain("demo", Case.Insensitive);
    }

    private static TodayFocus PlanFor(bool hasScheduledSession, double ringProgress, int recentActivity)
    {
        var profile = FullProfile();
        return TodayFocusPlanner.Plan(new TodayFocusInputs(
            ProfileCompletionCalculator.Evaluate(profile, LatestMetric(profile)),
            hasScheduledSession,
            ringProgress,
            recentActivity));
    }

    [Fact]
    public void Every_branch_produces_text_for_every_slot_the_card_renders()
    {
        // The Today hero card renders Headline, Message and PrimaryActionLabel unconditionally.
        // An empty value there is not "no data", it is a blank slab that reads as a broken app, so
        // every reachable branch is walked rather than trusting the ones the other tests cover.
        foreach (var focus in EveryBranch())
        {
            focus.Headline.ShouldNotBeNullOrWhiteSpace($"{focus.Kind} headline");
            focus.Message.ShouldNotBeNullOrWhiteSpace($"{focus.Kind} message");
            focus.PrimaryActionLabel.ShouldNotBeNullOrWhiteSpace($"{focus.Kind} action label");
        }
    }

    [Fact]
    public void Every_branch_reaches_a_registered_destination()
    {
        EveryBranch().Select(focus => focus.Kind).Distinct().Count().ShouldBe(6);
    }

    [Fact]
    public void The_setup_nudge_is_never_shown_without_something_to_say()
    {
        foreach (var focus in EveryBranch())
        {
            if (focus.ShowsSetupNudge)
            {
                focus.SetupNudge.ShouldNotBeNullOrWhiteSpace($"{focus.Kind} nudge");
            }
        }
    }

    [Fact]
    public void A_focus_that_shows_an_empty_nudge_is_corrected_rather_than_rendered()
    {
        var focus = new TodayFocus(
            TodayFocusKind.StartOpenWorkout,
            "Headline",
            "Message",
            "Do the thing",
            TodayFocusAction.StartWorkout,
            ShowsSetupNudge: true,
            SetupNudge: "   ");

        focus.ShowsSetupNudge.ShouldBeFalse("a visible empty label reserves layout and looks like a fault");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_focus_cannot_be_built_without_a_headline_message_or_action_label(string blank)
    {
        Should.Throw<ArgumentException>(() => new TodayFocus(
            TodayFocusKind.StartOpenWorkout, blank, "Message", "Label", TodayFocusAction.StartWorkout, false, ""));

        Should.Throw<ArgumentException>(() => new TodayFocus(
            TodayFocusKind.StartOpenWorkout, "Headline", blank, "Label", TodayFocusAction.StartWorkout, false, ""));

        Should.Throw<ArgumentException>(() => new TodayFocus(
            TodayFocusKind.StartOpenWorkout, "Headline", "Message", blank, TodayFocusAction.StartWorkout, false, ""));
    }

    private static IReadOnlyList<TodayFocus> EveryBranch()
    {
        var complete = FullProfile();
        var completeCompletion = ProfileCompletionCalculator.Evaluate(complete, LatestMetric(complete));

        var partial = FullProfile();
        partial.ExperienceLevel = TrainingExperienceLevel.Unspecified;
        var partialCompletion = ProfileCompletionCalculator.Evaluate(partial, LatestMetric(partial));

        var skipped = ProfileCompletionCalculator.Evaluate(SkippedProfile(), null);
        var missing = ProfileCompletionCalculator.Evaluate(null, null);

        return
        [
            TodayFocusPlanner.Plan(new TodayFocusInputs(missing, false, 0d, 0)),
            TodayFocusPlanner.Plan(new TodayFocusInputs(skipped, false, 0d, 0)),
            TodayFocusPlanner.Plan(new TodayFocusInputs(completeCompletion, false, 0d, 0)),
            TodayFocusPlanner.Plan(new TodayFocusInputs(completeCompletion, true, 0d, 4)),
            TodayFocusPlanner.Plan(new TodayFocusInputs(completeCompletion, true, 0.4d, 4)),
            TodayFocusPlanner.Plan(new TodayFocusInputs(completeCompletion, true, 1d, 4)),
            TodayFocusPlanner.Plan(new TodayFocusInputs(completeCompletion, false, 0d, 9)),
            TodayFocusPlanner.Plan(new TodayFocusInputs(partialCompletion, false, 0d, 0)),
            TodayFocusPlanner.Plan(new TodayFocusInputs(partialCompletion, true, 0.5d, 3)),
        ];
    }

    private static UserProfile SkippedProfile() => new()
    {
        DisplayName = ProfileCompletionCalculator.PlaceholderDisplayName,
        Goal = FitnessGoal.Maintain,
        AvailableEquipment = "Bodyweight",
        TrainingDaysPerWeek = 3,
    };

    private static UserProfile FullProfile() => new()
    {
        DisplayName = "Sam",
        Goal = FitnessGoal.LoseWeight,
        Height = Length.FromCentimetres(178m),
        ExperienceLevel = TrainingExperienceLevel.Beginner,
        AvailableEquipment = "Bodyweight, Dumbbells",
        TargetWeight = Mass.FromKilograms(78m),
        GoalTimeframeWeeks = 12,
        TrainingDaysPerWeek = 3,
    };

    private static BodyMetric LatestMetric(UserProfile profile) => new()
    {
        UserProfileId = profile.Id,
        Weight = Mass.FromKilograms(82m),
        RecordedUtc = DateTimeOffset.UtcNow,
    };
}
