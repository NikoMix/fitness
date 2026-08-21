namespace Forge.App.Controls;

public partial class StatRow : ContentView
{
    public static readonly BindableProperty LabelProperty = BindableProperty.Create(
        nameof(Label),
        typeof(string),
        typeof(StatRow),
        string.Empty,
        propertyChanged: OnTextChanged);

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value),
        typeof(string),
        typeof(StatRow),
        string.Empty,
        propertyChanged: OnTextChanged);

    public StatRow()
    {
        InitializeComponent();
        UpdateSemanticDescription();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static void OnTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((StatRow)bindable).UpdateSemanticDescription();
    }

    private void UpdateSemanticDescription()
    {
        var parts = new[] { Label, Value }.Where(text => !string.IsNullOrWhiteSpace(text));
        SemanticProperties.SetDescription(this, string.Join(", ", parts));
    }
}
