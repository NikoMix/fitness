namespace Forge.App.Features.Exercises;

public partial class ExerciseLibraryPage : ContentPage
{
    private readonly ExerciseLibraryViewModel viewModel;

    public ExerciseLibraryPage(ExerciseLibraryViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = viewModel.LoadAsync();
    }
}
