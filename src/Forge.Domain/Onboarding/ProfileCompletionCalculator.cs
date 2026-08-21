using Forge.Domain.Profile;

namespace Forge.Domain.Onboarding;

/// <summary>
/// Works out how much of a local profile is actually filled in, and what is missing.
/// </summary>
/// <remarks>
/// <para>
/// Onboarding can be skipped, which writes a deliberately minimal profile so the app is usable
/// immediately. That is the right trade, but it leaves screens reading a profile whose goal,
/// height and weight are all defaults. Without this calculator the only honest thing Today and
/// Profile could show is a blank; with it they can show precisely which two or three answers
/// would make the rest of the app useful, and link straight to them.
/// </para>
/// <para>
/// Date of birth and biological sex are deliberately excluded. Both are optional by design, and a
/// completion score that can never reach 100% for someone who declines to state them would punish
/// a choice Forge explicitly offers.
/// </para>
/// </remarks>
public static class ProfileCompletionCalculator
{
    /// <summary>
    /// The display name written when onboarding is skipped.
    /// </summary>
    /// <remarks>
    /// Treated as "not yet answered" rather than as a real name, so skipping does not count
    /// towards completion just because a column is non-empty.
    /// </remarks>
    public const string PlaceholderDisplayName = "Me";

    /// <summary>Evaluates completeness of a profile and its most recent body metric.</summary>
    /// <param name="profile">The stored profile, or <see langword="null"/> when none exists.</param>
    /// <param name="latestMetric">The most recent body metric, or <see langword="null"/>.</param>
    /// <returns>The completion state, including every outstanding gap.</returns>
    public static ProfileCompletion Evaluate(UserProfile? profile, BodyMetric? latestMetric)
    {
        if (profile is null)
        {
            return new ProfileCompletion(0, 0, []);
        }

        var gaps = new List<ProfileGap>();
        var total = 0;
        var completed = 0;

        Check(
            !string.IsNullOrWhiteSpace(profile.DisplayName)
                && !string.Equals(profile.DisplayName.Trim(), PlaceholderDisplayName, StringComparison.OrdinalIgnoreCase),
            new ProfileGap(
                "Your name",
                "Add the name you want Forge to use so the app stops calling you \"Me\".",
                OnboardingStep.Goal));

        Check(
            profile.Goal != FitnessGoal.Unspecified,
            new ProfileGap(
                "A goal",
                "Pick a goal so Forge knows how much training to plan and which numbers matter.",
                OnboardingStep.Goal));

        Check(
            profile.Height > Length.Zero,
            new ProfileGap(
                "Your height",
                "Height lets Forge estimate energy needs and check that a target weight is sensible.",
                OnboardingStep.BodyMetrics));

        Check(
            latestMetric is not null && latestMetric.Weight > Measurement.Mass.Zero,
            new ProfileGap(
                "Today's weight",
                "One weight entry gives every trend and progress chart something to measure from.",
                OnboardingStep.BodyMetrics));

        Check(
            profile.ExperienceLevel != TrainingExperienceLevel.Unspecified,
            new ProfileGap(
                "Training background",
                "Your starting level sets the difficulty of the first sessions Forge suggests.",
                OnboardingStep.Experience));

        Check(
            !string.IsNullOrWhiteSpace(profile.AvailableEquipment),
            new ProfileGap(
                "Available equipment",
                "Forge only suggests exercises you can do with the equipment you list.",
                OnboardingStep.Equipment));

        if (UsesWeightTarget(profile.Goal))
        {
            Check(
                profile.TargetWeight is { } target && target > Measurement.Mass.Zero,
                new ProfileGap(
                    "A target weight",
                    "A target is what the safety check compares against to keep the pace gradual.",
                    OnboardingStep.Goal));

            Check(
                profile.GoalTimeframeWeeks is > 0,
                new ProfileGap(
                    "A timeframe",
                    "A timeframe turns the target into a weekly rate Forge can check.",
                    OnboardingStep.Goal));
        }

        return new ProfileCompletion(completed, total, gaps);

        void Check(bool satisfied, ProfileGap gap)
        {
            total++;
            if (satisfied)
            {
                completed++;
                return;
            }

            gaps.Add(gap);
        }
    }

    private static bool UsesWeightTarget(FitnessGoal goal)
        => goal is FitnessGoal.LoseWeight or FitnessGoal.Maintain or FitnessGoal.GainWeight;
}

/// <summary>
/// One answer a profile is still missing.
/// </summary>
/// <param name="Label">A short noun phrase naming the missing answer.</param>
/// <param name="Reason">Why Forge asks for it, in one sentence.</param>
/// <param name="Step">The onboarding step that collects it, so the UI can link straight there.</param>
public sealed record ProfileGap(string Label, string Reason, OnboardingStep Step);

/// <summary>How complete a local profile is.</summary>
/// <param name="CompletedCount">The number of answers supplied.</param>
/// <param name="TotalCount">The number of answers that apply to this profile's goal.</param>
/// <param name="Gaps">The outstanding answers, in the order they are asked for.</param>
public sealed record ProfileCompletion(int CompletedCount, int TotalCount, IReadOnlyList<ProfileGap> Gaps)
{
    /// <summary>Completion between 0 and 1, suitable for a progress ring.</summary>
    public double Fraction => TotalCount <= 0 ? 0d : (double)CompletedCount / TotalCount;

    /// <summary>Completion as a whole percentage.</summary>
    public int Percent => (int)Math.Round(Fraction * 100, MidpointRounding.AwayFromZero);

    /// <summary>Whether a profile exists at all.</summary>
    public bool ProfileExists => TotalCount > 0;

    /// <summary>Whether every applicable answer has been supplied.</summary>
    public bool IsComplete => ProfileExists && Gaps.Count == 0;

    /// <summary>
    /// Whether the profile is little more than the record created by skipping onboarding.
    /// </summary>
    /// <remarks>
    /// Used to decide whether a screen should lead with a setup prompt rather than with data.
    /// </remarks>
    public bool IsMinimal => ProfileExists && Fraction < 0.5d;

    /// <summary>A short summary such as "4 of 6 answered".</summary>
    public string Summary => ProfileExists
        ? FormattableString.Invariant($"{CompletedCount} of {TotalCount} answered")
        : "No profile yet";

    /// <summary>The outstanding answers as a comma-separated list, for a one-line prompt.</summary>
    public string GapLabels => string.Join(", ", Gaps.Select(gap => gap.Label));
}
