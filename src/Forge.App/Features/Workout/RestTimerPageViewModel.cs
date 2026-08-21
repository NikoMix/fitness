using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using Forge.Domain.Workout;

namespace Forge.App.Features.Workout;

/// <summary>
/// The full-screen rest timer.
/// </summary>
/// <remarks>
/// This reads the same <see cref="IActiveWorkoutSession"/> the logging screen writes to, so
/// adding time here is immediately true there and vice versa. The displayed value is always
/// recomputed from the timer's absolute end time, which is what makes it correct after the phone
/// has been in a pocket with the screen off.
/// </remarks>
public sealed partial class RestTimerPageViewModel : ObservableObject
{
    private readonly IWorkoutClock clock;
    private readonly IActiveWorkoutSession session;

    /// <summary>Creates the rest timer view model.</summary>
    /// <param name="clock">Workout clock.</param>
    /// <param name="session">Shared owner of the workout in progress.</param>
    public RestTimerPageViewModel(IWorkoutClock clock, IActiveWorkoutSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        this.clock = clock;
        this.session = session;
    }

    /// <summary>Raised when something happened that a screen reader should announce.</summary>
    public event EventHandler<string>? LiveAnnouncementRequested;

    /// <summary>Listens for rest changes made from any other screen while this one is visible.</summary>
    public void Attach() => session.RestChanged += OnRestChanged;

    /// <summary>Stops listening when the screen goes away, so the singleton holds no reference.</summary>
    public void Detach() => session.RestChanged -= OnRestChanged;

    [ObservableProperty]
    private string remainingText = "0:00";

    [ObservableProperty]
    private string reasonText = string.Empty;

    [ObservableProperty]
    private string exerciseText = string.Empty;

    [ObservableProperty]
    private string endsAtText = string.Empty;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private bool isResting;

    [ObservableProperty]
    private bool isComplete;

    /// <summary>Recomputes the display from the wall clock.</summary>
    public void Reconcile()
    {
        if (session.State?.ActiveRestTimer is not { } timer)
        {
            IsResting = false;
            IsComplete = false;
            Progress = 0d;
            RemainingText = "No rest running";
            ReasonText = "Start a set to begin resting.";
            EndsAtText = string.Empty;
            ExerciseText = session.State?.CurrentExerciseName ?? string.Empty;
            return;
        }

        var now = clock.UtcNow;
        var remaining = timer.Remaining(now);

        IsResting = remaining > TimeSpan.Zero;
        IsComplete = timer.HasElapsed(now);
        Progress = timer.Progress(now);
        RemainingText = FormatRemaining(remaining);
        ReasonText = DescribeReason(session.RestReason);
        ExerciseText = session.State?.CurrentExerciseName ?? string.Empty;

        // The absolute end time is shown because it is the value that stays true across a
        // backgrounded app; a countdown alone gives the user no way to sanity-check it.
        EndsAtText = IsResting
            ? $"Ends at {timer.TargetEndUtc.ToLocalTime().DateTime.ToString("t", CultureInfo.CurrentCulture)}"
            : "Rest complete";
    }

    [RelayCommand]
    private async Task AdjustAsync(string secondsText)
    {
        if (!int.TryParse(secondsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return;
        }

        await session.AdjustRestAsync(TimeSpan.FromSeconds(seconds), CancellationToken.None);
        Reconcile();
        LiveAnnouncementRequested?.Invoke(this, $"Rest is now {RemainingText}.");
    }

    [RelayCommand]
    private async Task SkipAsync()
    {
        await session.SkipRestAsync(CancellationToken.None);
        Reconcile();
        LiveAnnouncementRequested?.Invoke(this, "Rest skipped.");
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private static Task BackToWorkoutAsync() => Shell.Current.GoToAsync("..");

    private void OnRestChanged(object? sender, EventArgs e)
    {
        if (MainThread.IsMainThread)
        {
            Reconcile();
            return;
        }

        MainThread.BeginInvokeOnMainThread(Reconcile);
    }

    private static string FormatRemaining(TimeSpan remaining)
        => remaining <= TimeSpan.Zero ? "0:00" : $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}";

    private static string DescribeReason(RestReason reason) => reason switch
    {
        RestReason.WarmUpSet => "Warm-up rest — short by design",
        RestReason.SupersetRound => "Round complete — take the full rest",
        _ => "Working set rest"
    };
}
