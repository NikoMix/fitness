#if ANDROID
using Android.Provider;
#elif IOS
using UIKit;
#endif

namespace Forge.App.Controls;

public partial class SkeletonPlaceholder : ContentView
{
    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy),
        typeof(bool),
        typeof(SkeletonPlaceholder),
        true,
        propertyChanged: OnIsBusyChanged);

    private int animationRun;

    public SkeletonPlaceholder()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    private static void OnIsBusyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((SkeletonPlaceholder)bindable).UpdateAnimation();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        UpdateAnimation();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        StopAnimation();
    }

    private void UpdateAnimation()
    {
        StopAnimation();
        IsVisible = IsBusy;

        if (!IsBusy)
        {
            return;
        }

        SkeletonBlock.Opacity = 1;

        if (IsReduceMotionEnabled())
        {
            return;
        }

        var run = ++animationRun;
        _ = PulseAsync(run);
    }

    private async Task PulseAsync(int run)
    {
        while (run == animationRun && IsBusy)
        {
            await SkeletonBlock.FadeToAsync(0.55, 650, Easing.CubicInOut);
            if (run != animationRun || !IsBusy)
            {
                break;
            }

            await SkeletonBlock.FadeToAsync(1, 650, Easing.CubicInOut);
        }
    }

    private void StopAnimation()
    {
        animationRun++;
        SkeletonBlock.AbortAnimation("FadeTo");
        SkeletonBlock.Opacity = 1;
    }

    private static bool IsReduceMotionEnabled()
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
