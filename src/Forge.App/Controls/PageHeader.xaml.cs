using System.Windows.Input;

namespace Forge.App.Controls;

/// <summary>
/// The single in-page title block: optional eyebrow, title, subtitle, trailing action and leading
/// back affordance.
/// </summary>
/// <remarks>
/// Every optional element hides itself when it has nothing to show, so a page can bind only what
/// it needs without leaving empty rows behind. Both buttons carry an explicit
/// <see cref="SemanticProperties.DescriptionProperty"/>: DevExpress buttons surface to Android's
/// accessibility tree as non-focusable text without one, which makes them unreachable for a screen
/// reader.
/// </remarks>
public partial class PageHeader : ContentView
{
    /// <summary>Identifies the <see cref="Eyebrow"/> bindable property.</summary>
    public static readonly BindableProperty EyebrowProperty = BindableProperty.Create(
        nameof(Eyebrow),
        typeof(string),
        typeof(PageHeader),
        string.Empty,
        propertyChanged: OnTextChanged);

    /// <summary>Identifies the <see cref="Title"/> bindable property.</summary>
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title),
        typeof(string),
        typeof(PageHeader),
        string.Empty,
        propertyChanged: OnTextChanged);

    /// <summary>Identifies the <see cref="Subtitle"/> bindable property.</summary>
    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(
        nameof(Subtitle),
        typeof(string),
        typeof(PageHeader),
        string.Empty,
        propertyChanged: OnTextChanged);

    /// <summary>Identifies the <see cref="ActionText"/> bindable property.</summary>
    public static readonly BindableProperty ActionTextProperty = BindableProperty.Create(
        nameof(ActionText),
        typeof(string),
        typeof(PageHeader),
        string.Empty,
        propertyChanged: OnActionChanged);

    /// <summary>Identifies the <see cref="ActionCommand"/> bindable property.</summary>
    public static readonly BindableProperty ActionCommandProperty = BindableProperty.Create(
        nameof(ActionCommand),
        typeof(ICommand),
        typeof(PageHeader),
        null,
        propertyChanged: OnActionChanged);

    /// <summary>Identifies the <see cref="BackText"/> bindable property.</summary>
    public static readonly BindableProperty BackTextProperty = BindableProperty.Create(
        nameof(BackText),
        typeof(string),
        typeof(PageHeader),
        "Back",
        propertyChanged: OnActionChanged);

    /// <summary>Identifies the <see cref="BackCommand"/> bindable property.</summary>
    public static readonly BindableProperty BackCommandProperty = BindableProperty.Create(
        nameof(BackCommand),
        typeof(ICommand),
        typeof(PageHeader),
        null,
        propertyChanged: OnActionChanged);

    /// <summary>Initialises the control.</summary>
    public PageHeader()
    {
        InitializeComponent();
        UpdateVisibility();
    }

    /// <summary>Small line above the title, such as a date or a step counter.</summary>
    public string Eyebrow
    {
        get => (string)GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    /// <summary>The page title. This is the only title the page shows.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>One supporting sentence under the title.</summary>
    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>Label for an optional trailing action.</summary>
    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    /// <summary>Command for the optional trailing action.</summary>
    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    /// <summary>Label for an optional leading back affordance.</summary>
    public string BackText
    {
        get => (string)GetValue(BackTextProperty);
        set => SetValue(BackTextProperty, value);
    }

    /// <summary>
    /// Command for the optional leading back affordance.
    /// </summary>
    /// <remarks>
    /// Used by flows that hide the Shell navigation bar because "back" means something other than
    /// popping the page - a wizard step, for example.
    /// </remarks>
    public ICommand? BackCommand
    {
        get => (ICommand?)GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    private static void OnTextChanged(BindableObject bindable, object oldValue, object newValue)
        => ((PageHeader)bindable).UpdateVisibility();

    private static void OnActionChanged(BindableObject bindable, object oldValue, object newValue)
        => ((PageHeader)bindable).UpdateVisibility();

    private void UpdateVisibility()
    {
        EyebrowLabel.IsVisible = !string.IsNullOrWhiteSpace(Eyebrow);
        TitleLabel.IsVisible = !string.IsNullOrWhiteSpace(Title);
        SubtitleLabel.IsVisible = !string.IsNullOrWhiteSpace(Subtitle);

        var hasAction = !string.IsNullOrWhiteSpace(ActionText) && ActionCommand is not null;
        ActionButton.IsVisible = hasAction;
        SemanticProperties.SetDescription(ActionButton, hasAction ? ActionText : string.Empty);

        var hasBack = !string.IsNullOrWhiteSpace(BackText) && BackCommand is not null;
        BackButton.IsVisible = hasBack;
        SemanticProperties.SetDescription(BackButton, hasBack ? BackText : string.Empty);
        SemanticProperties.SetHint(BackButton, hasBack ? "Returns to the previous step" : string.Empty);

        SemanticProperties.SetDescription(
            HeaderLayout,
            string.Join(", ", new[] { Eyebrow, Title, Subtitle }.Where(text => !string.IsNullOrWhiteSpace(text))));
    }
}
