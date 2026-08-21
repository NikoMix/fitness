using CommunityToolkit.Mvvm.ComponentModel;
using Forge.Core.Abstractions.Localization;
using Forge.Core.Abstractions.Preferences;

namespace Forge.App.Features.Settings.Localization;

/// <summary>View model for the display-language picker.</summary>
/// <remarks>
/// <para>
/// Static labels on this page are translated by <c>{loc:Translate}</c> in XAML, which rebinds
/// itself when the language changes. The properties here are the ones XAML cannot express:
/// composite strings with an argument, and values whose formatting depends on the culture.
/// </para>
/// <para>
/// The unit system is shown but not editable. It belongs to the units screen, and it is on this
/// page purely to make the separation visible: switching to German leaves the measurement
/// system exactly where the user put it.
/// </para>
/// </remarks>
public sealed class LanguageSettingsPageViewModel : ObservableObject
{
    private readonly ILocalizationService localization;
    private readonly ILocalizedValueFormatter formatter;
    private readonly IForgePreferences preferences;
    private bool subscribed;

    /// <summary>Creates the view model.</summary>
    /// <param name="localization">Resolves and changes the display language.</param>
    /// <param name="formatter">Formats the preview values.</param>
    /// <param name="preferences">Supplies the measurement system, which language must not touch.</param>
    public LanguageSettingsPageViewModel(
        ILocalizationService localization,
        ILocalizedValueFormatter formatter,
        IForgePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(formatter);
        ArgumentNullException.ThrowIfNull(preferences);

        this.localization = localization;
        this.formatter = formatter;
        this.preferences = preferences;

        // Native names, so every language reads as itself whatever the current language is.
        Languages = [.. localization.SupportedLanguages.Select(language => language.NativeName)];
    }

    /// <summary>The shipped languages, each named in its own language.</summary>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>Whether Forge follows the device language rather than an explicit choice.</summary>
    public bool FollowSystemLanguage
    {
        get => localization.FollowsSystemLanguage;
        set
        {
            if (value == localization.FollowsSystemLanguage)
            {
                return;
            }

            if (value)
            {
                localization.UseSystemLanguage();
            }
            else
            {
                localization.UseLanguage(localization.CurrentLanguage.Code);
            }

            Refresh();
        }
    }

    /// <summary>Whether the explicit language picker is available.</summary>
    public bool CanChooseLanguage => !localization.FollowsSystemLanguage;

    /// <summary>The chosen language, by native name.</summary>
    public string SelectedLanguageName
    {
        get => localization.CurrentLanguage.NativeName;
        set
        {
            var match = localization.SupportedLanguages
                .FirstOrDefault(language => string.Equals(language.NativeName, value, StringComparison.Ordinal));

            if (match is null || string.Equals(match.Code, localization.SelectedLanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            localization.UseLanguage(match.Code);
            Refresh();
        }
    }

    /// <summary>States which language Forge is currently displayed in.</summary>
    public string CurrentLanguageSummary =>
        localization.GetString(ForgeStringKeys.LanguageSettingsCurrent, localization.CurrentLanguage.NativeName);

    /// <summary>The active measurement system, translated.</summary>
    public string UnitSystemName => localization.GetString(
        preferences.UnitSystem == MeasurementSystemPreference.Imperial
            ? ForgeStringKeys.CommonImperial
            : ForgeStringKeys.CommonMetric);

    /// <summary>Explains that units are a separate setting from language.</summary>
    public string UnitsNote => localization.GetString(ForgeStringKeys.LanguageSettingsUnitsNote, UnitSystemName);

    /// <summary>Today's date in the current culture's long pattern.</summary>
    public string PreviewDate => formatter.LongDate(DateOnly.FromDateTime(DateTime.Now));

    /// <summary>A number showing the culture's group and decimal separators.</summary>
    public string PreviewNumber => formatter.Number(1234.5);

    /// <summary>A percentage showing the culture's percent conventions.</summary>
    public string PreviewPercent => formatter.Percent(0.735, 1);

    /// <summary>An elapsed workout duration.</summary>
    public string PreviewDuration => formatter.Duration(TimeSpan.FromSeconds(4530));

    /// <summary>A body weight, converted by unit preference and written by culture.</summary>
    public string PreviewBodyWeight => formatter.Mass(82.5);

    /// <summary>A daily energy target, converted by unit preference and written by culture.</summary>
    public string PreviewEnergy => formatter.Energy(2200);

    /// <summary>Whether the current language is written right to left.</summary>
    public bool IsRightToLeft => localization.IsRightToLeft;

    /// <summary>Starts listening for changes made elsewhere. Call from <c>OnAppearing</c>.</summary>
    /// <remarks>
    /// The localization service is a singleton and this view model is transient, so subscribing
    /// in the constructor without a matching detach would keep every page instance the user ever
    /// opened alive for the life of the app.
    /// </remarks>
    public void Attach()
    {
        if (subscribed)
        {
            return;
        }

        subscribed = true;
        localization.LanguageChanged += OnLanguageChanged;
        preferences.PreferencesChanged += OnPreferencesChanged;
        Refresh();
    }

    /// <summary>Stops listening. Call from <c>OnDisappearing</c>.</summary>
    public void Detach()
    {
        if (!subscribed)
        {
            return;
        }

        localization.LanguageChanged -= OnLanguageChanged;
        preferences.PreferencesChanged -= OnPreferencesChanged;
        subscribed = false;
    }

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e) => Refresh();

    private void OnPreferencesChanged(object? sender, PreferenceChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        OnPropertyChanged(nameof(FollowSystemLanguage));
        OnPropertyChanged(nameof(CanChooseLanguage));
        OnPropertyChanged(nameof(SelectedLanguageName));
        OnPropertyChanged(nameof(CurrentLanguageSummary));
        OnPropertyChanged(nameof(UnitSystemName));
        OnPropertyChanged(nameof(UnitsNote));
        OnPropertyChanged(nameof(PreviewDate));
        OnPropertyChanged(nameof(PreviewNumber));
        OnPropertyChanged(nameof(PreviewPercent));
        OnPropertyChanged(nameof(PreviewDuration));
        OnPropertyChanged(nameof(PreviewBodyWeight));
        OnPropertyChanged(nameof(PreviewEnergy));
        OnPropertyChanged(nameof(IsRightToLeft));
    }
}
