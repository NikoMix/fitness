using Forge.Domain.Measurement;
using Forge.Domain.Onboarding;
using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Onboarding;

public sealed class ProfileCompletionCalculatorTests
{
    [Fact]
    public void No_profile_reports_nothing_to_complete_and_no_ring_progress()
    {
        var completion = ProfileCompletionCalculator.Evaluate(null, null);

        completion.ProfileExists.ShouldBeFalse();
        completion.Fraction.ShouldBe(0d);
        completion.IsComplete.ShouldBeFalse();
        completion.Summary.ShouldBe("No profile yet");
    }

    [Fact]
    public void Skipped_onboarding_profile_is_reported_as_minimal()
    {
        var completion = ProfileCompletionCalculator.Evaluate(SkippedProfile(), null);

        completion.ProfileExists.ShouldBeTrue();
        completion.IsMinimal.ShouldBeTrue();
        completion.IsComplete.ShouldBeFalse();
        completion.Gaps.ShouldContain(gap => gap.Label == "Your name");
        completion.Gaps.ShouldContain(gap => gap.Label == "Your height");
        completion.Gaps.ShouldContain(gap => gap.Label == "Today's weight");
    }

    [Fact]
    public void Placeholder_display_name_does_not_count_as_answered()
    {
        var profile = FullProfile();
        profile.DisplayName = ProfileCompletionCalculator.PlaceholderDisplayName;

        ProfileCompletionCalculator.Evaluate(profile, LatestMetric(profile))
            .Gaps.ShouldContain(gap => gap.Label == "Your name");
    }

    [Fact]
    public void A_fully_answered_weight_goal_profile_is_complete()
    {
        var profile = FullProfile();

        var completion = ProfileCompletionCalculator.Evaluate(profile, LatestMetric(profile));

        completion.IsComplete.ShouldBeTrue();
        completion.Fraction.ShouldBe(1d);
        completion.Percent.ShouldBe(100);
        completion.Gaps.ShouldBeEmpty();
    }

    [Fact]
    public void Strength_goals_are_not_penalised_for_having_no_weight_target()
    {
        var profile = FullProfile();
        profile.Goal = FitnessGoal.BuildStrength;
        profile.TargetWeight = null;
        profile.GoalTimeframeWeeks = null;

        var completion = ProfileCompletionCalculator.Evaluate(profile, LatestMetric(profile));

        completion.IsComplete.ShouldBeTrue();
        completion.TotalCount.ShouldBe(6);
    }

    [Fact]
    public void Declining_to_state_sex_or_date_of_birth_still_reaches_full_completion()
    {
        var profile = FullProfile();
        profile.DateOfBirth = null;
        profile.BiologicalSex = BiologicalSex.PreferNotToSay;

        ProfileCompletionCalculator.Evaluate(profile, LatestMetric(profile)).IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void A_zero_weight_metric_does_not_count_as_a_recorded_weight()
    {
        var profile = FullProfile();
        var metric = LatestMetric(profile);
        metric.Weight = Mass.Zero;

        ProfileCompletionCalculator.Evaluate(profile, metric)
            .Gaps.ShouldContain(gap => gap.Label == "Today's weight");
    }

    [Fact]
    public void Every_gap_points_at_the_step_that_collects_it()
    {
        var completion = ProfileCompletionCalculator.Evaluate(SkippedProfile(), null);

        completion.Gaps.ShouldContain(gap => gap.Label == "Your height" && gap.Step == OnboardingStep.BodyMetrics);
        completion.Gaps.ShouldContain(gap => gap.Label == "Training background" && gap.Step == OnboardingStep.Experience);
        completion.Gaps.ShouldAllBe(gap => !string.IsNullOrWhiteSpace(gap.Reason));
    }

    [Fact]
    public void Mostly_complete_profile_is_no_longer_treated_as_minimal()
    {
        var profile = FullProfile();
        profile.ExperienceLevel = TrainingExperienceLevel.Unspecified;

        var completion = ProfileCompletionCalculator.Evaluate(profile, LatestMetric(profile));

        completion.IsMinimal.ShouldBeFalse();
        completion.IsComplete.ShouldBeFalse();
        completion.Summary.ShouldBe("7 of 8 answered");
        completion.GapLabels.ShouldBe("Training background");
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
        DateOfBirth = new DateOnly(1990, 5, 4),
        TrainingDaysPerWeek = 3,
    };

    private static BodyMetric LatestMetric(UserProfile profile) => new()
    {
        UserProfileId = profile.Id,
        Weight = Mass.FromKilograms(82m),
        RecordedUtc = DateTimeOffset.UtcNow,
    };
}
