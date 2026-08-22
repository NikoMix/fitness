using System.Globalization;
using Forge.App.Features.Profile;
using Forge.Core.Abstractions.Data;
using Forge.Core.Abstractions.Notifications;
using Forge.Domain.Engagement;
using Forge.Domain.Nutrition;
using Forge.Domain.Planning;
using Forge.Domain.Profile;
using Forge.Domain.Recovery;
using Forge.Domain.Training;
using Microsoft.Maui.Storage;

namespace Forge.App.Services.Notifications;

/// <summary>Refreshes local reminder schedules from the local Forge database.</summary>
public interface IReminderRefreshService
{
    /// <summary>Rebuilds useful reminder notifications from current local user data.</summary>
    /// <param name="now">The current instant supplied by the caller for testability.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PlannedReminder>> RefreshAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>
/// Data-backed implementation of Forge reminder refresh.
/// </summary>
/// <remarks>
/// Reminders are scoped to the active profile. Unscoped, a notification told one person they had
/// already trained today because somebody else on the device had, which is worse than a missing
/// reminder: it is the app asserting something about the reader's own day that is not true.
/// </remarks>
public sealed class ReminderRefreshService(
    IDataSessionFactory sessions,
    INotificationScheduler notifications,
    ReminderSchedulingPolicy policy,
    ProfileStore profiles) : IReminderRefreshService
{
    private const string SettingsPrefix = "forge.notifications.";
    private const int DefaultDailyCap = LocalNotificationScheduler.MaxNonCriticalNotificationsPerLocalDay;
    private const int DefaultHydrationTargetMillilitres = 2000;
    private const int DefaultWorkoutReminderHour = 18;
    private const int DefaultHydrationReminderHour = 11;
    private const int DefaultCheckInReminderHour = 8;
    private const int DefaultStreakReminderHour = 20;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlannedReminder>> RefreshAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var timeZone = TimeZoneInfo.Local;
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        var preferences = ReadPreferences();
        var scope = await profiles.GetActiveScopeAsync(cancellationToken);

        await using var session = sessions.Create();
        var activePlan = (await session.Repository<TrainingPlan>().ListAsync(cancellationToken))
            .OwnedBy(scope)
            .FirstOrDefault(plan => plan.IsActive);
        var workouts = (await session.Repository<WorkoutSession>().ListAsync(cancellationToken)).OwnedBy(scope).ToList();
        var hydration = (await session.Repository<HydrationEntry>().ListAsync(cancellationToken)).OwnedBy(scope).ToList();
        var checkIns = (await session.Repository<MorningCheckIn>().ListAsync(cancellationToken)).OwnedBy(scope).ToList();

        // Streaks are still read unscoped. Streak already carries a UserProfileId but does not
        // implement IProfileOwned yet, so there is nothing to filter on; one profile's streak
        // therefore still drives everybody's streak-protection reminder. See phase 1 of
        // docs/design/multi-profile.md.
        var streaks = await session.Repository<Streak>().ListAsync(cancellationToken);
        var streak = streaks.Count > 0 ? streaks[0] : null;

        var completedWorkoutToday = workouts.Any(workout =>
            workout.CompletedUtc is not null && ToLocalDate(workout.CompletedUtc.Value, timeZone) == localDate);
        var hydrationToday = hydration
            .Where(entry => ToLocalDate(entry.ConsumedUtc, timeZone) == localDate)
            .Sum(entry => entry.Volume.Millilitres);
        var checkInToday = checkIns.Exists(checkIn => checkIn.Date == localDate);
        var scheduledSession = FindTodaySession(activePlan, localDate);

        var pending = await notifications.GetPendingAsync(cancellationToken);
        var alreadyScheduled = pending.Count(item =>
            item.Category != ForgeNotificationCategory.RestTimer && ToLocalDate(item.DeliverAtLocal, timeZone) == localDate);
        var permission = await notifications.GetPermissionStateAsync(cancellationToken);

        var decisions = policy.Plan(new ReminderSchedulingInput(
            now,
            timeZone,
            permission,
            preferences,
            new ReminderUserSnapshot(
                localDate,
                scheduledSession is not null,
                scheduledSession?.Day.Name,
                completedWorkoutToday,
                hydrationToday,
                checkInToday,
                streak?.GamificationEnabled == true && streak.FreezesRemaining > 0),
            alreadyScheduled));

        foreach (var decision in decisions)
        {
            if (decision.SuppressionReason is null)
            {
                await notifications.ScheduleAsync(decision.Request, cancellationToken);
            }
            else if (decision.SuppressionReason == ReminderSuppressionReason.AlreadyCompleted)
            {
                await notifications.CancelAsync(decision.Request.StableId, cancellationToken);
            }
        }

        return decisions;
    }

    private static ReminderPreferences ReadPreferences()
        => new(
            Preferences.Default.Get(SettingsPrefix + "WorkoutRemindersEnabled", true),
            Preferences.Default.Get(SettingsPrefix + "HydrationRemindersEnabled", true),
            Preferences.Default.Get(SettingsPrefix + "DailyCheckInEnabled", true),
            Preferences.Default.Get(SettingsPrefix + "StreakProtectionEnabled", true),
            ReadQuietHours(),
            Math.Max(1, Preferences.Default.Get(SettingsPrefix + "DailyCap", DefaultDailyCap)),
            ReadTime("WorkoutReminderTime", new TimeOnly(DefaultWorkoutReminderHour, 0)),
            ReadTime("HydrationNudgeTime", new TimeOnly(DefaultHydrationReminderHour, 0)),
            ReadTime("DailyCheckInTime", new TimeOnly(DefaultCheckInReminderHour, 0)),
            ReadTime("StreakWarningTime", new TimeOnly(DefaultStreakReminderHour, 0)),
            Math.Max(1, Preferences.Default.Get(SettingsPrefix + "HydrationTargetMillilitres", DefaultHydrationTargetMillilitres)));

    private static QuietHoursPolicy ReadQuietHours()
        => new(
            Preferences.Default.Get(SettingsPrefix + "QuietHoursEnabled", true),
            ReadTime("QuietHoursStart", new TimeOnly(22, 0)),
            ReadTime("QuietHoursEnd", new TimeOnly(7, 0)));

    private static TimeOnly ReadTime(string key, TimeOnly fallback)
        => TimeOnly.TryParse(Preferences.Default.Get(SettingsPrefix + key, fallback.ToString("HH:mm", CultureInfo.InvariantCulture)), out var parsed)
            ? parsed
            : fallback;

    private static ScheduledPlanSession? FindTodaySession(TrainingPlan? activePlan, DateOnly localDate)
    {
        if (activePlan is null)
        {
            return null;
        }

        var weekStart = StartOfWeek(localDate, DayOfWeek.Monday);
        return PlanScheduler.Schedule(activePlan, weekStart, 1).FirstOrDefault(session => session.Date == localDate);
    }

    private static DateOnly StartOfWeek(DateOnly date, DayOfWeek firstDay)
    {
        var offset = ((int)date.DayOfWeek - (int)firstDay + 7) % 7;
        return date.AddDays(-offset);
    }

    private static DateOnly ToLocalDate(DateTimeOffset instant, TimeZoneInfo timeZone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);
}
