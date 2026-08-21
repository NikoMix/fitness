using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;
using Forge.Core.Abstractions.Notifications;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using Plugin.LocalNotification.Core.Models.AndroidOption;

#if ANDROID
using Android.App;
using Android.Content;
using Android;

[assembly: UsesPermission(Manifest.Permission.ReceiveBootCompleted)]
[assembly: UsesPermission(Manifest.Permission.PostNotifications)]
#endif

namespace Forge.App.Services.Notifications;

public sealed class LocalNotificationScheduler : INotificationScheduler
{
    private const string StoreKey = "forge.notifications.scheduled.v1";
    private const string CapPrefix = "forge.notifications.frequency.";
    private const string SettingsPrefix = "forge.notifications.";
    private const string PermissionDeniedKey = SettingsPrefix + "PermissionDenied";
    private const string PermissionPromptedKey = SettingsPrefix + "PermissionPrompted";

    /// <summary>Four non-urgent reminders a day is enough to help without feeling like surveillance.</summary>
    public const int MaxNonCriticalNotificationsPerLocalDay = 4;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ForgeNotificationPermissionState> GetPermissionStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var permission = new NotificationPermission { AskPermission = false };
        var enabled = await MainThread.InvokeOnMainThreadAsync(
            () => LocalNotificationCenter.Current.AreNotificationsEnabled(permission));

        if (enabled)
        {
            Preferences.Default.Set(PermissionDeniedKey, false);
            return ForgeNotificationPermissionState.Authorized;
        }

        return Preferences.Default.Get(PermissionDeniedKey, false) || Preferences.Default.Get(PermissionPromptedKey, false)
            ? ForgeNotificationPermissionState.Denied
            : ForgeNotificationPermissionState.Unknown;
    }

    public async Task<bool> RequestPermissionForDemonstratedValueAsync(
        NotificationPermissionPromptReason reason,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (reason == NotificationPermissionPromptReason.AppLaunch)
        {
            return false;
        }

        var permission = new NotificationPermission { AskPermission = true };

        // iOS offers a single system prompt and Android 13+ requires a runtime prompt. Keep this
        // behind explicit demonstrated-value moments so first launch never spends that chance.
        Preferences.Default.Set(PermissionPromptedKey, true);
        var allowed = await MainThread.InvokeOnMainThreadAsync(
            () => LocalNotificationCenter.Current.RequestNotificationPermission(permission));
        Preferences.Default.Set(PermissionDeniedKey, !allowed);
        return allowed;
    }

    public async Task<bool> ScheduleAsync(ForgeNotificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (await GetPermissionStateAsync(cancellationToken) == ForgeNotificationPermissionState.Denied)
        {
            return false;
        }

        var stored = LoadStore().Where(item => item.StableId != request.StableId).ToList();
        if (IsSuppressedByQuietHours(request))
        {
            SaveStore(stored);
            return false;
        }

        var normalized = request;

        if (!CanScheduleMore(normalized))
        {
            stored.Add(StoredNotification.From(normalized, notificationId: ToNotificationId(normalized.StableId), suppressedByFrequencyCap: true));
            SaveStore(stored);
            return false;
        }

        var storedNotification = StoredNotification.From(normalized, notificationId: ToNotificationId(normalized.StableId));
        stored.Add(storedNotification);
        SaveStore(stored);

        return await ShowAsync(storedNotification, cancellationToken);
    }

    public Task CancelAsync(string stableId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        cancellationToken.ThrowIfCancellationRequested();

        var stored = LoadStore();
        var match = stored.FirstOrDefault(item => item.StableId == stableId);
        if (match is not null)
        {
            LocalNotificationCenter.Current.Cancel(match.NotificationId);
            SaveStore(stored.Where(item => item.StableId != stableId));
        }

        return Task.CompletedTask;
    }

    public Task CancelByCategoryAsync(ForgeNotificationCategory category, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stored = LoadStore();
        foreach (var item in stored.Where(item => item.Category == category))
        {
            LocalNotificationCenter.Current.Cancel(item.NotificationId);
        }

        SaveStore(stored.Where(item => item.Category != category));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PendingForgeNotification>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<PendingForgeNotification> pending = LoadStore()
            .Where(item => !item.SuppressedByFrequencyCap && item.DeliverAtLocal >= DateTimeOffset.Now)
            .OrderBy(item => item.DeliverAtLocal)
            .Select(item => new PendingForgeNotification(item.StableId, item.Category, item.Title, item.DeliverAtLocal))
            .ToList();

        return Task.FromResult(pending);
    }

    public async Task ReschedulePersistedAsync(NotificationRescheduleReason reason, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Reboot clears Android AlarmManager schedules, and timezone changes shift wall-clock
        // intent. Persisting Forge's intended local delivery time lets us rebuild plugin alarms.
        var stored = LoadStore()
            .Where(item => !item.SuppressedByFrequencyCap && item.DeliverAtLocal >= DateTimeOffset.Now)
            .Select(item => reason == NotificationRescheduleReason.TimeZoneChanged ? item.WithLocalOffset(DateTimeOffset.Now.Offset) : item)
            .ToList();

        foreach (var item in stored)
        {
            await ShowAsync(item, cancellationToken);
        }

        SaveStore(stored);
    }

    internal static Task ReschedulePersistedNotificationsAsync(NotificationRescheduleReason reason)
        => new LocalNotificationScheduler().ReschedulePersistedAsync(reason);

    private static async Task<bool> ShowAsync(StoredNotification item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new NotificationRequest
        {
            NotificationId = item.NotificationId,
            CategoryType = ToPluginCategory(item.Category),
            Group = item.Category.ToString(),
            Title = item.Title,
            Subtitle = item.Subtitle ?? string.Empty,
            Description = item.Body,
            ReturningData = JsonSerializer.Serialize(new NotificationReturnData(item.StableId, item.Category), JsonOptions),
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = item.DeliverAtLocal,
                RepeatType = item.RepeatInterval is null ? NotificationRepeat.No : NotificationRepeat.TimeInterval,
                NotifyRepeatInterval = item.RepeatInterval,
                Android = new AndroidScheduleOptions
                {
                    ScheduleMode = AndroidScheduleMode.InexactAllowWhileIdle
                }
            },
            Android = new AndroidOptions
            {
                ChannelId = ToChannelId(item.Category),
                Tag = item.Category.ToString(),
                AutoCancel = true
            }
        };

        return await LocalNotificationCenter.Current.Show(request);
    }

    private static bool IsSuppressedByQuietHours(ForgeNotificationRequest request)
    {
        if (request.Category == ForgeNotificationCategory.RestTimer)
        {
            return false;
        }

        var policy = ReadQuietHours();
        return ReminderSchedulingPolicy.IsInQuietHours(request.DeliverAtLocal, policy);
    }

    private static QuietHoursPolicy ReadQuietHours()
    {
        var enabled = Preferences.Default.Get(SettingsPrefix + "QuietHoursEnabled", true);
        var start = ParseTime(Preferences.Default.Get(SettingsPrefix + "QuietHoursStart", "22:00"), new TimeOnly(22, 0));
        var end = ParseTime(Preferences.Default.Get(SettingsPrefix + "QuietHoursEnd", "07:00"), new TimeOnly(7, 0));
        return new QuietHoursPolicy(enabled, start, end);
    }

    private static TimeOnly ParseTime(string value, TimeOnly fallback)
        => TimeOnly.TryParse(value, out var parsed) ? parsed : fallback;

    private static bool CanScheduleMore(ForgeNotificationRequest request)
    {
        if (request.Category == ForgeNotificationCategory.RestTimer)
        {
            return true;
        }

        var key = CapPrefix + DateOnly.FromDateTime(request.DeliverAtLocal.LocalDateTime).ToString("O", CultureInfo.InvariantCulture);
        var current = Preferences.Default.Get(key, 0);
        if (current >= MaxNonCriticalNotificationsPerLocalDay)
        {
            return false;
        }

        Preferences.Default.Set(key, current + 1);
        return true;
    }

    private static List<StoredNotification> LoadStore()
    {
        var json = Preferences.Default.Get(StoreKey, string.Empty);
        return string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<StoredNotification>>(json, JsonOptions) ?? [];
    }

    private static void SaveStore(IEnumerable<StoredNotification> notifications)
        => Preferences.Default.Set(StoreKey, JsonSerializer.Serialize(notifications.ToList(), JsonOptions));

    private static int ToNotificationId(string stableId)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stableId));
        return Math.Abs(BitConverter.ToInt32(hash, 0));
    }

    private static string ToChannelId(ForgeNotificationCategory category) => $"forge.notifications.{category}";

    private static NotificationCategoryType ToPluginCategory(ForgeNotificationCategory category)
        => category switch
        {
            ForgeNotificationCategory.RestTimer => NotificationCategoryType.Alarm,
            ForgeNotificationCategory.Achievement => NotificationCategoryType.Status,
            _ => NotificationCategoryType.Reminder
        };

    private sealed record NotificationReturnData(string StableId, ForgeNotificationCategory Category);

    private sealed record StoredNotification(
        string StableId,
        int NotificationId,
        ForgeNotificationCategory Category,
        string Title,
        string Body,
        DateTimeOffset DeliverAtLocal,
        string? Subtitle,
        TimeSpan? RepeatInterval,
        bool SuppressedByFrequencyCap)
    {
        public static StoredNotification From(ForgeNotificationRequest request, int notificationId, bool suppressedByFrequencyCap = false)
            => new(request.StableId, notificationId, request.Category, request.Title, request.Body, request.DeliverAtLocal, request.Subtitle, request.RepeatInterval, suppressedByFrequencyCap);

        public StoredNotification WithLocalOffset(TimeSpan offset)
            => this with { DeliverAtLocal = new DateTimeOffset(DeliverAtLocal.DateTime, offset) };
    }
}

#if ANDROID
[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter([BootCompleted, TimeZoneChanged])]
public sealed class NotificationRescheduleReceiver : BroadcastReceiver
{
    private const string BootCompleted = "android.intent.action.BOOT_COMPLETED";
    private const string TimeZoneChanged = "android.intent.action.TIMEZONE_CHANGED";

    public override void OnReceive(Context? context, Intent? intent)
    {
        var reason = intent?.Action == TimeZoneChanged
            ? NotificationRescheduleReason.TimeZoneChanged
            : NotificationRescheduleReason.DeviceReboot;

        _ = Task.Run(() => LocalNotificationScheduler.ReschedulePersistedNotificationsAsync(reason));
    }
}
#endif
