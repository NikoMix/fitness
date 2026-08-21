using System.Windows.Input;

namespace Forge.App.Controls;

public partial class EmptyState : ContentView
{
    public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
        nameof(Glyph),
        typeof(string),
        typeof(EmptyState),
        "✦",
        propertyChanged: OnTextChanged);

    public static readonly BindableProperty HeadlineProperty = BindableProperty.Create(
        nameof(Headline),
        typeof(string),
        typeof(EmptyState),
        string.Empty,
        propertyChanged: OnTextChanged);

    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message),
        typeof(string),
        typeof(EmptyState),
        string.Empty,
        propertyChanged: OnTextChanged);

    public static readonly BindableProperty ActionTextProperty = BindableProperty.Create(
        nameof(ActionText),
        typeof(string),
        typeof(EmptyState),
        string.Empty,
        propertyChanged: OnActionChanged);

    public static readonly BindableProperty ActionCommandProperty = BindableProperty.Create(
        nameof(ActionCommand),
        typeof(ICommand),
        typeof(EmptyState),
        null,
        propertyChanged: OnActionChanged);

    public EmptyState()
    {
        InitializeComponent();
        UpdateActionVisibility();
        UpdateSemanticDescription();
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Headline
    {
        get => (string)GetValue(HeadlineProperty);
        set => SetValue(HeadlineProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    private static void OnTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((EmptyState)bindable).UpdateSemanticDescription();
    }

    private static void OnActionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var emptyState = (EmptyState)bindable;
        emptyState.UpdateActionVisibility();
        emptyState.UpdateSemanticDescription();
    }

    private void UpdateActionVisibility()
    {
        var hasAction = !string.IsNullOrWhiteSpace(ActionText) && ActionCommand is not null;
        ActionButton.IsVisible = hasAction;
        SemanticProperties.SetDescription(ActionButton, hasAction ? ActionText : string.Empty);
        SemanticProperties.SetHint(ActionButton, hasAction ? $"Activates {ActionText}" : string.Empty);
    }

    private void UpdateSemanticDescription()
    {
        var parts = new[] { Headline, Message }.Where(text => !string.IsNullOrWhiteSpace(text));
        SemanticProperties.SetDescription(StateChrome, string.Join(", ", parts));
    }
}
