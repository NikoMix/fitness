#if ANDROID || IOS
using Forge.Core.Abstractions.Security;
#endif

#if IOS
using UIKit;
#endif

namespace Forge.App.Services.Security;

#if ANDROID

/// <summary>
/// Hides Forge from the Android recents list using the secure window flag.
/// </summary>
/// <remarks>
/// <para>
/// <c>FLAG_SECURE</c> makes the system draw a blank placeholder instead of a screenshot in
/// recents, and additionally blocks screenshots and screen recording of the app. That second
/// effect is a side effect of the only mechanism Android offers, and the settings screen says
/// so rather than letting the user discover it when a screenshot silently fails.
/// </para>
/// <para>
/// The flag belongs to the window, so it is reapplied when the app returns to the foreground:
/// an activity destroyed under memory pressure comes back with a fresh window and no flag.
/// </para>
/// </remarks>
internal sealed class PlatformPrivacyScreenController : IPrivacyScreenController
{
    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public bool IsHidingEnabled { get; private set; }

    /// <inheritdoc />
    public void SetHidingEnabled(bool enabled)
    {
        IsHidingEnabled = enabled;
        Apply();
    }

    /// <inheritdoc />
    public void OnEnteringBackground()
    {
        // Nothing to do. The flag is already on the window, and Android applies it when it
        // captures the recents thumbnail.
    }

    /// <inheritdoc />
    public void OnEnteredForeground() => Apply();

    private void Apply()
    {
        var enabled = IsHidingEnabled;

        RunOnMainThread(() =>
        {
            if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window is not { } window)
            {
                return;
            }

            if (enabled)
            {
                window.AddFlags(Android.Views.WindowManagerFlags.Secure);
            }
            else
            {
                window.ClearFlags(Android.Views.WindowManagerFlags.Secure);
            }
        });
    }

    private static void RunOnMainThread(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
            return;
        }

        MainThread.BeginInvokeOnMainThread(action);
    }
}

#elif IOS

/// <summary>
/// Covers Forge's content before iOS photographs it for the app switcher.
/// </summary>
/// <remarks>
/// <para>
/// iOS has no equivalent of Android's secure window flag. It takes a snapshot of the running
/// app as it leaves the foreground and writes that image into the app's own container, where it
/// survives until the app is next opened. Adding an opaque blur over the window before the
/// snapshot is taken is the standard, and only, way to keep body measurements and training
/// history out of that image.
/// </para>
/// <para>
/// The cover is added on resign-activation rather than on entering the background, because that
/// is the last event guaranteed to run before the snapshot. It is added synchronously for the
/// same reason: dispatching it would let the snapshot win the race.
/// </para>
/// </remarks>
internal sealed class PlatformPrivacyScreenController : IPrivacyScreenController
{
    private UIVisualEffectView? cover;

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public bool IsHidingEnabled { get; private set; }

    /// <inheritdoc />
    public void SetHidingEnabled(bool enabled)
    {
        IsHidingEnabled = enabled;

        if (!enabled)
        {
            RunOnMainThread(HideCover);
        }
    }

    /// <inheritdoc />
    public void OnEnteringBackground()
    {
        if (!IsHidingEnabled)
        {
            return;
        }

        RunOnMainThread(ShowCover);
    }

    /// <inheritdoc />
    public void OnEnteredForeground() => RunOnMainThread(HideCover);

    private void ShowCover()
    {
        if (cover is not null || FindActiveWindow() is not { } window)
        {
            return;
        }

        var view = new UIVisualEffectView(UIBlurEffect.FromStyle(UIBlurEffectStyle.SystemMaterial))
        {
            Frame = window.Bounds,
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
        };

        window.AddSubview(view);
        window.BringSubviewToFront(view);
        cover = view;
    }

    private void HideCover()
    {
        if (cover is null)
        {
            return;
        }

        cover.RemoveFromSuperview();
        cover.Dispose();
        cover = null;
    }

    private static UIWindow? FindActiveWindow()
    {
        // Walked through the connected scenes rather than through UIApplication.KeyWindow,
        // which iOS 13 deprecated and which returns nothing in a multi-scene app.
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is not UIWindowScene windowScene)
            {
                continue;
            }

            foreach (var window in windowScene.Windows)
            {
                if (window.IsKeyWindow)
                {
                    return window;
                }
            }
        }

        return null;
    }

    private static void RunOnMainThread(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
            return;
        }

        MainThread.BeginInvokeOnMainThread(action);
    }
}

#endif
