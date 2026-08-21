namespace Forge.App.Features.Plans;

public partial class PlanSchedulePage : ContentPage
{
    private readonly PlanScheduleViewModel viewModel;

    public PlanSchedulePage(PlanScheduleViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = viewModel.LoadCommand.ExecuteAsync(null);
    }
}
