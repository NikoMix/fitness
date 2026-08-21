using Forge.Domain.Onboarding;

namespace Forge.Domain.Dashboard;

/// <summary>
/// Chooses the single most useful thing the Today screen can offer right now.
/// </summary>
/// <remarks>
/// <para>
/// Today is the first screen after launch and the most visited one, so it has to answer one
/// question - what should I do next - rather than present a wall of equally weighted cards. This
/// planner picks exactly one hero action from state that is already persisted. It never invents a
/// number: everything it says is derived from counts the caller read from the database.
/// </para>
/// <para>
/// Setup is the hero only while the profile is barely filled in. Once it is half complete the
/// remaining gaps drop to a quiet secondary nudge, because someone who deliberately skipped
/// onboarding should not be met by the same demand every single launch.
/// </para>
/// </remarks>
public static class TodayFocusPlanner
{
    /// <summary>Plans the hero action for Today.</summary>
    /// <param name="inputs">Counts and flags read from local storage.</param>
    /// <returns>The single next useful action, plus an optional secondary setup nudge.</returns>
    public static TodayFocus Plan(TodayFocusInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var completion = inputs.Completion;
        var nudge = BuildNudge(completion);

        if (!completion.ProfileExists || completion.IsMinimal)
        {
            return new TodayFocus(
                TodayFocusKind.FinishSetup,
                completion.ProfileExists ? "Finish setting up Forge" : "Set up Forge",
                completion.Gaps.Count > 0
                    ? $"Forge still needs {completion.GapLabels.ToLowerInvariant()}. That is a minute of work and it is what turns the rings, plans and trends below into your numbers instead of blanks."
                    : "A short setup gives Forge enough to plan training and track progress against your own numbers.",
                "Finish setup",
                TodayFocusAction.FinishSetup,
                ShowsSetupNudge: false,
                SetupNudge: string.Empty);
        }

        if (inputs.TrainingRingProgress >= 1d)
        {
            return new TodayFocus(
                TodayFocusKind.ReviewCompletedDay,
                "Today's training is done",
                "Every planned working set is logged. Reviewing what you lifted is the fastest way to pick next session's numbers.",
                "Review today",
                TodayFocusAction.ReviewToday,
                nudge.Length > 0,
                nudge);
        }

        if (inputs.TrainingRingProgress > 0d)
        {
            return new TodayFocus(
                TodayFocusKind.ContinueLogging,
                "You are partway through today",
                "Some sets are already logged. Pick up where you left off and Forge will keep the ring in step.",
                "Continue training",
                TodayFocusAction.StartWorkout,
                nudge.Length > 0,
                nudge);
        }

        if (inputs.HasScheduledSession)
        {
            return new TodayFocus(
                TodayFocusKind.StartPlannedSession,
                "Your session is ready",
                "Today's session comes from your active plan. Start it and Forge logs sets as you go.",
                "Start today's session",
                TodayFocusAction.StartWorkout,
                nudge.Length > 0,
                nudge);
        }

        if (inputs.RecentActivityCount == 0)
        {
            return new TodayFocus(
                TodayFocusKind.StartFirstWorkout,
                "Log your first set",
                "Nothing is logged yet, so there is nothing for Forge to chart. One set is enough to start every trend on this screen.",
                "Start a workout",
                TodayFocusAction.StartWorkout,
                nudge.Length > 0,
                nudge);
        }

        return new TodayFocus(
            TodayFocusKind.StartOpenWorkout,
            "Nothing scheduled today",
            "There is no planned session for today. Train freely and Forge will log it, or pick a plan so future days schedule themselves.",
            "Start an open workout",
            TodayFocusAction.StartWorkout,
            nudge.Length > 0,
            nudge);
    }

    /// <summary>Describes how many rings are complete, for a screen-reader friendly summary.</summary>
    /// <param name="ringProgress">Each ring's progress between 0 and 1.</param>
    /// <returns>A short sentence describing ring state.</returns>
    public static string DescribeRings(IReadOnlyList<double> ringProgress)
    {
        ArgumentNullException.ThrowIfNull(ringProgress);

        if (ringProgress.Count == 0)
        {
            return "No rings to show yet.";
        }

        var complete = ringProgress.Count(progress => progress >= 1d);
        var started = ringProgress.Count(progress => progress > 0d);

        if (started == 0)
        {
            return "Nothing logged against today's rings yet.";
        }

        return complete == ringProgress.Count
            ? FormattableString.Invariant($"All {ringProgress.Count} rings are complete.")
            : FormattableString.Invariant($"{complete} of {ringProgress.Count} rings complete, {started} started.");
    }

    private static string BuildNudge(ProfileCompletion completion)
        => completion.IsComplete || !completion.ProfileExists
            ? string.Empty
            : $"Forge is still missing {completion.GapLabels.ToLowerInvariant()}. Adding them sharpens the numbers on this screen.";
}

/// <summary>Everything the planner needs, read from local storage by the caller.</summary>
/// <param name="Completion">How complete the local profile is.</param>
/// <param name="HasScheduledSession">Whether an active plan schedules a session for today.</param>
/// <param name="TrainingRingProgress">Today's training ring progress between 0 and 1.</param>
/// <param name="RecentActivityCount">How many recent activity entries exist across all time.</param>
public sealed record TodayFocusInputs(
    ProfileCompletion Completion,
    bool HasScheduledSession,
    double TrainingRingProgress,
    int RecentActivityCount);

/// <summary>The kind of hero state Today is showing.</summary>
public enum TodayFocusKind
{
    /// <summary>The profile is too sparse for anything else to be useful.</summary>
    FinishSetup,

    /// <summary>A profile exists but nothing has ever been logged.</summary>
    StartFirstWorkout,

    /// <summary>An active plan schedules a session for today.</summary>
    StartPlannedSession,

    /// <summary>Some of today's sets are logged but the ring is not full.</summary>
    ContinueLogging,

    /// <summary>Today's planned work is complete.</summary>
    ReviewCompletedDay,

    /// <summary>Nothing is scheduled, but the user has trained before.</summary>
    StartOpenWorkout,
}

/// <summary>Where the hero action should take the user.</summary>
public enum TodayFocusAction
{
    /// <summary>Open the goal wizard to complete setup.</summary>
    FinishSetup,

    /// <summary>Open the training surface to log work.</summary>
    StartWorkout,

    /// <summary>Open the plan list.</summary>
    ChoosePlan,

    /// <summary>Review what was logged today.</summary>
    ReviewToday,
}

/// <summary>The single next useful action for Today.</summary>
/// <param name="Kind">The state that produced this focus.</param>
/// <param name="Headline">A short heading.</param>
/// <param name="Message">One or two sentences explaining the action and why it helps.</param>
/// <param name="PrimaryActionLabel">The button label.</param>
/// <param name="PrimaryAction">Where the button leads.</param>
/// <param name="ShowsSetupNudge">Whether a quiet secondary setup prompt should also be shown.</param>
/// <param name="SetupNudge">The secondary prompt text, empty when none applies.</param>
/// <remarks>
/// The invariants are enforced at construction rather than trusted. Today's hero card is the first
/// thing anyone sees, and an empty headline, message or button label renders as a blank slab that
/// looks like a broken app rather than like missing data. Enforcing it here means no future branch
/// can introduce that by omission.
/// </remarks>
public sealed record TodayFocus(
    TodayFocusKind Kind,
    string Headline,
    string Message,
    string PrimaryActionLabel,
    TodayFocusAction PrimaryAction,
    bool ShowsSetupNudge,
    string SetupNudge)
{
    /// <summary>A short heading. Never empty.</summary>
    public string Headline { get; } = Require(Headline, nameof(Headline));

    /// <summary>The explanation shown under the heading. Never empty.</summary>
    public string Message { get; } = Require(Message, nameof(Message));

    /// <summary>The primary button label. Never empty.</summary>
    public string PrimaryActionLabel { get; } = Require(PrimaryActionLabel, nameof(PrimaryActionLabel));

    /// <summary>Whether a quiet secondary setup prompt should also be shown.</summary>
    /// <remarks>
    /// Forced to <see langword="false"/> when <see cref="SetupNudge"/> has nothing to say, because
    /// a visible empty label reserves layout and leaves a gap that reads as a rendering fault.
    /// </remarks>
    public bool ShowsSetupNudge { get; } = ShowsSetupNudge && !string.IsNullOrWhiteSpace(SetupNudge);

    private static string Require(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"A Today focus must supply {name}; an empty value renders as a blank card.", name)
            : value;
}
