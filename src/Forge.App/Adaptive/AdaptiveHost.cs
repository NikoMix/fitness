using Forge.Core.Adaptive;

namespace Forge.App.Adaptive;

/// <summary>
/// Wraps a page's content and publishes the layout decisions that depend on how much room the
/// page actually has.
/// </summary>
/// <remarks>
/// <para>
/// Forge adapts on <em>measured width</em> rather than on <c>OnIdiom</c>. A device idiom is fixed
/// for the life of the process, and an iPad is not one width: full screen, a two-thirds Split
/// View, a half Split View and Slide Over are four different layouts on the same device, and the
/// user changes between them by dragging. Anything keyed on "is this a tablet" is wrong in three
/// of those four states. Rotation has the same problem.
/// </para>
/// <para>
/// The one thing that is genuinely fixed per device is the type scale, and that is handled
/// separately by <c>OnIdiom</c> inside <c>ForgeStyles.xaml</c>, where it costs no per-page work.
/// </para>
/// <para>
/// The host measures itself and nothing else. It deliberately does not reposition or resize its
/// child, so a page's layout is always readable from the page's own XAML rather than hidden in a
/// container's arrange pass. Pages bind to the properties below:
/// </para>
/// <code language="xaml">
/// &lt;adaptive:AdaptiveHost x:Name="Adaptive"&gt;
///     &lt;Grid ColumnDefinitions="{Binding Source={x:Reference Adaptive}, Path=SplitColumns}"&gt;
///         &lt;!-- list pane --&gt;
///         &lt;!-- detail pane, IsVisible bound to IsSplit --&gt;
///     &lt;/Grid&gt;
/// &lt;/adaptive:AdaptiveHost&gt;
/// </code>
/// <para>
/// The tuning values are supplied by the implicit style in <c>ForgeAdaptive.xaml</c>, which reads
/// them from <c>ForgeTokens.xaml</c>. The defaults here only exist so the control still behaves
/// if it is ever constructed outside the application's resource scope.
/// </para>
/// </remarks>
public sealed class AdaptiveHost : ContentView
{
    /// <summary>Identifies the <see cref="MediumBreakpoint"/> property.</summary>
    public static readonly BindableProperty MediumBreakpointProperty = Tuning(nameof(MediumBreakpoint), 600d);

    /// <summary>Identifies the <see cref="ExpandedBreakpoint"/> property.</summary>
    public static readonly BindableProperty ExpandedBreakpointProperty = Tuning(nameof(ExpandedBreakpoint), 840d);

    /// <summary>Identifies the <see cref="SplitMinimumWidth"/> property.</summary>
    public static readonly BindableProperty SplitMinimumWidthProperty = Tuning(nameof(SplitMinimumWidth), 740d);

    /// <summary>Identifies the <see cref="SplitMinimumHeight"/> property.</summary>
    public static readonly BindableProperty SplitMinimumHeightProperty = Tuning(nameof(SplitMinimumHeight), 520d);

    /// <summary>Identifies the <see cref="ReadingMeasure"/> property.</summary>
    public static readonly BindableProperty ReadingMeasureProperty = Tuning(nameof(ReadingMeasure), 680d);

    /// <summary>Identifies the <see cref="ContentMeasure"/> property.</summary>
    public static readonly BindableProperty ContentMeasureProperty = Tuning(nameof(ContentMeasure), 1040d);

    /// <summary>Identifies the <see cref="PreferredListPaneWidth"/> property.</summary>
    public static readonly BindableProperty PreferredListPaneWidthProperty = Tuning(nameof(PreferredListPaneWidth), 360d);

    /// <summary>Identifies the <see cref="MinimumDetailPaneWidth"/> property.</summary>
    public static readonly BindableProperty MinimumDetailPaneWidthProperty = Tuning(nameof(MinimumDetailPaneWidth), 420d);

    /// <summary>Identifies the <see cref="PaneGutter"/> property.</summary>
    public static readonly BindableProperty PaneGutterProperty = Tuning(nameof(PaneGutter), 24d);

    /// <summary>Identifies the <see cref="MinimumCardWidth"/> property.</summary>
    public static readonly BindableProperty MinimumCardWidthProperty = Tuning(nameof(MinimumCardWidth), 340d);

    /// <summary>Identifies the <see cref="MaximumCardColumns"/> property.</summary>
    public static readonly BindableProperty MaximumCardColumnsProperty = BindableProperty.Create(
        nameof(MaximumCardColumns),
        typeof(int),
        typeof(AdaptiveHost),
        3,
        propertyChanged: OnTuningChanged);

    private LayoutSizeClass sizeClass = LayoutSizeClass.Compact;
    private bool isSplit;
    private bool isLandscape;
    private double readingWidth = AdaptiveLayoutMetrics.Unconstrained;
    private double contentWidth = AdaptiveLayoutMetrics.Unconstrained;
    private double listPaneWidth = AdaptiveLayoutMetrics.Unconstrained;
    private double paneSpacing;
    private int cardColumns = 1;
    private ColumnDefinitionCollection splitColumns = StackedColumns();

    /// <summary>The width at or above which the layout is at least <see cref="LayoutSizeClass.Medium"/>.</summary>
    public double MediumBreakpoint
    {
        get => (double)GetValue(MediumBreakpointProperty);
        set => SetValue(MediumBreakpointProperty, value);
    }

    /// <summary>The width at or above which the layout is <see cref="LayoutSizeClass.Expanded"/>.</summary>
    public double ExpandedBreakpoint
    {
        get => (double)GetValue(ExpandedBreakpointProperty);
        set => SetValue(ExpandedBreakpointProperty, value);
    }

    /// <summary>The narrowest width that still justifies two panes side by side.</summary>
    public double SplitMinimumWidth
    {
        get => (double)GetValue(SplitMinimumWidthProperty);
        set => SetValue(SplitMinimumWidthProperty, value);
    }

    /// <summary>The shortest height that still justifies two panes side by side.</summary>
    public double SplitMinimumHeight
    {
        get => (double)GetValue(SplitMinimumHeightProperty);
        set => SetValue(SplitMinimumHeightProperty, value);
    }

    /// <summary>The widest a column of prose should ever be.</summary>
    public double ReadingMeasure
    {
        get => (double)GetValue(ReadingMeasureProperty);
        set => SetValue(ReadingMeasureProperty, value);
    }

    /// <summary>The widest a single column of cards and controls should ever be.</summary>
    public double ContentMeasure
    {
        get => (double)GetValue(ContentMeasureProperty);
        set => SetValue(ContentMeasureProperty, value);
    }

    /// <summary>The width the list pane of a split layout would like.</summary>
    public double PreferredListPaneWidth
    {
        get => (double)GetValue(PreferredListPaneWidthProperty);
        set => SetValue(PreferredListPaneWidthProperty, value);
    }

    /// <summary>The narrowest the detail pane of a split layout may be squeezed to.</summary>
    public double MinimumDetailPaneWidth
    {
        get => (double)GetValue(MinimumDetailPaneWidthProperty);
        set => SetValue(MinimumDetailPaneWidthProperty, value);
    }

    /// <summary>The gap between the two panes of a split layout.</summary>
    public double PaneGutter
    {
        get => (double)GetValue(PaneGutterProperty);
        set => SetValue(PaneGutterProperty, value);
    }

    /// <summary>The narrowest a single card in a wrapping grid may be.</summary>
    public double MinimumCardWidth
    {
        get => (double)GetValue(MinimumCardWidthProperty);
        set => SetValue(MinimumCardWidthProperty, value);
    }

    /// <summary>The most card columns to use however wide the window gets.</summary>
    public int MaximumCardColumns
    {
        get => (int)GetValue(MaximumCardColumnsProperty);
        set => SetValue(MaximumCardColumnsProperty, value);
    }

    /// <summary>The size class of the current window.</summary>
    public LayoutSizeClass SizeClass
    {
        get => sizeClass;
        private set => Publish(ref sizeClass, value);
    }

    /// <summary>Whether the window is phone sized.</summary>
    public bool IsCompact => SizeClass == LayoutSizeClass.Compact;

    /// <summary>Whether the window has more room than a phone.</summary>
    public bool IsMediumOrWider => SizeClass != LayoutSizeClass.Compact;

    /// <summary>Whether the window is wide enough for genuinely multi-column content.</summary>
    public bool IsExpanded => SizeClass == LayoutSizeClass.Expanded;

    /// <summary>Whether a list and a detail pane should be shown at the same time.</summary>
    public bool IsSplit
    {
        get => isSplit;
        private set => Publish(ref isSplit, value);
    }

    /// <summary>Whether the panes are stacked, so a detail belongs on its own page.</summary>
    public bool IsStacked => !IsSplit;

    /// <summary>Whether the window is wider than it is tall.</summary>
    public bool IsLandscape
    {
        get => isLandscape;
        private set => Publish(ref isLandscape, value);
    }

    /// <summary>The width to request for a column of prose, or -1 to leave it alone.</summary>
    public double ReadingWidth
    {
        get => readingWidth;
        private set => Publish(ref readingWidth, value);
    }

    /// <summary>The width to request for a single column of page content, or -1 to leave it alone.</summary>
    public double ContentWidth
    {
        get => contentWidth;
        private set => Publish(ref contentWidth, value);
    }

    /// <summary>The width of the list pane in a split layout, or -1 when there is no split.</summary>
    public double ListPaneWidth
    {
        get => listPaneWidth;
        private set => Publish(ref listPaneWidth, value);
    }

    /// <summary>
    /// The gap between the two panes: the gutter when split, and nothing at all when stacked.
    /// </summary>
    /// <remarks>
    /// A grid applies its column spacing between every pair of columns, including a pair where one
    /// is empty. Leaving a fixed spacing in place would put a gutter's worth of dead space down the
    /// right-hand edge of every phone screen, which is exactly the kind of quiet regression this
    /// work is not allowed to introduce.
    /// </remarks>
    public double PaneSpacing
    {
        get => paneSpacing;
        private set => Publish(ref paneSpacing, value);
    }

    /// <summary>
    /// The grid column the detail pane occupies: the second column when split, and the first when
    /// stacked, where it covers the list instead of sitting beside it.
    /// </summary>
    public int DetailPaneColumn => IsSplit ? 1 : 0;

    /// <summary>How many columns the detail pane spans: one when split, both when stacked.</summary>
    public int DetailPaneColumnSpan => IsSplit ? 1 : 2;

    /// <summary>How many equal columns of cards fit.</summary>
    public int CardColumns
    {
        get => cardColumns;
        private set => Publish(ref cardColumns, value);
    }

    /// <summary>
    /// Column definitions for a list-and-detail grid: a full-width list and a zero-width second
    /// column when stacked, a fixed list column and a star detail column when split.
    /// </summary>
    /// <remarks>
    /// Bound to a <c>Grid.ColumnDefinitions</c>. There are always two columns so that a child
    /// placed in column one is never out of range, and the unused column is collapsed to zero
    /// rather than left as a star, which is what stops the list floating in half a screen while a
    /// hidden detail pane reserves the rest.
    /// </remarks>
    public ColumnDefinitionCollection SplitColumns
    {
        get => splitColumns;
        private set => Publish(ref splitColumns, value);
    }

    /// <inheritdoc />
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        Recalculate(width, height);
    }

    private static BindableProperty Tuning(string name, double defaultValue)
        => BindableProperty.Create(name, typeof(double), typeof(AdaptiveHost), defaultValue, propertyChanged: OnTuningChanged);

    private static void OnTuningChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AdaptiveHost host)
        {
            host.Recalculate(host.Width, host.Height);
        }
    }

    private static ColumnDefinitionCollection StackedColumns()
        => [new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(0d, GridUnitType.Absolute))];

    private void Recalculate(double width, double height)
    {
        var resolvedClass = AdaptiveLayoutMetrics.ResolveSizeClass(width, MediumBreakpoint, ExpandedBreakpoint);
        var split = AdaptiveLayoutMetrics.SupportsTwoPanes(width, height, SplitMinimumWidth, SplitMinimumHeight);
        var list = split
            ? AdaptiveLayoutMetrics.ResolveListPaneWidth(width, PreferredListPaneWidth, MinimumDetailPaneWidth, PaneGutter)
            : AdaptiveLayoutMetrics.Unconstrained;

        // A split that cannot honour the minimum detail width is not a split. Falling back keeps
        // the arithmetic and the visibility flags telling the same story.
        if (list <= 0d)
        {
            split = false;
        }

        var previousClass = SizeClass;
        var previousSplit = IsSplit;

        SizeClass = resolvedClass;
        IsSplit = split;
        IsLandscape = width > height;
        ReadingWidth = AdaptiveLayoutMetrics.ResolveContentWidth(width, ReadingMeasure);
        ContentWidth = AdaptiveLayoutMetrics.ResolveContentWidth(width, ContentMeasure);
        ListPaneWidth = list;
        PaneSpacing = split ? PaneGutter : 0d;
        CardColumns = AdaptiveLayoutMetrics.ResolveColumnCount(width, MinimumCardWidth, MaximumCardColumns);

        if (previousSplit != split || SplitColumns.Count != 2)
        {
            SplitColumns = split
                ? [new ColumnDefinition(new GridLength(list, GridUnitType.Absolute)), new ColumnDefinition(GridLength.Star)]
                : StackedColumns();
        }
        else if (split)
        {
            // Same layout, new width: resize in place so the grid does not rebuild its children.
            SplitColumns[0].Width = new GridLength(list, GridUnitType.Absolute);
        }

        if (previousClass != resolvedClass)
        {
            OnPropertyChanged(nameof(IsCompact));
            OnPropertyChanged(nameof(IsMediumOrWider));
            OnPropertyChanged(nameof(IsExpanded));
        }

        if (previousSplit != split)
        {
            OnPropertyChanged(nameof(IsStacked));
            OnPropertyChanged(nameof(DetailPaneColumn));
            OnPropertyChanged(nameof(DetailPaneColumnSpan));
        }
    }

    private void Publish<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }
}
