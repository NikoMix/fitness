using Forge.Core.Abstractions.Preferences;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace Forge.App.Features.Settings.Services;

/// <summary>Applies the stored theme preference to the running MAUI application.</summary>
public sealed class MauiThemePreferenceApplier
{
    private readonly IForgePreferences preferences;

    /// <summary>Creates a theme preference applier.</summary>
    public MauiThemePreferenceApplier(IForgePreferences preferences)
    {
        this.preferences = preferences;
        preferences.PreferencesChanged += OnPreferencesChanged;
    }

    /// <summary>Applies the currently stored theme preference.</summary>
    public void ApplyStoredTheme() => Apply(preferences.ThemeMode);

    private void OnPreferencesChanged(object? sender, PreferenceChangedEventArgs e)
    {
        if (e.PreferenceKey == ForgePreferenceKeys.ThemeMode)
        {
            ApplyStoredTheme();
        }
    }

    private static void Apply(ThemeModePreference mode)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Application.Current is null)
            {
                return;
            }

            Application.Current.UserAppTheme = mode switch
            {
                ThemeModePreference.Light => AppTheme.Light,
                ThemeModePreference.Dark => AppTheme.Dark,
                _ => AppTheme.Unspecified,
            };
        });
    }
}
