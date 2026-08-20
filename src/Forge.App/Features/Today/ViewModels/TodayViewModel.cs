using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Insights.Services;
using Forge.App.Navigation;

namespace Forge.App.Features.Today.ViewModels;

public sealed partial class TodayViewModel(IInsightsDataService dataService) : ObservableObject
{
    public ObservableCollection<TodayRingViewModel> Rings { get; } =
    [
        new TodayRingViewModel("Training", 0d, "No workout logged yet"),
        new TodayRingViewModel("Mobility", 0d, "Add a short warm-up to fill this ring"),
        new TodayRingViewModel("Hydration", 0d, "Hydration logs appear here"),
    ];

    public ObservableCollection<RecentActivityViewModel> RecentActivity { get; } = [];

    public bool HasNoRecentActivity => !HasRecentActivity;

    public bool HasNoScheduledSession => !HasScheduledSession;

    [ObservableProperty]
    private bool hasScheduledSession;

    [ObservableProperty]
    private bool hasRecentActivity;

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private string sessionTitle = "Today's session";

    [ObservableProperty]
    private string sessionSubtitle = "Loading your local plan.";

    [ObservableProperty]
    private string nextActionTitle = "Loading";

    [ObservableProperty]
    private string nextActionDetail = "Reading today's persisted sets, plan and hydration.";

    partial void OnHasScheduledSessionChanged(bool value) => OnPropertyChanged(nameof(HasNoScheduledSession));

    partial void OnHasRecentActivityChanged(bool value) => OnPropertyChanged(nameof(HasNoRecentActivity));

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var snapshot = await dataService.LoadAsync(DateOnly.FromDateTime(DateTime.Now), cancellationToken).ConfigureAwait(false);
            var today = snapshot.Today;
            var rings = today.Rings.Select(ring => new TodayRingViewModel(ring.Label, ring.Progress, ring.Detail)).ToList();
            var recent = today.RecentActivity.Select(activity => new RecentActivityViewModel(
                    activity.Title,
                    activity.Detail,
                    activity.WhenUtc.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture)))
                .ToList();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Rings.Clear();
                foreach (var ring in rings)
                {
                    Rings.Add(ring);
                }

                RecentActivity.Clear();
                foreach (var activity in recent)
                {
                    RecentActivity.Add(activity);
                }

                SessionTitle = today.SessionTitle;
                SessionSubtitle = today.SessionSubtitle;
                HasScheduledSession = today.HasScheduledSession;
                HasRecentActivity = RecentActivity.Count > 0;
                NextActionTitle = today.NextActionTitle;
                NextActionDetail = today.NextActionDetail;
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsLoading = false);
        }
    }

    [RelayCommand]
    private static Task StartWorkoutAsync() => Shell.Current.GoToAsync(ForgeRoutes.Train);

    [RelayCommand]
    private static Task CreatePlanAsync() => Shell.Current.GoToAsync(ForgeRoutes.PlanList);
}

public sealed record TodayRingViewModel(string Label, double Progress, string Detail);

public sealed record RecentActivityViewModel(string Title, string Detail, string When);
