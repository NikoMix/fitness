using CommunityToolkit.Mvvm.ComponentModel;
using Forge.Core.Abstractions.Media;
using Forge.Core.Abstractions.Preferences;

namespace Forge.App.Features.Settings.ViewModels;

public sealed class UnitsSettingsPageViewModel(IForgePreferences preferences, IUnitFormatter formatter) : ObservableObject
{
    public IReadOnlyList<string> UnitSystemOptions { get; } = ["Metric", "Imperial"];

    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];

    public IReadOnlyList<string> VideoQualityOptions { get; } = ["Standard", "High", "Max"];

    public IReadOnlyList<string> RestTimerOptions { get; } = ["60 seconds", "90 seconds", "120 seconds", "180 seconds"];

    public IReadOnlyList<string> FirstDayOptions { get; } =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    public string SelectedUnitSystem
    {
        get => preferences.UnitSystem == MeasurementSystemPreference.Imperial ? UnitSystemOptions[1] : UnitSystemOptions[0];
        set
        {
            preferences.UnitSystem = value == UnitSystemOptions[1]
                ? MeasurementSystemPreference.Imperial
                : MeasurementSystemPreference.Metric;
            Refresh();
        }
    }

    public string SelectedTheme
    {
        get => preferences.ThemeMode.ToString();
        set
        {
            preferences.ThemeMode = Enum.TryParse<ThemeModePreference>(value, out var mode)
                ? mode
                : ThemeModePreference.System;
            Refresh();
        }
    }

    public string SelectedVideoQuality
    {
        get => preferences.PreferredVideoQuality.ToString();
        set
        {
            preferences.PreferredVideoQuality = Enum.TryParse<MediaQuality>(value, out var quality)
                ? quality
                : MediaQuality.High;
            Refresh();
        }
    }

    public bool DownloadOverUnmeteredOnly
    {
        get => preferences.DownloadMediaOverUnmeteredNetworksOnly;
        set
        {
            preferences.DownloadMediaOverUnmeteredNetworksOnly = value;
            Refresh();
        }
    }

    public string SelectedRestTimer
    {
        get => $"{(int)preferences.RestTimerDefaultDuration.TotalSeconds} seconds";
        set
        {
            var secondsText = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            preferences.RestTimerDefaultDuration = int.TryParse(secondsText, out var seconds)
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.FromSeconds(120);
            Refresh();
        }
    }

    public bool HapticFeedbackEnabled
    {
        get => preferences.HapticFeedbackEnabled;
        set
        {
            preferences.HapticFeedbackEnabled = value;
            Refresh();
        }
    }

    public string SelectedFirstDay
    {
        get => preferences.FirstDayOfWeek.ToString();
        set
        {
            preferences.FirstDayOfWeek = Enum.TryParse<DayOfWeek>(value, out var day) ? day : DayOfWeek.Monday;
            Refresh();
        }
    }

    public string PreviewMass => formatter.FormatMass(82.5);

    public string PreviewLength => formatter.FormatLength(180);

    public string PreviewVolume => formatter.FormatVolume(750);

    public string PreviewEnergy => formatter.FormatEnergy(2200);

    public string PreviewWeek => formatter.FormatFirstDayOfWeek();

    public string VideoPreferenceSummary => $"{preferences.PreferredVideoQuality} quality · "
        + (preferences.DownloadMediaOverUnmeteredNetworksOnly ? "unmetered networks only" : "metered networks allowed");

    public string RestTimerSummary => $"New rest timers default to {(int)preferences.RestTimerDefaultDuration.TotalSeconds} seconds.";

    private void Refresh()
    {
        OnPropertyChanged(nameof(SelectedUnitSystem));
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(SelectedVideoQuality));
        OnPropertyChanged(nameof(DownloadOverUnmeteredOnly));
        OnPropertyChanged(nameof(SelectedRestTimer));
        OnPropertyChanged(nameof(HapticFeedbackEnabled));
        OnPropertyChanged(nameof(SelectedFirstDay));
        OnPropertyChanged(nameof(PreviewMass));
        OnPropertyChanged(nameof(PreviewLength));
        OnPropertyChanged(nameof(PreviewVolume));
        OnPropertyChanged(nameof(PreviewEnergy));
        OnPropertyChanged(nameof(PreviewWeek));
        OnPropertyChanged(nameof(VideoPreferenceSummary));
        OnPropertyChanged(nameof(RestTimerSummary));
    }
}
