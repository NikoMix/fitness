using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Services.Notifications;
using Forge.Core.Abstractions.Notifications;
using Forge.Domain.Engagement;

namespace Forge.App.Features.Engagement.ViewModels;

public sealed partial class StreaksPageViewModel : ObservableObject
{
    private readonly INotificationScheduler? notifications;
    private readonly IReminderRefreshService? reminders;

    public StreaksPageViewModel(
        INotificationScheduler? notifications = null,
        IReminderRefreshService? reminders = null)
    {
        this.notifications = notifications;
        this.reminders = reminders;

        CurrentStreakDays = 5;
        BestStreakDays = 12;
        FreezesRemaining = 2;
        FreezesRemainingProgress = 2.0 / 3.0;
        EncouragingMessage = EngagementEthicsPolicy.SupportiveStreakBreakMessage;
        ReminderPermissionMessage = "Reminders are local, capped, and paused during quiet hours.";
        ReminderRefreshStatus = "Workout, hydration, check-in, and streak reminders use your local logs.";
        History =
        [
            new StreakHistoryRow("Today", "Training planned", "Keeps your rhythm moving."),
            new StreakHistoryRow("Yesterday", "Rest day", "Protected: rest is part of the plan."),
            new StreakHistoryRow("Monday", "Workout logged", "Three sets completed.")
        ];
    }

    [ObservableProperty]
    private int currentStreakDays;

    [ObservableProperty]
    private int bestStreakDays;

    [ObservableProperty]
    private int freezesRemaining;

    [ObservableProperty]
    private double freezesRemainingProgress;

    [ObservableProperty]
    private string encouragingMessage;

    [ObservableProperty]
    private string reminderPermissionMessage;

    [ObservableProperty]
    private string reminderRefreshStatus;

    public ObservableCollection<StreakHistoryRow> History { get; }

    [RelayCommand]
    private async Task EnableRespectfulRemindersAsync(CancellationToken cancellationToken)
    {
        if (notifications is null || reminders is null)
        {
            ReminderRefreshStatus = "Reminder services are not available in this build.";
            return;
        }

        var state = await notifications.GetPermissionStateAsync(cancellationToken);
        if (state == ForgeNotificationPermissionState.Denied)
        {
            ReminderPermissionMessage = "Notifications are off. You can re-enable them in system settings if reminders would help.";
            return;
        }

        if (state == ForgeNotificationPermissionState.Unknown)
        {
            var allowed = await notifications.RequestPermissionForDemonstratedValueAsync(
                NotificationPermissionPromptReason.UserEnabledReminder,
                cancellationToken);
            if (!allowed)
            {
                ReminderPermissionMessage = "Notifications are off. Forge will not ask again unless you choose reminders later.";
                return;
            }
        }

        var decisions = await reminders.RefreshAsync(DateTimeOffset.Now, cancellationToken);
        var scheduled = decisions.Count(decision => decision.SuppressionReason is null);
        ReminderPermissionMessage = "Reminders are enabled and stay local to this device.";
        ReminderRefreshStatus = scheduled == 0
            ? "Nothing new was scheduled because today's actions are complete, quiet, capped, or not planned."
            : $"{scheduled} respectful reminders scheduled for today.";
    }
}

public sealed record StreakHistoryRow(string Date, string Title, string Detail);
