using System.Globalization;
using DevExpress.Maui.Core;

namespace Forge.App.Controls;

/// <summary>
/// Shows how far through a multi-step flow the user is, as a segmented bar and a spoken caption.
/// </summary>
/// <remarks>
/// Segments are built in code rather than declared in XAML because the step count is a property of
/// the flow, not of the markup: a flow that grows a step should not need its progress bar edited.
/// The caption is the accessible representation - a screen reader is read "Step 2 of 6, a few
/// numbers to work from" rather than being walked through six anonymous rectangles.
/// </remarks>
public partial class StepProgressIndicator : ContentView
{
    /// <summary>Identifies the <see cref="StepCount"/> bindable property.</summary>
    public static readonly BindableProperty StepCountProperty = BindableProperty.Create(
        nameof(StepCount),
        typeof(int),
        typeof(StepProgressIndicator),
        0,
        propertyChanged: OnStepsChanged);

    /// <summary>Identifies the <see cref="CurrentStep"/> bindable property.</summary>
    public static readonly BindableProperty CurrentStepProperty = BindableProperty.Create(
        nameof(CurrentStep),
        typeof(int),
        typeof(StepProgressIndicator),
        0,
        propertyChanged: OnStepsChanged);

    /// <summary>Identifies the <see cref="StepTitle"/> bindable property.</summary>
    public static readonly BindableProperty StepTitleProperty = BindableProperty.Create(
        nameof(StepTitle),
        typeof(string),
        typeof(StepProgressIndicator),
        string.Empty,
        propertyChanged: OnStepsChanged);

    /// <summary>Initialises the control.</summary>
    public StepProgressIndicator()
    {
        InitializeComponent();
        Rebuild();
    }

    /// <summary>How many steps the flow has in total.</summary>
    public int StepCount
    {
        get => (int)GetValue(StepCountProperty);
        set => SetValue(StepCountProperty, value);
    }

    /// <summary>The one-based position of the step currently showing.</summary>
    public int CurrentStep
    {
        get => (int)GetValue(CurrentStepProperty);
        set => SetValue(CurrentStepProperty, value);
    }

    /// <summary>The title of the step currently showing, appended to the spoken caption.</summary>
    public string StepTitle
    {
        get => (string)GetValue(StepTitleProperty);
        set => SetValue(StepTitleProperty, value);
    }

    private static void OnStepsChanged(BindableObject bindable, object oldValue, object newValue)
        => ((StepProgressIndicator)bindable).Rebuild();

    private void Rebuild()
    {
        var count = Math.Max(0, StepCount);
        var current = Math.Clamp(CurrentStep, 0, count);

        if (SegmentHost.ColumnDefinitions.Count != count)
        {
            SegmentHost.Children.Clear();
            SegmentHost.ColumnDefinitions.Clear();

            for (var index = 0; index < count; index++)
            {
                SegmentHost.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                var segment = new DXBorder();
                segment.SetValue(Grid.ColumnProperty, index);
                segment.SetValue(AutomationProperties.IsInAccessibleTreeProperty, false);
                SegmentHost.Children.Add(segment);
            }
        }

        var reachedStyle = (Style)Resources["StepSegmentReached"];
        var pendingStyle = (Style)Resources["StepSegmentPending"];

        for (var index = 0; index < SegmentHost.Children.Count; index++)
        {
            if (SegmentHost.Children[index] is DXBorder segment)
            {
                segment.Style = index < current ? reachedStyle : pendingStyle;
            }
        }

        var caption = count <= 0
            ? string.Empty
            : string.Create(CultureInfo.CurrentCulture, $"Step {current} of {count}");

        CaptionLabel.Text = caption;
        CaptionLabel.IsVisible = caption.Length > 0;

        SemanticProperties.SetDescription(
            IndicatorLayout,
            string.Join(", ", new[] { caption, StepTitle }.Where(text => !string.IsNullOrWhiteSpace(text))));
    }
}
