namespace Forge.App.Controls;

/// <summary>
/// The standard Forge card surface: an optional title and subtitle above arbitrary content.
/// </summary>
/// <remarks>
/// The content a caller writes between the tags is moved into an inner host so the card can draw
/// its own header above it. The host is a plain <see cref="ContentView"/> rather than a
/// <see cref="ContentPresenter"/> - see the comment in the XAML for why that distinction decides
/// whether every binding inside a card works or silently resolves against nothing.
/// </remarks>
public partial class ForgeCard : ContentView
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title),
        typeof(string),
        typeof(ForgeCard),
        string.Empty,
        propertyChanged: OnHeaderChanged);

    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(
        nameof(Subtitle),
        typeof(string),
        typeof(ForgeCard),
        string.Empty,
        propertyChanged: OnHeaderChanged);

    private View? chrome;
    private bool restoringChrome;

    public ForgeCard()
    {
        InitializeComponent();
        chrome = Content;
        UpdateHeaderVisibility();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName != nameof(Content) || restoringChrome || chrome is null || ReferenceEquals(Content, chrome))
        {
            return;
        }

        // Detach the caller's content from this card before handing it to the body host. Assigning
        // it while it is still this card's Content makes MAUI log "already a child of ... Remove
        // before adding" twice on every card on every launch, which is a lot of noise in the one
        // diagnostic channel a released build has. Order matters and nothing else does: the body
        // still ends up inside BodyPresenter, which still sits inside the chrome, so the binding
        // context inherits exactly as before.
        var body = Content;
        restoringChrome = true;
        Content = chrome;
        restoringChrome = false;
        BodyPresenter.Content = body;
    }

    private static void OnHeaderChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((ForgeCard)bindable).UpdateHeaderVisibility();
    }

    private void UpdateHeaderVisibility()
    {
        var hasTitle = !string.IsNullOrWhiteSpace(Title);
        var hasSubtitle = !string.IsNullOrWhiteSpace(Subtitle);

        TitleLabel.IsVisible = hasTitle;
        SubtitleLabel.IsVisible = hasSubtitle;
        HeaderLayout.IsVisible = hasTitle || hasSubtitle;
        SemanticProperties.SetDescription(CardChrome, string.Join(", ", new[] { Title, Subtitle }.Where(text => !string.IsNullOrWhiteSpace(text))));
    }
}
