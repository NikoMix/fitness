#if ANDROID
using Android.Content;
using Android.Net;
using Android.Provider;
#elif IOS
using UIKit;
#endif

namespace Forge.App.Features.Media;

public sealed class MauiMediaPlaybackPolicy : IMediaPlaybackPolicy
{
    public bool ShouldSuppressAutoplay() => IsReduceMotionEnabled() || IsDataSaverEnabled();

    private static bool IsReduceMotionEnabled()
    {
#if ANDROID
        // Fully qualified with global:: because the sibling namespace
        // Forge.App.Features.Settings shadows Android.Provider.Settings from inside
        // Forge.App.Features.Media. This is the same shadowing trap that forced
        // Forge.Application -> Forge.Core and Forge.App.Shell -> Forge.App.Hosting.
        var resolver = Android.App.Application.Context.ContentResolver;
        var animatorScale = global::Android.Provider.Settings.Global.GetFloat(
            resolver, global::Android.Provider.Settings.Global.AnimatorDurationScale, 1f);
        var transitionScale = global::Android.Provider.Settings.Global.GetFloat(
            resolver, global::Android.Provider.Settings.Global.TransitionAnimationScale, 1f);
        return animatorScale == 0f || transitionScale == 0f;
#elif IOS
        return UIAccessibility.IsReduceMotionEnabled;
#else
        return false;
#endif
    }

    private static bool IsDataSaverEnabled()
    {
#if ANDROID
        var manager = Android.App.Application.Context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
        return manager?.RestrictBackgroundStatus == RestrictBackgroundStatus.Enabled;
#else
        return false;
#endif
    }
}
