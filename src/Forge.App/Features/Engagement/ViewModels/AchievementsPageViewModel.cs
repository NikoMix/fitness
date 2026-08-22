using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Engagement.Services;
using Forge.Domain.Engagement;

namespace Forge.App.Features.Engagement.ViewModels;

/// <summary>One badge card.</summary>
/// <param name="Code">Stable identifier of the definition.</param>
/// <param name="Title">Badge title.</param>
/// <param name="Category">What kind of thing it recognises.</param>
/// <param name="Description">What the user did, or what would earn it.</param>
/// <param name="WhyItMatters">Why this is good for the person.</param>
/// <param name="Progress">Measured progress from zero to one.</param>
/// <param name="ProgressText">The counted units behind the progress figure.</param>
/// <param name="IsUnlocked">Whether it has been earned.</param>
/// <param name="EarnedOn">When it was earned, worded for display.</param>
/// <param name="AccessibleDescription">The whole card as one sentence, for a screen reader.</param>
public sealed record AchievementCardViewModel(
    string Code,
    string Title,
    string Category,
    string Description,
    string WhyItMatters,
    double Progress,
    string ProgressText,
    bool IsUnlocked,
    string EarnedOn,
    string AccessibleDescription);

/// <summary>
/// The Achievements screen, built from the active profile's own logged training.
/// </summary>
/// <remarks>
/// <para>
/// Progress on a locked badge is measured, never estimated: the ring and the "3 of 4" beside it
/// come from the same count, so the shape can always be described in words. That is the same rule
/// the Progress feature applies to charts.
/// </para>
/// <para>
/// The card states why each badge is good for the person. A badge whose rationale cannot be
/// written down plainly is one that should not exist, and putting the reason on the card is what
/// keeps that check in front of whoever adds the next one.
/// </para>
/// </remarks>
public sealed partial class AchievementsPageViewModel : ObservableObject
{
    private readonly IEngagementDataService engagement;

    /// <summary>Creates the view model.</summary>
    /// <param name="engagement">The engagement data service.</param>
    /// <exception cref="ArgumentNullException"><paramref name="engagement"/> is <see langword="null"/>.</exception>
    public AchievementsPageViewModel(IEngagementDataService engagement)
    {
        ArgumentNullException.ThrowIfNull(engagement);

        this.engagement = engagement;
        Achievements = [];
        summary = string.Empty;
        celebration = string.Empty;
    }

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasAchievements;

    [ObservableProperty]
    private bool hasNoAchievements = true;

    [ObservableProperty]
    private bool gamificationEnabled = true;

    [ObservableProperty]
    private string summary;

    [ObservableProperty]
    private string celebration;

    [ObservableProperty]
    private bool hasCelebration;

    /// <summary>Every badge, earned ones first and then the nearest.</summary>
    public ObservableCollection<AchievementCardViewModel> Achievements { get; }

    /// <summary>Raised when the user asks to share an earned badge.</summary>
    public event EventHandler<AchievementCardViewModel>? ShareRequested;

    /// <summary>Loads the screen from the active profile's own data.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the screen is populated.</returns>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            Apply(await engagement.RefreshAsync(DateOnly.FromDateTime(DateTime.Now), cancellationToken));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Shares an earned badge, after re-checking the copy.
    /// </summary>
    /// <remarks>
    /// The check is repeated here rather than trusted from the definition because a share leaves
    /// the device. Copy that only the owner sees is a product problem; copy that reaches somebody
    /// else's screen is a public one.
    /// </remarks>
    /// <param name="achievement">The card to share.</param>
    [RelayCommand]
    private void Share(AchievementCardViewModel? achievement)
    {
        if (achievement is { IsUnlocked: true } && EngagementEthicsPolicy.IsPublishable(achievement.Description))
        {
            ShareRequested?.Invoke(this, achievement);
        }
    }

    private void Apply(EngagementSnapshot snapshot)
    {
        GamificationEnabled = snapshot.GamificationEnabled;

        Achievements.Clear();
        foreach (var status in snapshot.Achievements)
        {
            Achievements.Add(ToCard(status));
        }

        var earned = Achievements.Count(card => card.IsUnlocked);
        HasAchievements = earned > 0;
        HasNoAchievements = !HasAchievements;

        Summary = snapshot switch
        {
            { HasProfile: false } => "No profile is active on this device yet, so there is nothing to measure. Finish setting up Forge and this fills in from your own training.",
            { GamificationEnabled: false } => EngagementEthicsPolicy.GamificationDisablementMessage,
            _ when Achievements.Count == 0 => "No badges are defined.",
            _ => $"{earned} of {Achievements.Count} earned from your own logged training. Everything stays on this device.",
        };

        HasCelebration = snapshot.NewlyEarned.Count > 0;
        Celebration = snapshot.NewlyEarned.Count switch
        {
            0 => string.Empty,
            1 => $"New: {snapshot.NewlyEarned[0].Title}.",
            _ => $"New: {string.Join(", ", snapshot.NewlyEarned.Select(definition => definition.Title))}.",
        };
    }

    private static AchievementCardViewModel ToCard(AchievementStatus status)
    {
        var definition = status.Definition;
        var earnedOn = status.UnlockedUtc is { } when
            ? $"Earned {when.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture)}"
            : $"Progress: {status.ProgressDetail}";

        return new AchievementCardViewModel(
            definition.Code,
            definition.Title,
            CategoryLabel(definition.Category),
            definition.Description,
            definition.WhyItMatters,
            status.Progress,
            status.ProgressDetail,
            status.IsUnlocked,
            earnedOn,
            $"{definition.Title}. {CategoryLabel(definition.Category)}. {definition.Description} {earnedOn}.");
    }

    private static string CategoryLabel(AchievementCategory category) => category switch
    {
        AchievementCategory.Consistency => "Consistency",
        AchievementCategory.Recovery => "Recovery",
        AchievementCategory.Progression => "Progression",
        AchievementCategory.Exploration => "Exploration",
        AchievementCategory.OwnGoals => "Your own goals",
        _ => "Training",
    };
}
