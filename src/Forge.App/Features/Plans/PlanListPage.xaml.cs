namespace Forge.App.Features.Plans;

public partial class PlanListPage : ContentPage
{
    private readonly PlanListViewModel viewModel;

    public PlanListPage(PlanListViewModel viewModel)
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
