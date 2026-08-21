namespace Forge.Core.Adaptive;

/// <summary>
/// The arithmetic behind Forge's tablet layouts, kept free of any UI framework so it can be
/// tested directly.
/// </summary>
/// <remarks>
/// <para>
/// Every method takes its thresholds as arguments rather than reading a constant. The numbers
/// live in <c>Resources/Styles/ForgeTokens.xaml</c> alongside the rest of the design tokens, and
/// are handed to this class by the view layer. That is what stops the breakpoints drifting apart
/// between the styles and the code that acts on them.
/// </para>
/// <para>
/// Widths are in device-independent units. A returned width of <see cref="Unconstrained"/> means
/// "impose nothing", which maps onto MAUI's convention that a request of -1 is no request at all.
/// Returning it rather than the measured width matters: it is what keeps a phone layout byte for
/// byte the layout it was before any of this existed.
/// </para>
/// </remarks>
public static class AdaptiveLayoutMetrics
{
    /// <summary>The value used to mean "do not constrain this dimension".</summary>
    public const double Unconstrained = -1d;

    /// <summary>
    /// Classifies a window width.
    /// </summary>
    /// <param name="width">The available width in device-independent units.</param>
    /// <param name="mediumBreakpoint">The width at which <see cref="LayoutSizeClass.Medium"/> starts.</param>
    /// <param name="expandedBreakpoint">The width at which <see cref="LayoutSizeClass.Expanded"/> starts.</param>
    /// <returns>The size class for that width.</returns>
    public static LayoutSizeClass ResolveSizeClass(double width, double mediumBreakpoint, double expandedBreakpoint)
    {
        // Before the first layout pass a MAUI view reports -1. Treating that as Compact means a
        // page renders as a phone for one frame and then widens, which is far less jarring than
        // rendering a two-pane split and collapsing it.
        if (!IsUsable(width))
        {
            return LayoutSizeClass.Compact;
        }

        if (IsUsable(expandedBreakpoint) && width >= expandedBreakpoint)
        {
            return LayoutSizeClass.Expanded;
        }

        return IsUsable(mediumBreakpoint) && width >= mediumBreakpoint
            ? LayoutSizeClass.Medium
            : LayoutSizeClass.Compact;
    }

    /// <summary>
    /// Decides whether a surface can show two panes side by side.
    /// </summary>
    /// <param name="width">The available width.</param>
    /// <param name="height">The available height.</param>
    /// <param name="minimumWidth">The narrowest width at which two panes still earn their keep.</param>
    /// <param name="minimumHeight">The shortest height at which two panes still earn their keep.</param>
    /// <returns><see langword="true"/> when both panes should be shown.</returns>
    /// <remarks>
    /// Height is part of the test on purpose. A phone in landscape is wide enough for two columns
    /// and nowhere near tall enough for them: roughly 410 points of height, most of it eaten by
    /// the navigation bar and the keyboard. Gating on width alone would quietly regress the phone
    /// experience the moment somebody turned their handset sideways.
    /// </remarks>
    public static bool SupportsTwoPanes(double width, double height, double minimumWidth, double minimumHeight)
        => IsUsable(width)
           && IsUsable(height)
           && width >= minimumWidth
           && height >= minimumHeight;

    /// <summary>
    /// Caps a column of content at a comfortable measure.
    /// </summary>
    /// <param name="availableWidth">The width the content could occupy.</param>
    /// <param name="maximumWidth">The widest the content should ever be.</param>
    /// <returns>The width to request, or <see cref="Unconstrained"/> to leave it alone.</returns>
    /// <remarks>
    /// A line of text stops being readable somewhere past about ninety characters: the eye loses
    /// the start of the next line on the return sweep. On a 13-inch iPad an uncapped paragraph is
    /// roughly twice that. The cap is only applied when it would actually bite, so narrow windows
    /// are returned untouched.
    /// </remarks>
    public static double ResolveContentWidth(double availableWidth, double maximumWidth)
    {
        if (!IsUsable(availableWidth) || !IsUsable(maximumWidth))
        {
            return Unconstrained;
        }

        return availableWidth <= maximumWidth ? Unconstrained : maximumWidth;
    }

    /// <summary>
    /// Works out how many equal columns of cards fit.
    /// </summary>
    /// <param name="availableWidth">The width available to the grid.</param>
    /// <param name="minimumColumnWidth">The narrowest a single column may be.</param>
    /// <param name="maximumColumns">The most columns to use however wide the window gets.</param>
    /// <returns>A column count of at least one.</returns>
    /// <remarks>
    /// Capped rather than unbounded because a wall of six columns is not a better dashboard, it
    /// is a spreadsheet. Two or three reads as a designed layout; more reads as an accident.
    /// </remarks>
    public static int ResolveColumnCount(double availableWidth, double minimumColumnWidth, int maximumColumns)
    {
        if (!IsUsable(availableWidth) || !IsUsable(minimumColumnWidth) || maximumColumns < 1)
        {
            return 1;
        }

        var fits = (int)Math.Floor(availableWidth / minimumColumnWidth);
        return Math.Clamp(fits, 1, maximumColumns);
    }

    /// <summary>
    /// Sizes the list side of a list-and-detail split.
    /// </summary>
    /// <param name="availableWidth">The width available to both panes and the gap between them.</param>
    /// <param name="preferredWidth">The width the list pane would like.</param>
    /// <param name="minimumDetailWidth">The narrowest the detail pane may be squeezed to.</param>
    /// <param name="gutter">The gap between the two panes.</param>
    /// <returns>The width for the list pane, or <see cref="Unconstrained"/> when a split does not fit.</returns>
    /// <remarks>
    /// The list pane is fixed and the detail pane takes the remainder, which is the arrangement
    /// every platform's own split view uses. The alternative - both panes proportional - makes
    /// the list grow to absurd widths on a 13-inch screen for no benefit, because a list row
    /// carries the same information at 320 points as it does at 700.
    /// </remarks>
    public static double ResolveListPaneWidth(
        double availableWidth,
        double preferredWidth,
        double minimumDetailWidth,
        double gutter)
    {
        if (!IsUsable(availableWidth) || !IsUsable(preferredWidth))
        {
            return Unconstrained;
        }

        var spareForList = availableWidth - Math.Max(0d, gutter) - Math.Max(0d, minimumDetailWidth);
        if (spareForList <= 0d)
        {
            return Unconstrained;
        }

        return Math.Min(preferredWidth, spareForList);
    }

    private static bool IsUsable(double value) => value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
}
