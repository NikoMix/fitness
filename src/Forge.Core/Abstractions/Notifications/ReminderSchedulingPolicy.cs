namespace Forge.Core.Abstractions.Notifications;

/// <summary>Notification authorization state surfaced to reminder UI without platform types.</summary>
public enum ForgeNotificationPermissionState
{
    /// <summary>The app has not asked yet, or the platform cannot report a permanent result.</summary>
    Unknown,

    /// <summary>The app is allowed to schedule visible local notifications.</summary>
    Authorized,

    /// <summary>The user has denied notifications; Forge should explain settings instead of prompting again.</summary>
    Denied
}

/// <summary>User-controllable reminder switches and delivery preferences.</summary>
public sealed record ReminderPreferences(
    bool WorkoutRemindersEnabled,
    bool HydrationRemindersEnabled,
    bool DailyCheckInEnabled,
    bool StreakProtectionEnabled,
    QuietHoursPolicy QuietHours,
    int DailyNotificationCap,
    TimeOnly WorkoutReminderTime,
    TimeOnly HydrationNudgeTime,
    TimeOnly DailyCheckInTime,
    TimeOnly StreakWarningTime,
    decimal HydrationTargetMillilitres);

/// <summary>Local user state required for reminder decisions.</summary>
public sealed record ReminderUserSnapshot(
    DateOnly LocalDate,
    bool IsTrainingDay,
    string? PlannedWorkoutName,
    bool HasCompletedWorkoutToday,
    decimal HydrationConsumedMillilitres,
    bool HasCompletedDailyCheckIn,
    bool StreakProtectionAvailable);

/// <summary>Inputs for pure reminder planning.</summary>
public sealed record ReminderSchedulingInput(
    DateTimeOffset Now,
    TimeZoneInfo LocalTimeZone,
    ForgeNotificationPermissionState PermissionState,
    ReminderPreferences Preferences,
    ReminderUserSnapshot UserState,
    int AlreadyScheduledCountForLocalDay);

/// <summary>A planned reminder with stable metadata for scheduling.</summary>
public sealed record PlannedReminder(
    ForgeNotificationRequest Request,
    ReminderKind Kind,
    ReminderSuppressionReason? SuppressionReason = null);

/// <summary>Reminder kinds planned by Forge.</summary>
public enum ReminderKind
{
    /// <summary>A scheduled workout reminder.</summary>
    Workout,

    /// <summary>A hydration target nudge.</summary>
    Hydration,

    /// <summary>A daily readiness check-in prompt.</summary>
    DailyCheckIn,

    /// <summary>A warning that today's training rhythm is still unprotected.</summary>
    StreakProtection
}

/// <summary>Reasons a reminder candidate was not scheduled.</summary>
public enum ReminderSuppressionReason
{
    /// <summary>Notification permission is denied or unavailable.</summary>
    PermissionDenied,

    /// <summary>The candidate would fall inside quiet hours.</summary>
    QuietHours,

    /// <summary>The daily notification cap has already been reached.</summary>
    DailyCapReached,

    /// <summary>The user has already completed the underlying action.</summary>
    AlreadyCompleted,

    /// <summary>The reminder type is disabled or not relevant today.</summary>
    NotApplicable,

    /// <summary>The candidate time has already passed for the local day.</summary>
    TimePassed
}

/// <summary>Pure policy for humane local reminder decisions.</summary>
public sealed class ReminderSchedulingPolicy
{
    private const int DefaultReminderLeadMinutes = 1;
    private readonly IReadOnlyList<ReminderKind> priorityOrder;

    /// <summary>Initializes a new instance using Forge's default reminder priority order.</summary>
    public ReminderSchedulingPolicy()
        : this([ReminderKind.Workout, ReminderKind.StreakProtection, ReminderKind.Hydration, ReminderKind.DailyCheckIn])
    {
    }

    internal ReminderSchedulingPolicy(IReadOnlyList<ReminderKind> priorityOrder)
    {
        ArgumentNullException.ThrowIfNull(priorityOrder);
        this.priorityOrder = priorityOrder;
    }

    /// <summary>Builds schedule requests from state without reading clocks, storage, or platform APIs.</summary>
    /// <param name="input">All current state needed to decide reminders.</param>
    /// <returns>Scheduled and suppressed reminder candidates.</returns>
    public IReadOnlyList<PlannedReminder> Plan(ReminderSchedulingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var planned = new List<PlannedReminder>();
        var scheduledCount = Math.Max(0, input.AlreadyScheduledCountForLocalDay);

        foreach (var candidate in BuildCandidates(input, priorityOrder))
        {
            if (candidate.SuppressionReason is not null)
            {
                planned.Add(candidate);
                continue;
            }

            if (input.PermissionState == ForgeNotificationPermissionState.Denied)
            {
                planned.Add(candidate with { SuppressionReason = ReminderSuppressionReason.PermissionDenied });
                continue;
            }

            if (IsInQuietHours(candidate.Request.DeliverAtLocal, input.Preferences.QuietHours))
            {
                planned.Add(candidate with { SuppressionReason = ReminderSuppressionReason.QuietHours });
                continue;
            }

            if (scheduledCount >= input.Preferences.DailyNotificationCap)
            {
                planned.Add(candidate with { SuppressionReason = ReminderSuppressionReason.DailyCapReached });
                continue;
            }

            scheduledCount++;
            planned.Add(candidate);
        }

        return planned;
    }

    /// <summary>Determines whether a local wall-clock instant is inside quiet hours.</summary>
    /// <param name="deliverAtLocal">The local delivery instant.</param>
    /// <param name="policy">Quiet-hours policy.</param>
    /// <returns><see langword="true" /> when the instant is quiet.</returns>
    public static bool IsInQuietHours(DateTimeOffset deliverAtLocal, QuietHoursPolicy policy)
    {
        if (!policy.Enabled)
        {
            return false;
        }

        var localTime = TimeOnly.FromDateTime(deliverAtLocal.LocalDateTime);
        return policy.Start <= policy.End
            ? localTime >= policy.Start && localTime < policy.End
            : localTime >= policy.Start || localTime < policy.End;
    }

    /// <summary>Creates a wall-clock local instant that survives DST gaps and timezone changes.</summary>
    /// <remarks>
    /// Forge stores reminder intent as a local date plus <see cref="TimeOnly" /> and resolves it
    /// through the current <see cref="TimeZoneInfo" /> each time reminders are reconciled. If a DST
    /// spring-forward gap makes that wall time invalid, the value advances to the next valid minute
    /// and is still checked against quiet hours, so a 02:30 reminder never becomes a surprise 03:00
    /// notification during the protected overnight window.
    /// </remarks>
    /// <param name="date">Local calendar date.</param>
    /// <param name="time">Local wall-clock time.</param>
    /// <param name="timeZone">Timezone used to resolve the local wall-clock value.</param>
    /// <returns>A valid local instant with the timezone's current offset.</returns>
    public static DateTimeOffset ResolveWallClock(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(DefaultReminderLeadMinutes);
        }

        var offset = timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static IEnumerable<PlannedReminder> BuildCandidates(ReminderSchedulingInput input, IReadOnlyList<ReminderKind> priorityOrder)
    {
        foreach (var kind in priorityOrder)
        {
            yield return kind switch
            {
                ReminderKind.Workout => BuildWorkoutReminder(input),
                ReminderKind.Hydration => BuildHydrationReminder(input),
                ReminderKind.DailyCheckIn => BuildCheckInReminder(input),
                ReminderKind.StreakProtection => BuildStreakReminder(input),
                _ => Suppressed(kind, input, ReminderSuppressionReason.NotApplicable)
            };
        }
    }

    private static PlannedReminder BuildWorkoutReminder(ReminderSchedulingInput input)
    {
        if (!input.Preferences.WorkoutRemindersEnabled || !input.UserState.IsTrainingDay)
        {
            return Suppressed(ReminderKind.Workout, input, ReminderSuppressionReason.NotApplicable);
        }

        var deliverAt = ResolveWallClock(input.UserState.LocalDate, input.Preferences.WorkoutReminderTime, input.LocalTimeZone);
        var reminder = Scheduled(
                ReminderKind.Workout,
                ForgeNotificationCategory.WorkoutReminder,
                $"workout:{input.UserState.LocalDate:O}",
                "Workout planned",
                input.UserState.PlannedWorkoutName is null
                    ? "Your planned training session is ready when you are."
                    : $"{input.UserState.PlannedWorkoutName} is ready when you are.",
                deliverAt);

        if (input.UserState.HasCompletedWorkoutToday)
        {
            return reminder with { SuppressionReason = ReminderSuppressionReason.AlreadyCompleted };
        }

        return deliverAt <= input.Now
            ? reminder with { SuppressionReason = ReminderSuppressionReason.TimePassed }
            : reminder;
    }

    private static PlannedReminder BuildHydrationReminder(ReminderSchedulingInput input)
    {
        if (!input.Preferences.HydrationRemindersEnabled)
        {
            return Suppressed(ReminderKind.Hydration, input, ReminderSuppressionReason.NotApplicable);
        }

        var deliverAt = ResolveWallClock(input.UserState.LocalDate, input.Preferences.HydrationNudgeTime, input.LocalTimeZone);
        var reminder = Scheduled(
                ReminderKind.Hydration,
                ForgeNotificationCategory.HydrationReminder,
                $"hydration:{input.UserState.LocalDate:O}",
                "Hydration check",
                "A small drink now can make the rest of the day easier.",
                deliverAt);

        if (input.UserState.HydrationConsumedMillilitres >= input.Preferences.HydrationTargetMillilitres)
        {
            return reminder with { SuppressionReason = ReminderSuppressionReason.AlreadyCompleted };
        }

        return deliverAt <= input.Now
            ? reminder with { SuppressionReason = ReminderSuppressionReason.TimePassed }
            : reminder;
    }

    private static PlannedReminder BuildCheckInReminder(ReminderSchedulingInput input)
    {
        if (!input.Preferences.DailyCheckInEnabled)
        {
            return Suppressed(ReminderKind.DailyCheckIn, input, ReminderSuppressionReason.NotApplicable);
        }

        var deliverAt = ResolveWallClock(input.UserState.LocalDate, input.Preferences.DailyCheckInTime, input.LocalTimeZone);
        var reminder = Scheduled(
                ReminderKind.DailyCheckIn,
                ForgeNotificationCategory.DailyCheckIn,
                $"daily-check-in:{input.UserState.LocalDate:O}",
                "Quick readiness check",
                "Log how you feel so today's plan can stay grounded.",
                deliverAt);

        if (input.UserState.HasCompletedDailyCheckIn)
        {
            return reminder with { SuppressionReason = ReminderSuppressionReason.AlreadyCompleted };
        }

        return deliverAt <= input.Now
            ? reminder with { SuppressionReason = ReminderSuppressionReason.TimePassed }
            : reminder;
    }

    private static PlannedReminder BuildStreakReminder(ReminderSchedulingInput input)
    {
        if (!input.Preferences.StreakProtectionEnabled || !input.UserState.IsTrainingDay || !input.UserState.StreakProtectionAvailable)
        {
            return Suppressed(ReminderKind.StreakProtection, input, ReminderSuppressionReason.NotApplicable);
        }

        var deliverAt = ResolveWallClock(input.UserState.LocalDate, input.Preferences.StreakWarningTime, input.LocalTimeZone);
        var reminder = Scheduled(
                ReminderKind.StreakProtection,
                ForgeNotificationCategory.Streak,
                $"streak-protection:{input.UserState.LocalDate:O}",
                "Protect your rhythm",
                "If training no longer fits today, a planned rest day is a valid choice.",
                deliverAt);

        if (input.UserState.HasCompletedWorkoutToday)
        {
            return reminder with { SuppressionReason = ReminderSuppressionReason.AlreadyCompleted };
        }

        return deliverAt <= input.Now
            ? reminder with { SuppressionReason = ReminderSuppressionReason.TimePassed }
            : reminder;
    }

    private static PlannedReminder Scheduled(
        ReminderKind kind,
        ForgeNotificationCategory category,
        string stableId,
        string title,
        string body,
        DateTimeOffset deliverAt)
        => new(new ForgeNotificationRequest(stableId, category, title, body, deliverAt), kind);

    private static PlannedReminder Suppressed(ReminderKind kind, ReminderSchedulingInput input, ReminderSuppressionReason reason)
        => new(
            new ForgeNotificationRequest(
                $"suppressed:{kind}:{input.UserState.LocalDate:O}",
                ToCategory(kind),
                string.Empty,
                string.Empty,
                input.Now),
            kind,
            reason);

    private static ForgeNotificationCategory ToCategory(ReminderKind kind)
        => kind switch
        {
            ReminderKind.Workout => ForgeNotificationCategory.WorkoutReminder,
            ReminderKind.Hydration => ForgeNotificationCategory.HydrationReminder,
            ReminderKind.DailyCheckIn => ForgeNotificationCategory.DailyCheckIn,
            ReminderKind.StreakProtection => ForgeNotificationCategory.Streak,
            _ => ForgeNotificationCategory.WorkoutReminder
        };
}
