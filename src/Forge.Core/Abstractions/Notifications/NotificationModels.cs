namespace Forge.Core.Abstractions.Notifications;

/// <summary>Notification categories users can control independently.</summary>
public enum ForgeNotificationCategory
{
    WorkoutReminder,
    HydrationReminder,
    MealReminder,
    RestTimer,
    Achievement,
    Streak
}

/// <summary>Why Forge is asking for notification permission.</summary>
public enum NotificationPermissionPromptReason
{
    AppLaunch,
    UserEnabledReminder,
    ScheduledWorkoutCreated,
    RestTimerStarted,
    AchievementEarned
}

/// <summary>A local notification request independent from any app-head implementation.</summary>
public sealed record ForgeNotificationRequest(
    string StableId,
    ForgeNotificationCategory Category,
    string Title,
    string Body,
    DateTimeOffset DeliverAtLocal,
    string? Subtitle = null,
    TimeSpan? RepeatInterval = null);

/// <summary>A pending local notification returned by the scheduler abstraction.</summary>
public sealed record PendingForgeNotification(
    string StableId,
    ForgeNotificationCategory Category,
    string Title,
    DateTimeOffset DeliverAtLocal);

/// <summary>Quiet-hours settings used to avoid interrupting rest.</summary>
public sealed record QuietHoursPolicy(bool Enabled, TimeOnly Start, TimeOnly End);
