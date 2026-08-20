#if ANDROID
using Android.Provider;
using Microsoft.Maui.Devices;
#elif IOS
using UIKit;
#endif

namespace Forge.App.Motion;

public interface IMotionPreferences
{
    bool IsReduceMotionEnabled { get; }

    bool IsHapticFeedbackEnabled { get; }
}

public sealed class PlatformMotionPreferences : IMotionPreferences
{
    public bool IsReduceMotionEnabled
    {
        get
        {
#if ANDROID
            var resolver = Android.App.Application.Context.ContentResolver;
            var animatorScale = Settings.Global.GetFloat(resolver, Settings.Global.AnimatorDurationScale, 1f);
            var transitionScale = Settings.Global.GetFloat(resolver, Settings.Global.TransitionAnimationScale, 1f);
            return animatorScale == 0f || transitionScale == 0f;
#elif IOS
            return UIAccessibility.IsReduceMotionEnabled;
#else
            return false;
#endif
        }
    }

    public bool IsHapticFeedbackEnabled
    {
        get
        {
#if ANDROID
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                return HapticFeedback.Default.IsSupported;
            }

            var resolver = Android.App.Application.Context.ContentResolver;
            return Settings.System.GetInt(resolver, Settings.System.HapticFeedbackEnabled, 1) != 0;
#elif IOS
            return true;
#else
            return true;
#endif
        }
    }
}

public static class MotionPreferences
{
    private static IMotionPreferences? current;

    public static IMotionPreferences Current
    {
        get => current ??= new PlatformMotionPreferences();
        set => current = value ?? throw new ArgumentNullException(nameof(value));
    }
}
