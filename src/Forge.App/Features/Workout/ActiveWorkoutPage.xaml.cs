using DevExpress.Maui.Controls;

namespace Forge.App.Features.Workout;

public partial class ActiveWorkoutPage : ContentPage
{
    private readonly ActiveWorkoutPageViewModel viewModel;
    private bool previousKeepScreenOn;
    private bool timerRunning;

    public ActiveWorkoutPage(ActiveWorkoutPageViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
        viewModel.LiveAnnouncementRequested += OnLiveAnnouncementRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        previousKeepScreenOn = DeviceDisplay.Current.KeepScreenOn;
        DeviceDisplay.Current.KeepScreenOn = true;
        await viewModel.InitializeAsync();
        timerRunning = true;
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
        {
            if (!timerRunning)
            {
                return false;
            }

            viewModel.ReconcileRest();
            return true;
        });
        viewModel.ReconcileRest();
    }

    protected override void OnDisappearing()
    {
        timerRunning = false;
        DeviceDisplay.Current.KeepScreenOn = previousKeepScreenOn;
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        _ = NavigateBackAfterSavingAsync();
        return true;
    }

    private void OnLiveAnnouncementRequested(object? sender, string message)
        => Microsoft.Maui.Accessibility.SemanticScreenReader.Announce(message);

    private void ShowPlateCalculator(object? sender, EventArgs e)
    {
        viewModel.CalculatePlatesCommand.Execute(null);
        PlateSheet.State = BottomSheetState.HalfExpanded;
    }

    private async Task NavigateBackAfterSavingAsync()
    {
        await viewModel.PrepareToNavigateAwayAsync();
        await Shell.Current.GoToAsync("..");
    }
}
