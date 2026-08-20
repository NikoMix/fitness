namespace Forge.App.Motion;

public sealed class PressFeedbackBehavior : Behavior<VisualElement>, IDisposable
{
    public static readonly BindableProperty PressedScaleProperty = BindableProperty.Create(
        nameof(PressedScale),
        typeof(double),
        typeof(PressFeedbackBehavior),
        0.97d);

    public static readonly BindableProperty DurationProperty = BindableProperty.Create(
        nameof(Duration),
        typeof(uint),
        typeof(PressFeedbackBehavior),
        MotionTokens.Fast);

    private VisualElement? element;
    private TapGestureRecognizer? tapRecognizer;
    private CancellationTokenSource? animationCancellation;

    public double PressedScale
    {
        get => (double)GetValue(PressedScaleProperty);
        set => SetValue(PressedScaleProperty, value);
    }

    public uint Duration
    {
        get => (uint)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);
        element = bindable;

        if (bindable is Button button)
        {
            button.Pressed += OnPressed;
            button.Released += OnReleased;
            return;
        }

        if (bindable is ImageButton imageButton)
        {
            imageButton.Pressed += OnPressed;
            imageButton.Released += OnReleased;
            return;
        }

        // GestureRecognizers is declared on View, not VisualElement. A behaviour attached to
        // a non-View VisualElement therefore cannot receive taps, and silently doing nothing
        // is better than throwing: press feedback is decoration, not function.
        if (bindable is View view)
        {
            tapRecognizer = new TapGestureRecognizer();
            tapRecognizer.Tapped += OnTapped;
            view.GestureRecognizers.Add(tapRecognizer);
        }
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
        CancelAnimation();

        if (bindable is Button button)
        {
            button.Pressed -= OnPressed;
            button.Released -= OnReleased;
        }
        else if (bindable is ImageButton imageButton)
        {
            imageButton.Pressed -= OnPressed;
            imageButton.Released -= OnReleased;
        }

        if (tapRecognizer is not null)
        {
            tapRecognizer.Tapped -= OnTapped;
            if (bindable is View view)
            {
                view.GestureRecognizers.Remove(tapRecognizer);
            }

            tapRecognizer = null;
        }

        bindable.Scale = 1;
        element = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnPressed(object? sender, EventArgs e)
    {
        if (element is null || MotionPreferences.Current.IsReduceMotionEnabled)
        {
            return;
        }

        ForgeAnimations.TryHapticClick();
        AnimateScale(PressedScale, Duration / 2);
    }

    private void OnReleased(object? sender, EventArgs e)
    {
        if (element is null || MotionPreferences.Current.IsReduceMotionEnabled)
        {
            return;
        }

        AnimateScale(1, Duration / 2);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (element is null)
        {
            return;
        }

        ForgeAnimations.TryHapticClick();
        CancelAnimation();
        animationCancellation = new CancellationTokenSource();
        _ = ForgeAnimations.ScalePressAsync(element, PressedScale, Duration, cancellationToken: animationCancellation.Token);
    }

    private void AnimateScale(double scale, uint duration)
    {
        if (element is null)
        {
            return;
        }

        CancelAnimation();
        animationCancellation = new CancellationTokenSource();
        _ = ForgeAnimations.ScaleToAsync(element, scale, duration, cancellationToken: animationCancellation.Token);
    }

    private void CancelAnimation()
    {
        if (animationCancellation is null)
        {
            return;
        }

        animationCancellation.Cancel();
        animationCancellation.Dispose();
        animationCancellation = null;
    }

    public void Dispose()
    {
        CancelAnimation();
        GC.SuppressFinalize(this);
    }
}
