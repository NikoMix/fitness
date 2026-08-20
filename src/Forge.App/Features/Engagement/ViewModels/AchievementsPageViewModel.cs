using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Domain.Engagement;

namespace Forge.App.Features.Engagement.ViewModels;

public sealed partial class AchievementsPageViewModel : ObservableObject
{
    public AchievementsPageViewModel()
    {
        Achievements =
        [
            new AchievementCardViewModel("First personal record", "Strength", "You found a new benchmark. Keep building at your pace.", 1, true),
            new AchievementCardViewModel("Three-session rhythm", "Consistency", "Three training days logged. Your routine is taking shape.", 0.6, false),
            new AchievementCardViewModel("10,000 kg moved", "Volume", "A meaningful body of work, one set at a time.", 0.35, false),
            new AchievementCardViewModel("Movement explorer", "Exploration", "Try five different exercises to learn what fits.", 0.2, false)
        ];

        HasAchievements = Achievements.Any(achievement => achievement.IsUnlocked);
        HasNoAchievements = !HasAchievements;
    }

    public ObservableCollection<AchievementCardViewModel> Achievements { get; }

    [ObservableProperty]
    private bool hasAchievements;

    [ObservableProperty]
    private bool hasNoAchievements;

    public event EventHandler<AchievementCardViewModel>? ShareRequested;

    [RelayCommand]
    private void Share(AchievementCardViewModel achievement)
    {
        if (achievement.IsUnlocked && EngagementEthicsPolicy.IsSupportiveCopy(achievement.Description))
        {
            ShareRequested?.Invoke(this, achievement);
        }
    }
}

public sealed record AchievementCardViewModel(
    string Title,
    string Category,
    string Description,
    double Progress,
    bool IsUnlocked);
