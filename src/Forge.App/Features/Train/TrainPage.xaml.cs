namespace Forge.App.Features.Train;

/// <summary>
/// The training hub.
/// </summary>
/// <remarks>
/// The plan is reloaded on every appearance rather than once in the constructor. The user reaches
/// this screen straight after finishing a session, and after editing a plan, and in both cases the
/// day Forge offers next has just changed.
/// </remarks>
public partial class TrainPage : ContentPage
{
    private readonly TrainViewModel viewModel;

    /// <summary>Creates the training hub.</summary>
    /// <param name="viewModel">The page view model.</param>
    public TrainPage(TrainViewModel viewModel)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(viewModel);

        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = viewModel.LoadPlanCommand.ExecuteAsync(null);
    }
}
