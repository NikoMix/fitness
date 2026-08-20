using CommunityToolkit.Mvvm.ComponentModel;

namespace Forge.App.Features.Settings.ViewModels;

public sealed partial class NotificationSettingsPageViewModel : ObservableObject
{
    private const string Prefix = "forge.notifications.";

    public NotificationSettingsPageViewModel()
    {
        workoutRemindersEnabled = Preferences.Default.Get(Prefix + nameof(WorkoutRemindersEnabled), true);
        mealRemindersEnabled = Preferences.Default.Get(Prefix + nameof(MealRemindersEnabled), false);
        hydrationRemindersEnabled = Preferences.Default.Get(Prefix + nameof(HydrationRemindersEnabled), true);
        quietHoursEnabled = Preferences.Default.Get(Prefix + nameof(QuietHoursEnabled), true);
        quietHoursStart = Preferences.Default.Get(Prefix + nameof(QuietHoursStart), "22:00");
        quietHoursEnd = Preferences.Default.Get(Prefix + nameof(QuietHoursEnd), "07:00");
    }

    [ObservableProperty]
    private bool workoutRemindersEnabled;

    [ObservableProperty]
    private bool mealRemindersEnabled;

    [ObservableProperty]
    private bool hydrationRemindersEnabled;

    [ObservableProperty]
    private bool quietHoursEnabled;

    [ObservableProperty]
    private string quietHoursStart;

    [ObservableProperty]
    private string quietHoursEnd;

    partial void OnWorkoutRemindersEnabledChanged(bool value) => Preferences.Default.Set(Prefix + nameof(WorkoutRemindersEnabled), value);

    partial void OnMealRemindersEnabledChanged(bool value) => Preferences.Default.Set(Prefix + nameof(MealRemindersEnabled), value);

    partial void OnHydrationRemindersEnabledChanged(bool value) => Preferences.Default.Set(Prefix + nameof(HydrationRemindersEnabled), value);

    partial void OnQuietHoursEnabledChanged(bool value) => Preferences.Default.Set(Prefix + nameof(QuietHoursEnabled), value);

    partial void OnQuietHoursStartChanged(string value) => Preferences.Default.Set(Prefix + nameof(QuietHoursStart), value);

    partial void OnQuietHoursEndChanged(string value) => Preferences.Default.Set(Prefix + nameof(QuietHoursEnd), value);
}
