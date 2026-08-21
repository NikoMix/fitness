namespace Forge.App.Features.Plans;

public partial class PlanEditorPage : ContentPage, IQueryAttributable
{
    private readonly PlanEditorViewModel viewModel;
    private Guid planId = Guid.Empty;

    public PlanEditorPage(PlanEditorViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("forge.plan", out var value) && value is Guid id)
        {
            planId = id;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = viewModel.LoadCommand.ExecuteAsync(planId);
    }
}
