namespace Forge.App.Controls;

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

        BodyPresenter.Content = Content;
        restoringChrome = true;
        Content = chrome;
        restoringChrome = false;
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
