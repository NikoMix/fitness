using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Insights.Services;
using Forge.App.Features.Profile;
using Forge.App.Navigation;
using Forge.Domain.Dashboard;
using Forge.Domain.Onboarding;

namespace Forge.App.Features.Today.ViewModels;

/// <summary>
/// The Today dashboard: one hero action, three rings that reflect real logged data, and a fast
/// path into training.
/// </summary>
/// <remarks>
/// <para>
/// Every number on this screen is read from local storage. Nothing is seeded, estimated or filled
/// in to make the screen look inhabited - a fitness app that shows plausible sample numbers on day
/// one teaches the user that its numbers cannot be trusted on day thirty.
/// </para>
/// <para>
/// The hero card is chosen by <see cref="TodayFocusPlanner"/> from the profile's completeness and
/// today's real counts, so a skipped onboarding leads with the specific answers that are missing
/// rather than with a plan that cannot be personalised.
/// </para>
/// </remarks>
public sealed partial class TodayViewModel : ObservableObject
{
    private readonly IInsightsDataService dataService;
    private readonly ProfileStore? profileStore;
    private TodayFocusAction primaryAction = TodayFocusAction.StartWorkout;

    /// <summary>Initialises the view model.</summary>
    /// <param name="dataService">Reads today's persisted sets, plan and hydration.</param>
    /// <param name="profileStore">Reads the local profile so setup gaps can be named.</param>
    public TodayViewModel(IInsightsDataService dataService, ProfileStore profileStore)
    {
        this.dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        this.profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
    }

    /// <summary>Today's rings, in the order the data service returns them.</summary>
    public ObservableCollection<TodayRingViewModel> Rings { get; } = [];

    /// <summary>Recent activity across all time, newest first.</summary>
    public ObservableCollection<RecentActivityViewModel> RecentActivity { get; } = [];

    /// <summary>Whether nothing has been logged yet.</summary>
    public bool HasNoRecentActivity => !HasRecentActivity;

    /// <summary>Whether no ring has any progress at all today.</summary>
    public bool HasNoRingData => !HasRingData;

    [ObservableProperty]
    private bool hasRecentActivity;

    [ObservableProperty]
    private bool hasRingData;

    [ObservableProperty]
    private bool hasScheduledSession;

    [ObservableProperty]
    private bool showsPlanPrompt;

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private bool hasLoadError;

    [ObservableProperty]
    private string dateLine = string.Empty;

    [ObservableProperty]
    private string greeting = "Today";

    [ObservableProperty]
    private string focusHeadline = "Loading";

    [ObservableProperty]
    private string focusMessage = "Reading today's persisted sets, plan and hydration.";

    [ObservableProperty]
    private string focusActionText = "Start a workout";

    [ObservableProperty]
    private bool hasSetupNudge;

    [ObservableProperty]
    private string setupNudge = string.Empty;

    [ObservableProperty]
    private string sessionTitle = "Today's session";

    [ObservableProperty]
    private string sessionSubtitle = "Loading your local plan.";

    [ObservableProperty]
    private string ringSummary = string.Empty;

    [ObservableProperty]
    private string planDetail = string.Empty;

    partial void OnHasRecentActivityChanged(bool value) => OnPropertyChanged(nameof(HasNoRecentActivity));

    partial void OnHasRingDataChanged(bool value) => OnPropertyChanged(nameof(HasNoRingData));

    /// <summary>Reads today's data and rebuilds the screen.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes once the screen has been rebuilt.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        var localNow = DateTime.Now;

        try
        {
            var snapshot = await dataService.LoadAsync(DateOnly.FromDateTime(localNow), cancellationToken).ConfigureAwait(false);
            var completion = await LoadCompletionAsync(cancellationToken).ConfigureAwait(false);

            var today = snapshot.Today;
            var rings = today.Rings.Select(ring => new TodayRingViewModel(ring.Label, ring.Progress, ring.Detail)).ToList();
            var recent = today.RecentActivity
                .Select(activity => new RecentActivityViewModel(
                    activity.Title,
                    activity.Detail,
                    activity.WhenUtc.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture)))
                .ToList();

            var trainingProgress = rings.Count > 0 ? rings[0].Progress : 0d;
            var focus = TodayFocusPlanner.Plan(new TodayFocusInputs(
                completion,
                today.HasScheduledSession,
                trainingProgress,
                recent.Count));

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                HasLoadError = false;
                Replace(Rings, rings);
                Replace(RecentActivity, recent);

                DateLine = localNow.ToString("dddd d MMMM", CultureInfo.CurrentCulture);
                Greeting = GreetingFor(localNow);

                SessionTitle = today.SessionTitle;
                SessionSubtitle = today.SessionSubtitle;
                PlanDetail = today.NextActionDetail;
                HasScheduledSession = today.HasScheduledSession;

                // Only one prompt at a time. When the hero is already asking for setup, adding a
                // "choose a plan" card underneath turns a single clear next step into a menu.
                ShowsPlanPrompt = !today.HasScheduledSession && focus.Kind != TodayFocusKind.FinishSetup;
                HasRecentActivity = recent.Count > 0;
                HasRingData = rings.Exists(ring => ring.Progress > 0d);
                RingSummary = TodayFocusPlanner.DescribeRings([.. rings.Select(ring => ring.Progress)]);

                FocusHeadline = focus.Headline;
                FocusMessage = focus.Message;
                FocusActionText = focus.PrimaryActionLabel;
                HasSetupNudge = focus.ShowsSetupNudge;
                SetupNudge = focus.SetupNudge;
                primaryAction = focus.PrimaryAction;
            }).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Local storage has not finished starting. Say so plainly and offer a retry rather
            // than rendering zeroed rings, which would read as "you have done nothing today".
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                HasLoadError = true;
                ShowsPlanPrompt = false;
                FocusHeadline = "Forge is still opening your local database";
                FocusMessage = "Nothing is lost. Give it a moment and reload - Today will fill in as soon as local storage is ready.";
                FocusActionText = "Reload";
            }).ConfigureAwait(false);
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsLoading = false).ConfigureAwait(false);
        }
    }

    /// <summary>Runs the hero action chosen for the current state.</summary>
    [RelayCommand]
    private async Task PrimaryActionAsync()
    {
        if (HasLoadError)
        {
            await LoadAsync().ConfigureAwait(true);
            return;
        }

        var route = primaryAction switch
        {
            TodayFocusAction.FinishSetup => ForgeRoutes.GoalWizard,
            TodayFocusAction.ChoosePlan => ForgeRoutes.PlanList,
            TodayFocusAction.ReviewToday => ForgeRoutes.Insights,
            _ => $"//{ForgeRoutes.Train}",
        };

        await Shell.Current.GoToAsync(route).ConfigureAwait(true);
    }

    /// <summary>Opens the goal wizard to complete a skipped or partial setup.</summary>
    [RelayCommand]
    private static Task FinishSetupAsync() => Shell.Current.GoToAsync(ForgeRoutes.GoalWizard);

    /// <summary>Opens the training surface.</summary>
    [RelayCommand]
    private static Task StartWorkoutAsync() => Shell.Current.GoToAsync($"//{ForgeRoutes.Train}");

    /// <summary>Opens the plan list.</summary>
    [RelayCommand]
    private static Task CreatePlanAsync() => Shell.Current.GoToAsync(ForgeRoutes.PlanList);

    /// <summary>Opens hydration logging, the fastest ring to move.</summary>
    [RelayCommand]
    private static Task LogHydrationAsync() => Shell.Current.GoToAsync(ForgeRoutes.Hydration);

    /// <summary>Re-reads today's data.</summary>
    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    private async Task<ProfileCompletion> LoadCompletionAsync(CancellationToken cancellationToken)
    {
        if (profileStore is null)
        {
            return ProfileCompletionCalculator.Evaluate(null, null);
        }

        var snapshot = await profileStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return ProfileCompletionCalculator.Evaluate(
            snapshot?.Profile,
            snapshot?.BodyMetrics.Count > 0 ? snapshot.BodyMetrics[0] : null);
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static string GreetingFor(DateTime localNow) => localNow.Hour switch
    {
        < 5 => "Late one",
        < 12 => "Good morning",
        < 18 => "Good afternoon",
        _ => "Good evening",
    };
}

/// <summary>One activity ring on Today.</summary>
/// <param name="Label">What the ring measures.</param>
/// <param name="Progress">Completion between 0 and 1.</param>
/// <param name="Detail">The real counts behind the ring.</param>
public sealed record TodayRingViewModel(string Label, double Progress, string Detail);

/// <summary>One recent activity row.</summary>
/// <param name="Title">What happened.</param>
/// <param name="Detail">Supporting numbers.</param>
/// <param name="When">When it happened, formatted for display.</param>
public sealed record RecentActivityViewModel(string Title, string Detail, string When);
