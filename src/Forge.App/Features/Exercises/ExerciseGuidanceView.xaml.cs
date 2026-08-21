namespace Forge.App.Features.Exercises;

/// <summary>
/// Renders the full guidance for one exercise, bound to an <see cref="ExerciseDetailViewModel"/>.
/// </summary>
/// <remarks>
/// Used by <see cref="ExerciseDetailPage"/> on a phone and by the detail pane of
/// <see cref="ExerciseLibraryPage"/> on a tablet, so that the two are the same screen rather than
/// two screens that happen to agree today.
/// </remarks>
public partial class ExerciseGuidanceView : ContentView
{
    /// <summary>Creates the view.</summary>
    public ExerciseGuidanceView() => InitializeComponent();
}
