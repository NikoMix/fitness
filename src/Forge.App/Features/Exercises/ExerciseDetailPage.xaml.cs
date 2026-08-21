namespace Forge.App.Features.Exercises;

public partial class ExerciseDetailPage : ContentPage, IQueryAttributable
{
    private readonly ExerciseDetailViewModel viewModel;

    public ExerciseDetailPage(ExerciseDetailViewModel viewModel)
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
