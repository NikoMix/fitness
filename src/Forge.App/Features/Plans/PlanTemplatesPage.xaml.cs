namespace Forge.App.Features.Plans;

public partial class PlanTemplatesPage : ContentPage
{
    private readonly PlanTemplatesViewModel viewModel;

    public PlanTemplatesPage(PlanTemplatesViewModel viewModel)
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
