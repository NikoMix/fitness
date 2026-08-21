using System.Globalization;
using Forge.App.Motion;

namespace Forge.App.Controls;

/// <summary>
/// A single activity ring: circular progress, a percentage in the middle, a label and a detail line.
/// </summary>
/// <remarks>
/// <para>
/// The ring is square and fills whatever width its container gives it, clamped so it never shrinks
/// below a legible size or grows past the point where it stops reading as a compact tile. Callers
/// therefore control the layout - typically a wrapping <c>FlexLayout</c> with a percentage basis -
/// and never have to guess a pixel size that happens to fit one particular handset.
/// </para>
/// <para>
/// The three inner labels are removed from the accessibility tree and replaced by one description
/// on the tile, so a screen reader announces "Training, 40 percent, 2 of 5 working sets" as a
/// single coherent item instead of three disconnected fragments.
/// </para>
/// </remarks>
public partial class ActivityRing : ContentView
{
    // Below the primary touch target the percentage in the middle stops being legible at arm's
    // length; past roughly two of them a progress ring reads as a chart rather than as a tile in a
    // row of tiles. These bound the automatic fit; they are not a fixed size.
    private const double MinimumAutoDiameterFallback = 64;
    private const double AutoDiameterCeilingFactor = 2.2;

    /// <summary>Identifies the <see cref="Progress"/> bindable property.</summary>
    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress),
        typeof(double),
        typeof(ActivityRing),
        0d,
        propertyChanged: OnProgressChanged);

    /// <summary>Identifies the <see cref="Label"/> bindable property.</summary>
    public static readonly BindableProperty LabelProperty = BindableProperty.Create(
        nameof(Label),
        typeof(string),
        typeof(ActivityRing),
        string.Empty,
        propertyChanged: OnContentChanged);

    /// <summary>Identifies the <see cref="Detail"/> bindable property.</summary>
    public static readonly BindableProperty DetailProperty = BindableProperty.Create(
        nameof(Detail),
        typeof(string),
        typeof(ActivityRing),
        string.Empty,
        propertyChanged: OnContentChanged);

    /// <summary>Identifies the read-only <see cref="ValueText"/> bindable property.</summary>
    public static readonly BindableProperty ValueTextProperty = BindableProperty.Create(
        nameof(ValueText),
        typeof(string),
        typeof(ActivityRing),
        "0%");

    /// <summary>Initialises the control.</summary>
    public ActivityRing()
    {
        InitializeComponent();

        // The ring animates its value by default. Reduce Motion has to switch that off, and it is
        // read through Forge's shared preference so the Settings toggle and the OS setting both
        // apply rather than only the OS one.
        Ring.AllowAnimation = !MotionPreferences.Current.IsReduceMotionEnabled;
        RingHost.SizeChanged += OnRingHostSizeChanged;
        UpdateValueText();
        UpdateSemanticDescription();
    }

    /// <summary>Ring completion between 0 and 1.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>What the ring measures, for example "Training".</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>The real numbers behind the ring, for example "2 of 5 working sets".</summary>
    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    /// <summary>The percentage rendered in the middle of the ring.</summary>
    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        private set => SetValue(ValueTextProperty, value);
    }

    private static void OnProgressChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var ring = (ActivityRing)bindable;
        ring.UpdateValueText();
        ring.UpdateSemanticDescription();
    }

    private static void OnContentChanged(BindableObject bindable, object oldValue, object newValue)
        => ((ActivityRing)bindable).UpdateSemanticDescription();

    private void OnRingHostSizeChanged(object? sender, EventArgs e)
    {
        var available = RingHost.Width;
        if (available <= 0)
        {
            return;
        }

        var minimum = ForgeResources.Double("TouchTargetPrimary", MinimumAutoDiameterFallback);
        var diameter = Math.Clamp(available, minimum, minimum * AutoDiameterCeilingFactor);
        if (Math.Abs(Ring.HeightRequest - diameter) < 0.5)
        {
            return;
        }

        Ring.HeightRequest = diameter;
        Ring.WidthRequest = diameter;
        RingHost.HeightRequest = diameter;
    }

    private void UpdateValueText()
    {
        var percent = (int)Math.Round(Math.Clamp(Progress, 0d, 1d) * 100, MidpointRounding.AwayFromZero);
        ValueText = string.Create(CultureInfo.CurrentCulture, $"{percent}%");
    }

    private void UpdateSemanticDescription()
    {
        var parts = new[] { Label, ValueText, Detail }.Where(text => !string.IsNullOrWhiteSpace(text));
        SemanticProperties.SetDescription(TileChrome, string.Join(", ", parts));
    }
}
