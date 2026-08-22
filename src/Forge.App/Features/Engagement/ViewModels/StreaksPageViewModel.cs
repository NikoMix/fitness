using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Engagement.Services;
using Forge.App.Services.Notifications;
using Forge.Core.Abstractions.Notifications;
using Forge.Domain.Engagement;

namespace Forge.App.Features.Engagement.ViewModels;

/// <summary>One week as the history list shows it.</summary>
/// <param name="Label">Heading for the row.</param>
/// <param name="Detail">What actually happened that week.</param>
/// <param name="Status">Short status word, or empty when the week was ordinary.</param>
/// <param name="IsProtected">Whether any day in the week was covered by a protected period.</param>
/// <param name="Description">The whole row as one sentence, for a screen reader.</param>
public sealed record StreakHistoryRow(string Label, string Detail, string Status, bool IsProtected, string Description);

/// <summary>
/// The Consistency screen: training measured in weeks, derived entirely from logged sessions.
/// </summary>
/// <remarks>
/// <para>
/// Every number on this screen comes from <see cref="IEngagementDataService"/>, which reads the
/// active profile's own workouts. Nothing is stored as a counter and nothing is defaulted, so a
/// profile with no history renders an empty state rather than a plausible-looking zero.
/// </para>
/// <para>
/// There is no daily figure here and no countdown of any kind. See
/// <c>docs/design/engagement-ethics.md</c>.
/// </para>
/// </remarks>
public sealed partial class StreaksPageViewModel : ObservableObject
{
    private readonly IEngagementDataService engagement;
    private readonly INotificationScheduler? notifications;
    private readonly IReminderRefreshService? reminders;

    /// <summary>Creates the view model.</summary>
    /// <param name="engagement">The engagement data service.</param>
    /// <param name="notifications">Local notification scheduling, when available on this platform.</param>
    /// <param name="reminders">Reminder refresh, when available on this platform.</param>
    /// <exception cref="ArgumentNullException"><paramref name="engagement"/> is <see langword="null"/>.</exception>
    public StreaksPageViewModel(
        IEngagementDataService engagement,
        INotificationScheduler? notifications = null,
        IReminderRefreshService? reminders = null)
    {
        ArgumentNullException.ThrowIfNull(engagement);

        this.engagement = engagement;
        this.notifications = notifications;
        this.reminders = reminders;

        History = [];
        headline = string.Empty;
        detail = string.Empty;
        restAssurance = EngagementEthicsPolicy.RestIsTrainingMessage;
        protectionSummary = string.Empty;
        weekProgressDetail = string.Empty;
        activeWeeksCaption = string.Empty;
        reminderPermissionMessage = "Reminders are local, capped, and paused during quiet hours.";
        reminderRefreshStatus = "Workout, hydration and check-in reminders are built from your own logs.";
        gamificationNote = EngagementEthicsPolicy.GamificationDisablementMessage;
    }

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasProfile;

    [ObservableProperty]
    private bool hasHistory;

    [ObservableProperty]
    private bool hasNoHistory = true;

    [ObservableProperty]
    private bool gamificationEnabled = true;

    [ObservableProperty]
    private bool gamificationDisabled;

    [ObservableProperty]
    private string gamificationNote;

    [ObservableProperty]
    private int activeWeeks;

    [ObservableProperty]
    private int bestActiveWeeks;

    [ObservableProperty]
    private string activeWeeksText = string.Empty;

    [ObservableProperty]
    private string bestActiveWeeksText = string.Empty;

    [ObservableProperty]
    private string activeWeeksCaption;

    [ObservableProperty]
    private bool hasWeeklyTarget;

    [ObservableProperty]
    private bool hasNoWeeklyTarget = true;

    [ObservableProperty]
    private double weekProgress;

    [ObservableProperty]
    private string weekProgressText = string.Empty;

    [ObservableProperty]
    private string weekProgressDetail;

    [ObservableProperty]
    private string headline;

    [ObservableProperty]
    private string detail;

    [ObservableProperty]
    private string restAssurance;

    [ObservableProperty]
    private bool isProtected;

    [ObservableProperty]
    private bool isNotProtected = true;

    [ObservableProperty]
    private string protectionSummary;

    [ObservableProperty]
    private string reminderPermissionMessage;

    [ObservableProperty]
    private string reminderRefreshStatus;

    /// <summary>The recent weeks, newest first.</summary>
    public ObservableCollection<StreakHistoryRow> History { get; }

    /// <summary>Loads the screen from the active profile's own data.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the screen is populated.</returns>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            Apply(await engagement.RefreshAsync(Today(), cancellationToken));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ToggleGamificationAsync(CancellationToken cancellationToken)
        => Apply(await engagement.SetGamificationEnabledAsync(!GamificationEnabled, Today(), cancellationToken));

    /// <summary>
    /// Marks today onwards as protected, so the weeks it covers are not read as missed training.
    /// </summary>
    /// <remarks>
    /// Takes the reason as a string because the page passes a literal <c>CommandParameter</c>. An
    /// unrecognised value does nothing rather than guessing a reason on the user's behalf.
    /// </remarks>
    /// <param name="reason">One of the <see cref="TrainingInterruption"/> names.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the change is saved.</returns>
    [RelayCommand]
    private async Task MarkProtectedAsync(string? reason, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<TrainingInterruption>(reason, ignoreCase: true, out var interruption))
        {
            return;
        }

        var today = Today();
        Apply(await engagement.ProtectFromAsync(interruption, today, today, cancellationToken));
    }

    [RelayCommand]
    private async Task EndProtectionAsync(CancellationToken cancellationToken)
        => Apply(await engagement.EndProtectionAsync(Today(), cancellationToken));

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
            : $"{scheduled} reminders scheduled for today.";
    }

    private void Apply(EngagementSnapshot snapshot)
    {
        var rhythm = snapshot.Rhythm;

        HasProfile = snapshot.HasProfile;
        GamificationEnabled = snapshot.GamificationEnabled;
        GamificationDisabled = !snapshot.GamificationEnabled;
        HasHistory = snapshot.HasProfile && rhythm.HasHistory;
        HasNoHistory = !HasHistory;

        ActiveWeeks = rhythm.ActiveWeeks;
        BestActiveWeeks = rhythm.BestActiveWeeks;
        ActiveWeeksText = rhythm.ActiveWeeks.ToString(CultureInfo.CurrentCulture);
        BestActiveWeeksText = rhythm.BestActiveWeeks.ToString(CultureInfo.CurrentCulture);
        ActiveWeeksCaption = rhythm.ProtectedWeeks == 0
            ? "Weeks in a row containing training"
            : $"Weeks in a row containing training. {Count(rhythm.ProtectedWeeks, "protected week", "protected weeks")} stepped over rather than counted against you.";

        HasWeeklyTarget = rhythm.HasWeeklyTarget;
        HasNoWeeklyTarget = !rhythm.HasWeeklyTarget;
        WeekProgress = rhythm.WeekProgress;
        WeekProgressText = rhythm.HasWeeklyTarget
            ? string.Create(CultureInfo.CurrentCulture, $"{rhythm.CurrentWeekSessions}/{rhythm.WeeklyTarget}")
            : string.Empty;
        WeekProgressDetail = rhythm.HasWeeklyTarget
            ? $"{Count(rhythm.CurrentWeekSessions, "session", "sessions")} of your own target of {rhythm.WeeklyTarget} this week. This week is still open."
            : "No plan is active, so there is no weekly target to measure against. Forge will count your sessions but will not invent a target.";

        Headline = rhythm.Headline;
        Detail = rhythm.Detail;
        RestAssurance = rhythm.RestAssurance;

        IsProtected = rhythm.ProtectionToday is not null;
        IsNotProtected = !IsProtected;
        ProtectionSummary = rhythm.ProtectionToday is { } protection
            ? $"Protected since {protection.Start.ToString("d MMM", CultureInfo.CurrentCulture)} for {protection.ReasonLabel}."
            : string.Empty;

        History.Clear();
        foreach (var week in rhythm.Weeks)
        {
            History.Add(new StreakHistoryRow(
                week.Label,
                week.Detail,
                week.WasProtected ? "Protected" : string.Empty,
                week.WasProtected,
                $"{week.Label}. {week.Detail}"));
        }
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    private static string Count(int value, string singular, string plural)
        => string.Create(CultureInfo.CurrentCulture, $"{value} {(value == 1 ? singular : plural)}");
}
