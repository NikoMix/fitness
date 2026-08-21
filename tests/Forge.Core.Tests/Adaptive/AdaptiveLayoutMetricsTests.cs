using Forge.Core.Adaptive;
using Shouldly;

namespace Forge.Core.Tests.Adaptive;

/// <summary>
/// Guards the breakpoint arithmetic against the device sizes Forge actually ships on.
/// </summary>
/// <remarks>
/// The widths in these tests are the real point sizes of the devices named beside them, so a
/// change to a breakpoint that would silently reclassify an iPad shows up here rather than on a
/// reviewer's desk.
/// </remarks>
public sealed class AdaptiveLayoutMetricsTests
{
    private const double MediumBreakpoint = 600d;
    private const double ExpandedBreakpoint = 840d;
    private const double SplitMinimumWidth = 740d;
    private const double SplitMinimumHeight = 520d;

    [Theory]
    [InlineData(360d)]   // Pixel 8, portrait
    [InlineData(412d)]   // Pixel 8 Pro, portrait
    [InlineData(430d)]   // iPhone 16 Pro Max, portrait
    [InlineData(320d)]   // iPad Slide Over
    public void Phone_widths_stay_compact(double width)
        => AdaptiveLayoutMetrics.ResolveSizeClass(width, MediumBreakpoint, ExpandedBreakpoint)
            .ShouldBe(LayoutSizeClass.Compact);

    [Theory]
    [InlineData(744d)]   // iPad mini, portrait
    [InlineData(639d)]   // iPad 11-inch, half of a landscape Split View
    public void Small_tablet_widths_are_medium(double width)
        => AdaptiveLayoutMetrics.ResolveSizeClass(width, MediumBreakpoint, ExpandedBreakpoint)
            .ShouldBe(LayoutSizeClass.Medium);

    [Theory]
    [InlineData(1024d)]  // iPad 13-inch, portrait
    [InlineData(1366d)]  // iPad 13-inch, landscape
    [InlineData(1194d)]  // iPad 11-inch, landscape
    [InlineData(1280d)]  // Pixel Tablet, landscape
    public void Large_tablet_widths_are_expanded(double width)
        => AdaptiveLayoutMetrics.ResolveSizeClass(width, MediumBreakpoint, ExpandedBreakpoint)
            .ShouldBe(LayoutSizeClass.Expanded);

    [Fact]
    public void Unmeasured_width_is_compact()
        => AdaptiveLayoutMetrics.ResolveSizeClass(-1d, MediumBreakpoint, ExpandedBreakpoint)
            .ShouldBe(LayoutSizeClass.Compact);

    [Theory]
    [InlineData(412d, 915d)]    // Pixel 8 Pro, portrait
    [InlineData(915d, 412d)]    // Pixel 8 Pro, landscape - wide but far too short
    [InlineData(430d, 932d)]    // iPhone 16 Pro Max, portrait
    [InlineData(932d, 430d)]    // iPhone 16 Pro Max, landscape
    public void Phones_never_split(double width, double height)
        => AdaptiveLayoutMetrics.SupportsTwoPanes(width, height, SplitMinimumWidth, SplitMinimumHeight)
            .ShouldBeFalse();

    [Theory]
    [InlineData(834d, 1194d)]   // iPad 11-inch, portrait
    [InlineData(1194d, 834d)]   // iPad 11-inch, landscape
    [InlineData(1024d, 1366d)]  // iPad 13-inch, portrait
    [InlineData(1366d, 1024d)]  // iPad 13-inch, landscape
    [InlineData(744d, 1133d)]   // iPad mini, portrait
    [InlineData(1280d, 800d)]   // Pixel Tablet, landscape
    [InlineData(800d, 1280d)]   // Pixel Tablet, portrait
    public void Tablets_split_in_both_orientations(double width, double height)
        => AdaptiveLayoutMetrics.SupportsTwoPanes(width, height, SplitMinimumWidth, SplitMinimumHeight)
            .ShouldBeTrue();

    [Fact]
    public void Half_width_multitasking_collapses_the_split()
        => AdaptiveLayoutMetrics.SupportsTwoPanes(507d, 1024d, SplitMinimumWidth, SplitMinimumHeight)
            .ShouldBeFalse();

    [Theory]
    [InlineData(360d)]
    [InlineData(412d)]
    [InlineData(680d)]
    public void Narrow_content_is_left_unconstrained(double available)
        => AdaptiveLayoutMetrics.ResolveContentWidth(available, 680d)
            .ShouldBe(AdaptiveLayoutMetrics.Unconstrained);

    [Fact]
    public void Wide_content_is_capped_at_the_measure()
        => AdaptiveLayoutMetrics.ResolveContentWidth(1366d, 680d).ShouldBe(680d);

    [Fact]
    public void Unmeasured_content_is_left_unconstrained()
        => AdaptiveLayoutMetrics.ResolveContentWidth(-1d, 680d)
            .ShouldBe(AdaptiveLayoutMetrics.Unconstrained);

    [Theory]
    [InlineData(412d, 1)]
    [InlineData(744d, 2)]
    [InlineData(1024d, 3)]
    [InlineData(1366d, 3)]
    public void Card_columns_grow_with_width(double available, int expected)
        => AdaptiveLayoutMetrics.ResolveColumnCount(available, 340d, 3).ShouldBe(expected);

    [Fact]
    public void Card_columns_never_drop_below_one()
        => AdaptiveLayoutMetrics.ResolveColumnCount(120d, 340d, 3).ShouldBe(1);

    [Fact]
    public void List_pane_takes_its_preferred_width_when_there_is_room()
        => AdaptiveLayoutMetrics.ResolveListPaneWidth(1366d, 360d, 420d, 24d).ShouldBe(360d);

    [Fact]
    public void List_pane_yields_width_to_protect_the_detail_pane()
    {
        // iPad mini portrait: 744 - 24 gutter - 420 minimum detail leaves 300 for the list.
        AdaptiveLayoutMetrics.ResolveListPaneWidth(744d, 360d, 420d, 24d).ShouldBe(300d);
    }

    [Fact]
    public void List_pane_is_unconstrained_when_no_split_fits()
        => AdaptiveLayoutMetrics.ResolveListPaneWidth(400d, 360d, 420d, 24d)
            .ShouldBe(AdaptiveLayoutMetrics.Unconstrained);
}
