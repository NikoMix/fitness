namespace Forge.App.Controls;

/// <summary>
/// Displays a safety refusal, a warning, or step guidance, with every part optional.
/// </summary>
/// <remarks>
/// The whole panel is announced as one accessible item. A screen-reader user hearing a refusal
/// needs the headline, the reasons, the signpost and the reassurance as one statement; four
/// separate focus stops invites stopping after the first one.
/// </remarks>
public partial class AdvisoryPanel : ContentView
{
    /// <summary>Identifies the <see cref="Headline"/> bindable property.</summary>
    public static readonly BindableProperty HeadlineProperty = BindableProperty.Create(
        nameof(Headline),
        typeof(string),
        typeof(AdvisoryPanel),
        string.Empty,
        propertyChanged: OnContentChanged);

    /// <summary>Identifies the <see cref="Message"/> bindable property.</summary>
    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message),
        typeof(string),
        typeof(AdvisoryPanel),
        string.Empty,
        propertyChanged: OnContentChanged);

    /// <summary>Identifies the <see cref="Signpost"/> bindable property.</summary>
    public static readonly BindableProperty SignpostProperty = BindableProperty.Create(
        nameof(Signpost),
        typeof(string),
        typeof(AdvisoryPanel),
        string.Empty,
        propertyChanged: OnContentChanged);

    /// <summary>Identifies the <see cref="Reassurance"/> bindable property.</summary>
    public static readonly BindableProperty ReassuranceProperty = BindableProperty.Create(
        nameof(Reassurance),
        typeof(string),
        typeof(AdvisoryPanel),
        string.Empty,
        propertyChanged: OnContentChanged);

    /// <summary>Identifies the <see cref="IsBlocking"/> bindable property.</summary>
    public static readonly BindableProperty IsBlockingProperty = BindableProperty.Create(
        nameof(IsBlocking),
        typeof(bool),
        typeof(AdvisoryPanel),
        false,
        propertyChanged: OnContentChanged);

    /// <summary>Initialises the control.</summary>
    public AdvisoryPanel()
    {
        InitializeComponent();
        UpdatePresentation();
    }

    /// <summary>A short heading describing the outcome.</summary>
    public string Headline
    {
        get => (string)GetValue(HeadlineProperty);
        set => SetValue(HeadlineProperty, value);
    }

    /// <summary>The full reasoning, one paragraph per reason.</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Where to get real help when the guardrail is a health matter.</summary>
    public string Signpost
    {
        get => (string)GetValue(SignpostProperty);
        set => SetValue(SignpostProperty, value);
    }

    /// <summary>What happened to the user's input. Never left empty on a refusal.</summary>
    public string Reassurance
    {
        get => (string)GetValue(ReassuranceProperty);
        set => SetValue(ReassuranceProperty, value);
    }

    /// <summary>Whether the advisory prevents the user from continuing.</summary>
    public bool IsBlocking
    {
        get => (bool)GetValue(IsBlockingProperty);
        set => SetValue(IsBlockingProperty, value);
    }

    private static void OnContentChanged(BindableObject bindable, object oldValue, object newValue)
        => ((AdvisoryPanel)bindable).UpdatePresentation();

    private void UpdatePresentation()
    {
        HeadlineLabel.IsVisible = !string.IsNullOrWhiteSpace(Headline);
        MessageLabel.IsVisible = !string.IsNullOrWhiteSpace(Message);
        SignpostLabel.IsVisible = !string.IsNullOrWhiteSpace(Signpost);
        ReassuranceLabel.IsVisible = !string.IsNullOrWhiteSpace(Reassurance);

        var styleKey = IsBlocking ? "AdvisoryCard" : "Card";
        if (Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(styleKey, out var style) == true
            && style is Style panelStyle)
        {
            PanelChrome.Style = panelStyle;
        }

        SemanticProperties.SetDescription(
            PanelChrome,
            string.Join(
                ". ",
                new[] { Headline, Message, Signpost, Reassurance }.Where(text => !string.IsNullOrWhiteSpace(text))));
    }
}
