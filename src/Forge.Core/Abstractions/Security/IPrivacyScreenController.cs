namespace Forge.Core.Abstractions.Security;

/// <summary>
/// Hides Forge's content from the operating system's app switcher.
/// </summary>
/// <remarks>
/// <para>
/// Both platforms photograph a running app to draw the task switcher, and on both that image
/// outlives the app: Android keeps it in the recents list, iOS writes it to disk in the app's
/// own container. A lock that only guards the running app while the last screen of body
/// measurements sits in the switcher behind it is theatre, so the two features are turned on
/// together.
/// </para>
/// <para>
/// The mechanisms differ enough that this interface deliberately exposes intent rather than
/// mechanism. Android sets a window flag once and the system honours it thereafter; iOS has no
/// equivalent flag and needs a cover view added before the snapshot is taken and removed after.
/// </para>
/// </remarks>
public interface IPrivacyScreenController
{
    /// <summary>Whether this device can hide app-switcher content at all.</summary>
    bool IsSupported { get; }

    /// <summary>Whether hiding is currently switched on.</summary>
    bool IsHidingEnabled { get; }

    /// <summary>Turns app-switcher hiding on or off, taking effect as soon as the platform allows.</summary>
    /// <param name="enabled">Whether Forge content should be hidden from the switcher.</param>
    void SetHidingEnabled(bool enabled);

    /// <summary>
    /// Called when the app is about to lose the foreground, which is the last moment before the
    /// operating system may photograph it.
    /// </summary>
    void OnEnteringBackground();

    /// <summary>Called once the app is interactive again, so any cover can be removed.</summary>
    void OnEnteredForeground();
}
