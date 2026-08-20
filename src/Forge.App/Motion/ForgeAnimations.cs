using System.Globalization;
using Microsoft.Maui.Devices;

namespace Forge.App.Motion;

public static class ForgeAnimations
{
    private const string FadeAnimation = "ForgeFade";
    private const string ScaleAnimation = "ForgeScale";
    private const string TranslateAnimation = "ForgeTranslate";
    private const string CountAnimation = "ForgeCount";
    private const string PulseAnimation = "ForgePulse";
    private const string CelebrationAnimation = "ForgeCelebration";

    public static Task FadeInAsync(VisualElement element, uint duration = MotionTokens.Fast, IMotionPreferences? preferences = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (ShouldSkip(preferences, cancellationToken) || duration == MotionTokens.Instant)
        {
            element.Opacity = 1;
            return Task.CompletedTask;
        }

        element.AbortAnimation(FadeAnimation);
        element.Opacity = 0;
        return AnimateAsync(element, FadeAnimation, duration, MotionTokens.Entrance, progress => element.Opacity = progress, cancellationToken);
    }

    public static Task SlideInAsync(VisualElement element, double fromX = 0, double fromY = 16, uint duration = MotionTokens.Medium, IMotionPreferences? preferences = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (ShouldSkip(preferences, cancellationToken) || duration == MotionTokens.Instant)
        {
            element.TranslationX = 0;
            element.TranslationY = 0;
            element.Opacity = 1;
            return Task.CompletedTask;
        }

        element.AbortAnimation(TranslateAnimation);
        element.AbortAnimation(FadeAnimation);
        element.TranslationX = fromX;
        element.TranslationY = fromY;
        element.Opacity = 0;

        var translateX = AnimateAsync(element, TranslateAnimation + "X", duration, MotionTokens.Entrance, progress => element.TranslationX = fromX * (1 - progress), cancellationToken);
        var translateY = AnimateAsync(element, TranslateAnimation + "Y", duration, MotionTokens.Entrance, progress => element.TranslationY = fromY * (1 - progress), cancellationToken);
        var fade = AnimateAsync(element, FadeAnimation, duration, MotionTokens.Entrance, progress => element.Opacity = progress, cancellationToken);
        return Task.WhenAll(translateX, translateY, fade);
    }

    public static async Task ScalePressAsync(VisualElement element, double pressedScale = 0.97, uint duration = MotionTokens.Fast, IMotionPreferences? preferences = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (ShouldSkip(preferences, cancellationToken) || duration == MotionTokens.Instant)
        {
            element.Scale = 1;
            return;
        }

        element.AbortAnimation(ScaleAnimation);
        await AnimateAsync(element, ScaleAnimation + "Down", duration / 2, MotionTokens.Press, progress => element.Scale = 1 - ((1 - pressedScale) * progress), cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await AnimateAsync(element, ScaleAnimation + "Up", duration / 2, MotionTokens.Press, progress => element.Scale = pressedScale + ((1 - pressedScale) * progress), cancellationToken);
    }

    public static Task ScaleToAsync(VisualElement element, double scale, uint duration = MotionTokens.Fast, IMotionPreferences? preferences = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (ShouldSkip(preferences, cancellationToken) || duration == MotionTokens.Instant)
        {
            element.Scale = scale;
            return Task.CompletedTask;
        }

        element.AbortAnimation(ScaleAnimation);
        var from = element.Scale;
        return AnimateAsync(element, ScaleAnimation, duration, MotionTokens.Press, progress => element.Scale = from + ((scale - from) * progress), cancellationToken);
    }

    public static async Task CrossFadeAsync(VisualElement from, VisualElement to, uint duration = MotionTokens.Medium, IMotionPreferences? preferences = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        if (ShouldSkip(preferences, cancellationToken) || duration == MotionTokens.Instant)
        {
            from.Opacity = 0;
            from.IsVisible = false;
            to.Opacity = 1;
            to.IsVisible = true;
            return;
        }

        to.Opacity = 0;
        to.IsVisible = true;
        await Task.WhenAll(
            AnimateAsync(from, FadeAnimation + "Out", duration, MotionTokens.Exit, progress => from.Opacity = 1 - progress, cancellationToken),
            AnimateAsync(to, FadeAnimation + "In", duration, MotionTokens.Entrance, progress => to.Opacity = progress, cancellationToken));

        if (!cancellationToken.IsCancellationRequested)
        {
            from.IsVisible = false;
        }
    }

    public static Task CountUpAsync(Label label, double from, double to, string? format = null, IFormatProvider? formatProvider = null, uint duration = MotionTokens.Medium, IMotionPreferences? preferences = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(label);
        formatProvider ??= CultureInfo.CurrentCulture;

        if (ShouldSkip(preferences, cancellationToken) || duration == MotionTokens.Instant)
        {
            label.Text = FormatNumber(to, format, formatProvider);
            return Task.CompletedTask;
        }

        label.AbortAnimation(CountAnimation);
        return AnimateAsync(label, CountAnimation, duration, MotionTokens.Count, progress =>
        {
            var value = from + ((to - from) * progress);
            label.Text = FormatNumber(value, format, formatProvider);
        }, cancellationToken);
    }

    public static async Task PulseAsync(VisualElement element, double peakScale = 1.035, double lowOpacity = 0.78, uint duration = MotionTokens.Slow, int repeatCount = 2, IMotionPreferences? preferences = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (ShouldSkip(preferences, cancellationToken) || duration == MotionTokens.Instant || repeatCount <= 0)
        {
            element.Scale = 1;
            element.Opacity = 1;
            return;
        }

        element.AbortAnimation(PulseAnimation);
        var half = Math.Max(1, duration / 2);
        for (var i = 0; i < repeatCount && !cancellationToken.IsCancellationRequested; i++)
        {
            await Task.WhenAll(
                AnimateAsync(element, PulseAnimation + "ScaleUp", half, MotionTokens.Standard, progress => element.Scale = 1 + ((peakScale - 1) * progress), cancellationToken),
                AnimateAsync(element, PulseAnimation + "FadeDown", half, MotionTokens.Standard, progress => element.Opacity = 1 - ((1 - lowOpacity) * progress), cancellationToken));

            await Task.WhenAll(
                AnimateAsync(element, PulseAnimation + "ScaleDown", half, MotionTokens.Standard, progress => element.Scale = peakScale - ((peakScale - 1) * progress), cancellationToken),
                AnimateAsync(element, PulseAnimation + "FadeUp", half, MotionTokens.Standard, progress => element.Opacity = lowOpacity + ((1 - lowOpacity) * progress), cancellationToken));
        }
    }

    public static async Task CelebrateAsync(Layout container, string glyph = "✦", uint duration = MotionTokens.Celebration, IMotionPreferences? preferences = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(container);

        if (ShouldSkip(preferences, cancellationToken) || duration == MotionTokens.Instant)
        {
            return;
        }

        var overlay = new Grid
        {
            InputTransparent = true,
            Opacity = 0,
            Scale = 0.85,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        var burst = new Label
        {
            Text = glyph,
            FontSize = 56,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        overlay.Children.Add(burst);
        container.Children.Add(overlay);

        try
        {
            await Task.WhenAll(
                AnimateAsync(overlay, CelebrationAnimation + "FadeIn", duration / 3, MotionTokens.Entrance, progress => overlay.Opacity = progress, cancellationToken),
                AnimateAsync(overlay, CelebrationAnimation + "ScaleIn", duration / 3, MotionTokens.Emphasized, progress => overlay.Scale = 0.85 + (0.2 * progress), cancellationToken));

            await Task.WhenAll(
                AnimateAsync(overlay, CelebrationAnimation + "Float", (duration * 2) / 3, MotionTokens.Standard, progress => overlay.TranslationY = -18 * progress, cancellationToken),
                AnimateAsync(overlay, CelebrationAnimation + "FadeOut", (duration * 2) / 3, MotionTokens.Exit, progress => overlay.Opacity = 1 - progress, cancellationToken));
        }
        finally
        {
            container.Children.Remove(overlay);
        }
    }

    internal static void TryHapticClick(IMotionPreferences? preferences = null)
    {
        var effectivePreferences = preferences ?? MotionPreferences.Current;
        if (effectivePreferences.IsReduceMotionEnabled || !effectivePreferences.IsHapticFeedbackEnabled)
        {
            return;
        }

        try
        {
            if (HapticFeedback.Default.IsSupported)
            {
                HapticFeedback.Default.Perform(HapticFeedbackType.Click);
            }
        }
        catch (FeatureNotSupportedException)
        {
        }
        catch (FeatureNotEnabledException)
        {
        }
    }

    private static Task AnimateAsync(VisualElement owner, string name, uint duration, Easing easing, Action<double> update, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = default;
        registration = cancellationToken.Register(() =>
        {
            owner.AbortAnimation(name);
            completion.TrySetCanceled(cancellationToken);
        });

        var animation = new Animation(update, 0, 1, easing);
        animation.Commit(owner, name, 16, duration, null, (_, cancelled) =>
        {
            registration.Dispose();
            if (cancelled && cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            else
            {
                completion.TrySetResult();
            }
        });

        return completion.Task;
    }

    private static bool ShouldSkip(IMotionPreferences? preferences, CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested || (preferences ?? MotionPreferences.Current).IsReduceMotionEnabled;
    }

    private static string FormatNumber(double value, string? format, IFormatProvider provider)
    {
        return string.IsNullOrWhiteSpace(format)
            ? value.ToString("0", provider)
            : value.ToString(format, provider);
    }
}
