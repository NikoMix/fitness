using Forge.Core.Abstractions.Preferences;

namespace Forge.Core.Abstractions.Security;

/// <summary>Stable keys for the app lock's local preferences.</summary>
/// <remarks>
/// Namespaced under <c>forge.preferences.security</c> so they sit alongside the rest of Forge's
/// preferences in one store, and so a backup that round-trips preference keys carries them too.
/// </remarks>
public static class AppLockPreferenceKeys
{
    /// <summary>Stored value for <see cref="IAppLockSettings.IsEnabled"/>.</summary>
    public const string IsEnabled = "forge.preferences.security.app-lock.enabled";

    /// <summary>Stored value for <see cref="IAppLockSettings.GraceDuration"/> in whole seconds.</summary>
    public const string GraceSeconds = "forge.preferences.security.app-lock.grace-seconds";

    /// <summary>Stored value for <see cref="IAppLockSettings.RelaxDuringActivity"/>.</summary>
    public const string RelaxDuringActivity = "forge.preferences.security.app-lock.relax-during-workout";

    /// <summary>Stored value for <see cref="IAppLockSettings.HideInAppSwitcher"/>.</summary>
    public const string HideInAppSwitcher = "forge.preferences.security.app-lock.hide-in-app-switcher";
}

/// <summary>The user's app lock preferences.</summary>
/// <remarks>
/// Kept separate from <c>IForgePreferences</c> so the Security feature owns its own settings
/// surface and neither feature has to edit the other's file to add a value.
/// </remarks>
public interface IAppLockSettings
{
    /// <summary>Raised whenever any app lock preference changes.</summary>
    event EventHandler<PreferenceChangedEventArgs>? Changed;

    /// <summary>
    /// Whether Forge asks the user to authenticate. Off by default: most people do not want a
    /// second lock screen on a phone that already has one, and turning a security control on
    /// for someone without asking is how a fitness app becomes the thing that locks them out of
    /// their own training history.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// How long Forge may stay in the background before returning to it requires a new unlock.
    /// </summary>
    TimeSpan GraceDuration { get; set; }

    /// <summary>
    /// Whether the grace period is extended while a workout is running. On by default, and the
    /// settings screen says so in as many words rather than quietly overriding the choice above.
    /// </summary>
    bool RelaxDuringActivity { get; set; }

    /// <summary>Whether Forge content is hidden from the operating system's app switcher.</summary>
    bool HideInAppSwitcher { get; set; }
}

/// <summary>App lock settings persisted through Forge's shared preference store.</summary>
/// <param name="store">The local key-value store.</param>
public sealed class AppLockSettings(IPreferenceStore store) : IAppLockSettings
{
    /// <summary>
    /// The grace periods offered in the settings screen, shortest first.
    /// </summary>
    /// <remarks>
    /// A fixed list rather than a free-text duration. Every value here has been reasoned about
    /// against the workout case; an arbitrary number entered in a text box has not.
    /// </remarks>
    public static IReadOnlyList<TimeSpan> GraceOptions { get; } =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    ];

    /// <summary>
    /// The default grace period.
    /// </summary>
    /// <remarks>
    /// One minute covers the overwhelmingly common cases - glancing at a notification, changing
    /// a track, taking a photo - without covering "left the phone on the changing room bench".
    /// Zero is offered but is not the default, because an app that demands a fingerprint every
    /// time you check the time is an app people turn the lock off on, which protects nobody.
    /// </remarks>
    public static TimeSpan DefaultGrace { get; } = TimeSpan.FromMinutes(1);

    private const int MaximumGraceSeconds = 3600;

    /// <inheritdoc />
    public event EventHandler<PreferenceChangedEventArgs>? Changed;

    /// <inheritdoc />
    public bool IsEnabled
    {
        get => store.GetBoolean(AppLockPreferenceKeys.IsEnabled, false);
        set => SetBoolean(AppLockPreferenceKeys.IsEnabled, value);
    }

    /// <inheritdoc />
    public TimeSpan GraceDuration
    {
        get => Clamp(store.GetInt32(AppLockPreferenceKeys.GraceSeconds, (int)DefaultGrace.TotalSeconds));
        set
        {
            var seconds = (int)Math.Round(Clamp(value).TotalSeconds, MidpointRounding.AwayFromZero);
            if (store.GetInt32(AppLockPreferenceKeys.GraceSeconds, int.MinValue) == seconds)
            {
                return;
            }

            store.SetInt32(AppLockPreferenceKeys.GraceSeconds, seconds);
            OnChanged(AppLockPreferenceKeys.GraceSeconds);
        }
    }

    /// <inheritdoc />
    public bool RelaxDuringActivity
    {
        get => store.GetBoolean(AppLockPreferenceKeys.RelaxDuringActivity, true);
        set => SetBoolean(AppLockPreferenceKeys.RelaxDuringActivity, value);
    }

    /// <inheritdoc />
    public bool HideInAppSwitcher
    {
        get => store.GetBoolean(AppLockPreferenceKeys.HideInAppSwitcher, true);
        set => SetBoolean(AppLockPreferenceKeys.HideInAppSwitcher, value);
    }

    private static TimeSpan Clamp(TimeSpan value) => Clamp((int)value.TotalSeconds);

    private static TimeSpan Clamp(int seconds) => TimeSpan.FromSeconds(Math.Clamp(seconds, 0, MaximumGraceSeconds));

    private void SetBoolean(string key, bool value)
    {
        if (store.GetBoolean(key, !value) == value)
        {
            return;
        }

        store.SetBoolean(key, value);
        OnChanged(key);
    }

    private void OnChanged(string key) => Changed?.Invoke(this, new PreferenceChangedEventArgs(key));
}
