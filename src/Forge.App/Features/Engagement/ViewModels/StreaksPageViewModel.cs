using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Forge.Domain.Engagement;

namespace Forge.App.Features.Engagement.ViewModels;

public sealed partial class StreaksPageViewModel : ObservableObject
{
    public StreaksPageViewModel()
    {
        CurrentStreakDays = 5;
        BestStreakDays = 12;
        FreezesRemaining = 2;
        FreezesRemainingProgress = 2.0 / 3.0;
        EncouragingMessage = EngagementEthicsPolicy.SupportiveStreakBreakMessage;
        History =
        [
            new StreakHistoryRow("Today", "Training planned", "Keeps your rhythm moving."),
            new StreakHistoryRow("Yesterday", "Rest day", "Protected: rest is part of the plan."),
            new StreakHistoryRow("Monday", "Workout logged", "Three sets completed.")
        ];
    }

    [ObservableProperty]
    private int currentStreakDays;

    [ObservableProperty]
    private int bestStreakDays;

    [ObservableProperty]
    private int freezesRemaining;

    [ObservableProperty]
    private double freezesRemainingProgress;

    [ObservableProperty]
    private string encouragingMessage;

    public ObservableCollection<StreakHistoryRow> History { get; }
}

public sealed record StreakHistoryRow(string Date, string Title, string Detail);
