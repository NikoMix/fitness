using System.Globalization;

namespace Forge.App.Motion;

public sealed class AnimatedNumberBehavior : Behavior<Label>, IDisposable
{
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value),
        typeof(double),
        typeof(AnimatedNumberBehavior),
        0d,
        propertyChanged: OnValueChanged);

    public static readonly BindableProperty FormatProperty = BindableProperty.Create(
        nameof(Format),
        typeof(string),
        typeof(AnimatedNumberBehavior),
        "0");

    public static readonly BindableProperty DurationProperty = BindableProperty.Create(
        nameof(Duration),
        typeof(uint),
        typeof(AnimatedNumberBehavior),
        MotionTokens.Medium);

    private Label? label;
    private double displayedValue;
    private CancellationTokenSource? animationCancellation;

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public uint Duration
    {
        get => (uint)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    protected override void OnAttachedTo(Label bindable)
    {
        base.OnAttachedTo(bindable);
        label = bindable;
        displayedValue = TryParse(bindable.Text, out var parsed) ? parsed : Value;
        bindable.Text = displayedValue.ToString(Format, CultureInfo.CurrentCulture);
    }

    protected override void OnDetachingFrom(Label bindable)
    {
        CancelAnimation();
        label = null;
        base.OnDetachingFrom(bindable);
    }

    private static void OnValueChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((AnimatedNumberBehavior)bindable).AnimateTo((double)newValue);
    }

    private void AnimateTo(double newValue)
    {
        if (label is null)
        {
            displayedValue = newValue;
            return;
        }

        CancelAnimation();

        if (MotionPreferences.Current.IsReduceMotionEnabled || Duration == MotionTokens.Instant)
        {
            displayedValue = newValue;
            label.Text = newValue.ToString(Format, CultureInfo.CurrentCulture);
            return;
        }

        animationCancellation = new CancellationTokenSource();
        var from = displayedValue;
        var token = animationCancellation.Token;
        _ = ForgeAnimations.CountUpAsync(label, from, newValue, Format, CultureInfo.CurrentCulture, Duration, cancellationToken: token)
            .ContinueWith(task =>
            {
                if (!task.IsCanceled && !task.IsFaulted)
                {
                    displayedValue = newValue;
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
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

    private static bool TryParse(string? text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value);
    }
}
