using Forge.Core.Abstractions.Preferences;
using Microsoft.Maui.Storage;

namespace Forge.App.Features.Settings.Services;

/// <summary>MAUI-backed implementation of Forge's local preference key-value store.</summary>
public sealed class MauiPreferenceStore : IPreferenceStore
{
    /// <inheritdoc />
    public string GetString(string key, string defaultValue) => Preferences.Default.Get(key, defaultValue);

    /// <inheritdoc />
    public void SetString(string key, string value) => Preferences.Default.Set(key, value);

    /// <inheritdoc />
    public bool GetBoolean(string key, bool defaultValue) => Preferences.Default.Get(key, defaultValue);

    /// <inheritdoc />
    public void SetBoolean(string key, bool value) => Preferences.Default.Set(key, value);

    /// <inheritdoc />
    public int GetInt32(string key, int defaultValue) => Preferences.Default.Get(key, defaultValue);

    /// <inheritdoc />
    public void SetInt32(string key, int value) => Preferences.Default.Set(key, value);
}
