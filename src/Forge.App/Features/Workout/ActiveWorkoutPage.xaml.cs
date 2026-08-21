using DevExpress.Maui.Controls;

namespace Forge.App.Features.Workout;

/// <summary>
/// The logging screen used during a workout.
/// </summary>
/// <remarks>
/// The screen is held awake for the whole session. A lifter checking their phone between sets
/// has chalky, sweaty hands and often gloves, so forcing an unlock every ninety seconds is a
/// real usability failure rather than a nicety. The previous value is restored on the way out so
/// Forge never leaves the device burning battery once the workout ends.
/// </remarks>
public partial class ActiveWorkoutPage : ContentPage
{
    private readonly ActiveWorkoutPageViewModel viewModel;
    private bool previousKeepScreenOn;
    private bool timerRunning;

    /// <summary>Creates the active workout page.</summary>
    /// <param name="viewModel">The page view model.</param>
    public ActiveWorkoutPage(ActiveWorkoutPageViewModel viewModel)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(viewModel);

        this.viewModel = viewModel;
        BindingContext = viewModel;
        viewModel.LiveAnnouncementRequested += OnLiveAnnouncementRequested;
    }
    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        previousKeepScreenOn = DeviceDisplay.Current.KeepScreenOn;
        DeviceDisplay.Current.KeepScreenOn = true;

        viewModel.Attach();
        await viewModel.InitializeAsync();
        await viewModel.ResumeSensorsAsync();
        timerRunning = true;

        // The tick only triggers a repaint. Every displayed value is recomputed from the wall
        // clock, so a rotation or a suspend that stops the tick cannot desynchronise the timer.
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

    /// <inheritdoc />
    protected override async void OnDisappearing()
    {
        timerRunning = false;
        DeviceDisplay.Current.KeepScreenOn = previousKeepScreenOn;
        viewModel.Detach();
        await viewModel.SuspendSensorsAsync();
        base.OnDisappearing();
    }

    /// <inheritdoc />
    protected override bool OnBackButtonPressed()
    {
        _ = NavigateBackAfterSavingAsync();
        return true;
    }

    private static void OnLiveAnnouncementRequested(object? sender, string message)
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
