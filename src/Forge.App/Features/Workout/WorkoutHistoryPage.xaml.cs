namespace Forge.App.Features.Workout;

/// <summary>Past sessions, newest first, each opening its summary.</summary>
public partial class WorkoutHistoryPage : ContentPage
{
    private readonly WorkoutHistoryPageViewModel viewModel;

    /// <summary>Creates the workout history page.</summary>
    /// <param name="viewModel">The page view model.</param>
    public WorkoutHistoryPage(WorkoutHistoryPageViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }
}
