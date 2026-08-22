using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Plans;
using Forge.App.Features.Workout;
using Forge.App.Navigation;
using Forge.Core.Abstractions;
using Forge.Domain.Planning;
using Microsoft.Extensions.Logging;

namespace Forge.App.Features.Train;

/// <summary>
/// The training hub: the entry point into a session, the catalogue and past work.
/// </summary>
/// <remarks>
/// Starting a workout used to mean one button that passed nothing, so the session opened against
/// the whole exercise catalogue with an invented target and the plan the user had written was
/// never consulted. This screen is where the two halves of the product now meet: it offers the
/// day the plan places today, then the rest of the plan's days, and it keeps the ad hoc route
/// because training without a plan is a real and legitimate thing to do.
/// </remarks>
public sealed partial class TrainViewModel(
    IPlanPersistenceService plans,
    IWorkoutPersistenceService workouts,
    ILogger<TrainViewModel>? logger = null) : ObservableObject
{
    private static readonly Action<ILogger, Exception?> PlanLoadFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(PlanLoadFailed)),
            "Could not load the training plan for the Train screen. The ad hoc start stays available.");

    private Guid suggestedDayId;

    /// <summary>The days of the active plan, in plan order.</summary>
    public ObservableCollection<PlanDayStartRow> PlanDays { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    private string planName = string.Empty;

    [ObservableProperty]
    private string planMessage = "Build a plan and Forge will queue its exercises, sets, reps and rest when you start a workout.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestedDay))]
    private string suggestedDayName = string.Empty;

    [ObservableProperty]
    private string suggestedDayDetail = string.Empty;

    [ObservableProperty]
    private bool isLoadingPlan = true;

    /// <summary>Whether the profile has a plan to start from.</summary>
    public bool HasPlan => !string.IsNullOrEmpty(PlanName);

    /// <summary>Whether Forge has a day to offer for today.</summary>
    public bool HasSuggestedDay => !string.IsNullOrEmpty(SuggestedDayName);

    /// <summary>Loads the active plan and works out which day to offer today.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes once the screen is ready.</returns>
    [RelayCommand]
    public async Task LoadPlanAsync(CancellationToken cancellationToken)
    {
        IsLoadingPlan = true;

        TrainingPlan? plan = null;
        IReadOnlyList<PlanDayCompletion> completions = [];
        var failureMessage = string.Empty;

        try
        {
            plan = await plans.GetActivePlanAsync(cancellationToken).ConfigureAwait(false);
            completions = await workouts.LoadPlanDayCompletionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Deliberately broad. Train is a tab the user lands on constantly, and a plan that
            // cannot be read must not take the screen down with it - the ad hoc start below still
            // works. The message is fixed rather than interpolated from the exception, which is
            // what DescribeFor enforces.
            LogPlanLoadFailed(logger, ex);
            failureMessage = ForgeUserFacingException.DescribeFor(
                ex,
                "Forge could not read your training plan just now. You can still start a workout and log it.");
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var completedThisWeek = completions
            .Where(completion => completion.CompletedOn >= weekStart && completion.CompletedOn <= today)
            .Select(completion => completion.PlanDayId)
            .ToHashSet();

        var rows = plan is null
            ? []
            : plan.Days
                .OrderBy(day => day.Ordinal)
                .Select(day => new PlanDayStartRow(
                    day.Id,
                    day.Name,
                    Describe(day),
                    completedThisWeek.Contains(day.Id)))
                .ToList();

        var suggested = plan is null ? null : PlanWorkoutProjection.DayForDate(plan, today, completedThisWeek);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            PlanDays.Clear();
            foreach (var row in rows)
            {
                PlanDays.Add(row);
            }

            PlanName = plan?.Name ?? string.Empty;
            if (!string.IsNullOrEmpty(failureMessage))
            {
                PlanMessage = failureMessage;
            }
            else if (plan is not null)
            {
                PlanMessage = rows.Count == 0
                    ? "This plan has no days yet. Add one and Forge will queue it when you start a workout."
                    : "Starting from a day queues its exercises with the sets, reps, load and rest you wrote.";
            }

            suggestedDayId = suggested?.Id ?? Guid.Empty;
            SuggestedDayName = suggested?.Name ?? string.Empty;
            SuggestedDayDetail = suggested is null ? string.Empty : Describe(suggested);
            IsLoadingPlan = false;
        }).ConfigureAwait(false);
    }

    /// <summary>Starts the day the plan places today.</summary>
    /// <returns>A task that completes once navigation is under way.</returns>
    [RelayCommand]
    private Task StartSuggestedDayAsync()
        => suggestedDayId == Guid.Empty ? Task.CompletedTask : GoToWorkoutAsync(suggestedDayId);

    /// <summary>Starts a specific plan day.</summary>
    /// <param name="row">The day to execute.</param>
    /// <returns>A task that completes once navigation is under way.</returns>
    [RelayCommand]
    private static Task StartPlanDayAsync(PlanDayStartRow row)
        => row is null ? Task.CompletedTask : GoToWorkoutAsync(row.PlanDayId);

    /// <summary>
    /// Starts a workout that follows no plan.
    /// </summary>
    /// <remarks>
    /// Kept deliberately. Somebody in a hotel gym with three machines is still training, and the
    /// screen they land on now says it has no target rather than showing them one Forge invented.
    /// </remarks>
    /// <returns>A task that completes once navigation is under way.</returns>
    [RelayCommand]
    private static Task StartWorkoutAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.ActiveWorkout);

    [RelayCommand]
    private static Task OpenPlansAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.PlanList);

    [RelayCommand]
    private static Task OpenExerciseLibraryAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.ExerciseLibrary);

    [RelayCommand]
    private static Task OpenWorkoutHistoryAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.WorkoutHistory);

    [RelayCommand]
    private static Task OpenPlateCalculatorAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.PlateCalculator);

    [RelayCommand]
    private static Task OpenVideoLibraryAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.VideoLibrary);

    [RelayCommand]
    private static Task OpenMorningCheckInAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.MorningCheckIn);

    [RelayCommand]
    private static Task OpenReadinessAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.Readiness);

    [RelayCommand]
    private static Task OpenCoachingAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.Coaching);

    private static Task GoToWorkoutAsync(Guid planDayId)
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(
            ForgeRoutes.ActiveWorkout,
            new Dictionary<string, object> { [ActiveWorkoutPage.PlanDayParameter] = planDayId });

    private static string Describe(PlanDay day)
    {
        var exercises = day.Exercises.Count;
        if (exercises == 0)
        {
            return "No exercises yet";
        }

        var sets = day.Exercises.Sum(exercise => exercise.Sets.Count);
        var estimate = SessionDurationEstimator.Estimate(day);
        var exerciseText = exercises == 1 ? "1 exercise" : $"{exercises} exercises";
        var setText = sets == 1 ? "1 set" : $"{sets} sets";

        return estimate <= TimeSpan.Zero
            ? $"{exerciseText} · {setText}"
            : $"{exerciseText} · {setText} · about {Math.Round(estimate.TotalMinutes)} min";
    }

    private static void LogPlanLoadFailed(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            PlanLoadFailed(logger, exception);
        }
    }
}

/// <summary>One plan day the user can start a workout from.</summary>
/// <param name="PlanDayId">The day to execute.</param>
/// <param name="Name">Display name, for example "Upper A".</param>
/// <param name="Detail">Exercise count, set count and an estimated duration.</param>
/// <param name="IsDoneThisWeek">Whether this day has already been completed since Monday.</param>
public sealed record PlanDayStartRow(Guid PlanDayId, string Name, string Detail, bool IsDoneThisWeek)
{
    /// <summary>A spoken description covering the whole row.</summary>
    public string AccessibilityDescription
        => IsDoneThisWeek
            ? $"Start {Name}. {Detail}. Already completed this week."
            : $"Start {Name}. {Detail}.";

    /// <summary>The completion marker, or an empty string when the day is still outstanding.</summary>
    public string StatusText => IsDoneThisWeek ? "Done this week" : string.Empty;
}
