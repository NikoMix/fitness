using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Core.Abstractions.Security;

namespace Forge.App.Features.Security.ViewModels;

/// <summary>Backs the app lock settings screen.</summary>
/// <remarks>
/// Every claim on this screen is one the app can actually keep. Where a control does something
/// less than its name suggests - the workout allowance quietly lengthening a chosen grace
/// period, the Android privacy flag also blocking screenshots - the screen says so instead of
/// leaving the user to find out.
/// </remarks>
public sealed partial class AppLockSettingsPageViewModel(
    AppLockCoordinator coordinator,
    IAppLockSettings settings,
    IPrivacyScreenController privacyScreen) : ObservableObject
{
    private static readonly string[] GraceLabels =
    [
        "Immediately",
        "After 15 seconds",
        "After 1 minute",
        "After 5 minutes",
        "After 15 minutes",
    ];

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string capabilitySummary = "Checking what this device can do…";

    [ObservableProperty]
    private bool isBusy;

    /// <summary>The grace periods a user may choose between.</summary>
    public IReadOnlyList<string> GraceOptions { get; } = GraceLabels;

    /// <summary>Whether the lock is switched on.</summary>
    /// <remarks>
    /// The setter starts an authenticated change rather than writing the preference. If the
    /// prompt is cancelled or fails, the property change notification at the end of that work
    /// snaps the control back to reality instead of leaving the UI claiming a lock that was
    /// never turned on.
    /// </remarks>
    public bool IsLockEnabled
    {
        get => settings.IsEnabled;
        set
        {
            if (value == settings.IsEnabled || IsBusy)
            {
                return;
            }

            _ = ApplyLockEnabledAsync(value);
        }
    }

    /// <summary>The chosen background grace period, as a display label.</summary>
    public string SelectedGrace
    {
        get => LabelFor(settings.GraceDuration);
        set
        {
            var index = Array.IndexOf(GraceLabels, value);
            if (index < 0)
            {
                return;
            }

            settings.GraceDuration = AppLockSettings.GraceOptions[index];
            Refresh();
        }
    }

    /// <summary>Whether the grace period is extended while a workout is running.</summary>
    public bool RelaxDuringWorkout
    {
        get => settings.RelaxDuringActivity;
        set
        {
            settings.RelaxDuringActivity = value;
            Refresh();
        }
    }

    /// <summary>Whether Forge content is hidden from the operating system app switcher.</summary>
    public bool HideInAppSwitcher
    {
        get => settings.HideInAppSwitcher;
        set
        {
            coordinator.SetHideInAppSwitcher(value);
            Refresh();
        }
    }

    /// <summary>Explains, without overclaiming, what the lock is for.</summary>
    public string ScopeText { get; } =
        "App lock asks the device to confirm who you are before Forge shows your training "
        + "history, body measurements and nutrition log.";

    /// <summary>Explains what the lock does not protect against.</summary>
    public string LimitsText { get; } =
        "It does not encrypt anything extra. Your Forge database is already encrypted on this "
        + "device with a key held in the system keystore, and that key is available to the app "
        + "whether or not this lock is on. Anyone who knows your device passcode can get past "
        + "this lock, and anyone with your unlocked phone in their hand can get past it if they "
        + "can also pass your fingerprint or face check. Treat it as a curtain, not a safe.";

    /// <summary>Explains the recovery guarantee.</summary>
    public string RecoveryText { get; } =
        "You cannot be locked out. Forge only turns this on after a successful check on this "
        + "device, failed attempts never delete anything, and if the device stops being able to "
        + "recognise you - a removed passcode, a broken sensor, a restored backup - Forge turns "
        + "the lock off and lets you in rather than keeping you out.";

    /// <summary>Explains the workout allowance in the same words the code implements.</summary>
    public string WorkoutAllowanceText =>
        RelaxDuringWorkout
            ? "During a workout the grace period is stretched to at least "
              + $"{(int)AppLockPolicy.ActivityGraceFloor.TotalMinutes} minutes, so a screen-off "
              + "rest between sets or a glance at a message does not put a lock screen between "
              + "you and your next set. It is never shortened below your choice above."
            : "The grace period above applies during workouts too. Expect to unlock between "
              + "sets if you put the phone down.";

    /// <summary>Explains the app-switcher setting, including the Android side effect.</summary>
    public string AppSwitcherText =>
        privacyScreen.IsSupported
            ? "Hides Forge's content in the app switcher, so the last screen you had open is not "
              + "left on display when you switch apps. On Android this uses the system's secure "
              + "window flag, which also blocks screenshots and screen recording of Forge."
            : "This device cannot hide Forge's content in the app switcher, so the setting is "
              + "not offered.";

    /// <summary>Whether the app-switcher control should be shown at all.</summary>
    public bool IsAppSwitcherSupported => privacyScreen.IsSupported;

    /// <summary>Re-reads what the device can currently do.</summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>A task that completes once the summary has been refreshed.</returns>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var capability = await coordinator.GetCapabilityAsync(cancellationToken).ConfigureAwait(true);

        CapabilitySummary = capability switch
        {
            AppLockCapability.Biometric =>
                "This device can check your fingerprint or face, and will offer your passcode if that fails.",
            AppLockCapability.DeviceCredentialOnly =>
                "No fingerprint or face is enrolled, so Forge will ask for your PIN, pattern or passcode.",
            AppLockCapability.TemporarilyUnavailable =>
                "Forge could not reach this device's lock check just now. Try again in a moment.",
            _ => "This device has no screen lock set, so app lock is unavailable. Add a PIN, "
                 + "pattern or passcode in system settings first.",
        };

        Refresh();
    }

    private static string LabelFor(TimeSpan grace)
    {
        for (var index = 0; index < AppLockSettings.GraceOptions.Count; index++)
        {
            if (AppLockSettings.GraceOptions[index] == grace)
            {
                return GraceLabels[index];
            }
        }

        return string.Create(CultureInfo.CurrentCulture, $"After {grace.TotalSeconds:N0} seconds");
    }

    private async Task ApplyLockEnabledAsync(bool desired)
    {
        IsBusy = true;

        try
        {
            var result = desired
                ? await coordinator.TryEnableAsync(CancellationToken.None).ConfigureAwait(true)
                : await coordinator.TryDisableAsync(CancellationToken.None).ConfigureAwait(true);

            StatusMessage = result.IsSuccess
                ? desired
                    ? "App lock is on. Forge will ask for this the next time it starts."
                    : "App lock is off."
                : result.Message ?? "Nothing changed.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Nothing changed.";
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsLockEnabled));
        OnPropertyChanged(nameof(SelectedGrace));
        OnPropertyChanged(nameof(RelaxDuringWorkout));
        OnPropertyChanged(nameof(HideInAppSwitcher));
        OnPropertyChanged(nameof(WorkoutAllowanceText));
        OnPropertyChanged(nameof(AppSwitcherText));
        OnPropertyChanged(nameof(IsAppSwitcherSupported));
    }
}
