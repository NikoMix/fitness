namespace Forge.App.Features.Workout;

/// <summary>
/// Full-screen rest timer.
/// </summary>
/// <remarks>
/// The screen is kept awake while rest runs, because a phone that locks mid-rest forces the user
/// to unlock it with chalky hands to see how long is left. The tick only drives repainting: the
/// value it paints is recomputed from the timer's absolute end time, so a rotation, a lock, or a
/// suspend that stops the tick entirely cannot make the countdown wrong.
/// </remarks>
public partial class RestTimerPage : ContentPage
{
    private readonly RestTimerPageViewModel viewModel;
    private bool previousKeepScreenOn;
    private bool timerRunning;

    /// <summary>Creates the rest timer page.</summary>
    /// <param name="viewModel">The page view model.</param>
    public RestTimerPage(RestTimerPageViewModel viewModel)
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
        previousKeepScreenOn = DeviceDisplay.Current.KeepScreenOn;
        DeviceDisplay.Current.KeepScreenOn = true;

        viewModel.Attach();
        viewModel.LiveAnnouncementRequested += OnLiveAnnouncementRequested;
        viewModel.Reconcile();
        timerRunning = true;
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
        {
            if (!timerRunning)
            {
                return false;
            }

            viewModel.Reconcile();
            return true;
        });
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        timerRunning = false;
        DeviceDisplay.Current.KeepScreenOn = previousKeepScreenOn;
        viewModel.LiveAnnouncementRequested -= OnLiveAnnouncementRequested;
        viewModel.Detach();
        base.OnDisappearing();
    }

    private static void OnLiveAnnouncementRequested(object? sender, string message)
        => Microsoft.Maui.Accessibility.SemanticScreenReader.Announce(message);
}
