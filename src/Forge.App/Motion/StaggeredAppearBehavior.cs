namespace Forge.App.Motion;

public sealed class StaggeredAppearBehavior : Behavior<VisualElement>, IDisposable
{
    public static readonly BindableProperty IndexProperty = BindableProperty.Create(
        nameof(Index),
        typeof(int),
        typeof(StaggeredAppearBehavior),
        0);

    public static readonly BindableProperty ItemDelayProperty = BindableProperty.Create(
        nameof(ItemDelay),
        typeof(int),
        typeof(StaggeredAppearBehavior),
        35);

    public static readonly BindableProperty MaxTotalDelayProperty = BindableProperty.Create(
        nameof(MaxTotalDelay),
        typeof(int),
        typeof(StaggeredAppearBehavior),
        210);

    public static readonly BindableProperty DistanceProperty = BindableProperty.Create(
        nameof(Distance),
        typeof(double),
        typeof(StaggeredAppearBehavior),
        12d);

    private VisualElement? element;
    private CancellationTokenSource? animationCancellation;

    public int Index
    {
        get => (int)GetValue(IndexProperty);
        set => SetValue(IndexProperty, value);
    }

    public int ItemDelay
    {
        get => (int)GetValue(ItemDelayProperty);
        set => SetValue(ItemDelayProperty, value);
    }

    public int MaxTotalDelay
    {
        get => (int)GetValue(MaxTotalDelayProperty);
        set => SetValue(MaxTotalDelayProperty, value);
    }

    public double Distance
    {
        get => (double)GetValue(DistanceProperty);
        set => SetValue(DistanceProperty, value);
    }

    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);
        element = bindable;
        bindable.Loaded += OnLoaded;
        bindable.Unloaded += OnUnloaded;
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
        bindable.Loaded -= OnLoaded;
        bindable.Unloaded -= OnUnloaded;
        CancelAnimation();
        element = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (element is null)
        {
            return;
        }

        CancelAnimation();
        animationCancellation = new CancellationTokenSource();
        var token = animationCancellation.Token;
        _ = RunAsync(element, token);
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        CancelAnimation();
    }

    private async Task RunAsync(VisualElement target, CancellationToken cancellationToken)
    {
        if (MotionPreferences.Current.IsReduceMotionEnabled)
        {
            target.Opacity = 1;
            target.TranslationY = 0;
            return;
        }

        target.Opacity = 0;
        target.TranslationY = Distance;
        var delay = Math.Min(Math.Max(0, Index) * Math.Max(0, ItemDelay), Math.Max(0, MaxTotalDelay));
        if (delay > 0)
        {
            await Task.Delay(delay, cancellationToken);
        }

        await ForgeAnimations.SlideInAsync(target, 0, Distance, MotionTokens.Medium, cancellationToken: cancellationToken);
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
