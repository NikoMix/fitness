namespace Forge.App.Features.Workout;

public partial class WorkoutSummaryPage : ContentPage, IQueryAttributable
{
    private readonly WorkoutSummaryPageViewModel viewModel;
    private Guid? sessionId;

    public WorkoutSummaryPage(WorkoutSummaryPageViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("sessionId", out var value) && Guid.TryParse(value?.ToString(), out var parsed))
        {
            sessionId = parsed;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync(sessionId);
    }
}
