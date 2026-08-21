using Forge.Domain.Workout;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace Forge.App.Features.Workout;

public interface IRestNotificationScheduler
{
    Task ScheduleAsync(RestTimer timer, CancellationToken cancellationToken);

    Task CancelAsync(int notificationId, CancellationToken cancellationToken);
}

internal sealed class RestNotificationScheduler : IRestNotificationScheduler
{
    public Task ScheduleAsync(RestTimer timer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timer);
        cancellationToken.ThrowIfCancellationRequested();

        // Rest completion is user-helpful but not safety-critical. Android 14+ heavily restricts
        // exact alarms and Play Console review expects a narrow justification; iOS also limits
        // background execution and may coalesce delivery. We therefore schedule a normal local
        // notification for the wall-clock end time and reconcile the UI from TargetEndUtc on resume
        // instead of depending on foreground services, background timers, or exact alarms.
        var request = new NotificationRequest
        {
            NotificationId = timer.NotificationId,
            Title = "Rest complete",
            Description = "Your next set is ready.",
            ReturningData = "workout-rest-complete",
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = timer.TargetEndUtc.LocalDateTime
            }
        };

        LocalNotificationCenter.Current.Show(request);
        return Task.CompletedTask;
    }

    public Task CancelAsync(int notificationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LocalNotificationCenter.Current.Cancel(notificationId);
        return Task.CompletedTask;
    }
}
