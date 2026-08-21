namespace Forge.Core.Abstractions.Notifications;

/// <summary>Schedules and manages local notifications without leaking platform-specific types.</summary>
public interface INotificationScheduler
{
    /// <summary>Gets the current notification permission state without showing a system prompt.</summary>
    Task<ForgeNotificationPermissionState> GetPermissionStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Requests notification permission only after the user has seen clear value.</summary>
    Task<bool> RequestPermissionForDemonstratedValueAsync(
        NotificationPermissionPromptReason reason,
        CancellationToken cancellationToken = default);

    /// <summary>Schedules or replaces a local notification.</summary>
    Task<bool> ScheduleAsync(ForgeNotificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Cancels a notification by its stable application identifier.</summary>
    Task CancelAsync(string stableId, CancellationToken cancellationToken = default);

    /// <summary>Cancels all pending notifications in a user-controllable category.</summary>
    Task CancelByCategoryAsync(ForgeNotificationCategory category, CancellationToken cancellationToken = default);

    /// <summary>Returns pending notifications known to the scheduler.</summary>
    Task<IReadOnlyList<PendingForgeNotification>> GetPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Reconciles persisted schedules after events that can clear or shift platform alarms.</summary>
    Task ReschedulePersistedAsync(NotificationRescheduleReason reason, CancellationToken cancellationToken = default);
}

/// <summary>Why persisted notification schedules are being reconciled.</summary>
public enum NotificationRescheduleReason
{
    AppStart,
    DeviceReboot,
    TimeZoneChanged
}
