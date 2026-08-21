using System.Windows.Input;

namespace Forge.App.Controls;

public partial class SectionHeader : ContentView
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title),
        typeof(string),
        typeof(SectionHeader),
        string.Empty,
        propertyChanged: OnTextChanged);

    public static readonly BindableProperty ActionTextProperty = BindableProperty.Create(
        nameof(ActionText),
        typeof(string),
        typeof(SectionHeader),
        string.Empty,
        propertyChanged: OnActionChanged);

    public static readonly BindableProperty ActionCommandProperty = BindableProperty.Create(
        nameof(ActionCommand),
        typeof(ICommand),
        typeof(SectionHeader),
        null,
        propertyChanged: OnActionChanged);

    public SectionHeader()
    {
        InitializeComponent();
        UpdateActionVisibility();
        UpdateSemanticDescription();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
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
        ((SectionHeader)bindable).UpdateSemanticDescription();
    }

    private static void OnActionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var sectionHeader = (SectionHeader)bindable;
        sectionHeader.UpdateActionVisibility();
        sectionHeader.UpdateSemanticDescription();
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
        SemanticProperties.SetDescription(this, Title);
    }
}
