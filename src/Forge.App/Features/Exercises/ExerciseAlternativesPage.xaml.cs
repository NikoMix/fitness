namespace Forge.App.Features.Exercises;

public partial class ExerciseAlternativesPage : ContentPage, IQueryAttributable
{
    private readonly ExerciseAlternativesViewModel viewModel;

    public ExerciseAlternativesPage(ExerciseAlternativesViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("forge.parameter", out var value) && value is string exerciseName)
        {
            _ = viewModel.LoadAsync(exerciseName);
        }
    }
}
