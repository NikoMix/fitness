namespace Forge.App.Controls;

public partial class MetricTile : ContentView
{
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value),
        typeof(string),
        typeof(MetricTile),
        string.Empty,
        propertyChanged: OnTextChanged);

    public static readonly BindableProperty UnitProperty = BindableProperty.Create(
        nameof(Unit),
        typeof(string),
        typeof(MetricTile),
        string.Empty,
        propertyChanged: OnTextChanged);

    public static readonly BindableProperty CaptionProperty = BindableProperty.Create(
        nameof(Caption),
        typeof(string),
        typeof(MetricTile),
        string.Empty,
        propertyChanged: OnTextChanged);

    public static readonly BindableProperty AccentProperty = BindableProperty.Create(
        nameof(Accent),
        typeof(Color),
        typeof(MetricTile),
        null,
        propertyChanged: OnAccentChanged);

    public MetricTile()
    {
        InitializeComponent();
        UpdateSemanticDescription();
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public Color? Accent
    {
        get => (Color?)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private static void OnTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((MetricTile)bindable).UpdateSemanticDescription();
    }

    private static void OnAccentChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((MetricTile)bindable).ApplyAccent();
    }

    private void ApplyAccent()
    {
        if (Accent is null)
        {
            ValueLabel.ClearValue(Label.TextColorProperty);
            UnitLabel.ClearValue(Label.TextColorProperty);
            return;
        }

        ValueLabel.TextColor = Accent;
        UnitLabel.TextColor = Accent;
    }

    private void UpdateSemanticDescription()
    {
        var valueWithUnit = string.Join(" ", new[] { Value, ExpandUnit(Unit) }.Where(text => !string.IsNullOrWhiteSpace(text)));
        var parts = new[] { Caption, valueWithUnit }.Where(text => !string.IsNullOrWhiteSpace(text));
        SemanticProperties.SetDescription(TileChrome, string.Join(", ", parts));
    }

    private static string ExpandUnit(string unit)
    {
        return unit.Trim().ToLowerInvariant() switch
        {
            "kg" => "kilograms",
            "g" => "grams",
            "lb" or "lbs" => "pounds",
            "kcal" => "kilocalories",
            "km" => "kilometers",
            "m" => "meters",
            "min" or "mins" => "minutes",
            "sec" or "secs" or "s" => "seconds",
            "hr" or "hrs" or "h" => "hours",
            "%" => "percent",
            _ => unit,
        };
    }
}
