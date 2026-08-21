using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;
using Forge.Core.Abstractions.Preferences;
using Forge.Domain.Measurement;
using Forge.Domain.Onboarding;
using Forge.Domain.Profile;

namespace Forge.App.Features.Profile;

/// <summary>
/// The profile summary: what Forge knows, what it is still missing, and the body-metric history.
/// </summary>
/// <remarks>
/// The completion ring is calculated from the stored profile rather than being a fixed decorative
/// value. A ring that always reads 60% is worse than no ring: it looks like information, and it is
/// the single most visible thing on the screen.
/// </remarks>
public sealed partial class ProfileViewModel : ObservableObject
{
    private readonly ProfileStore? profileStore;
    private readonly IUnitFormatter? formatter;

    /// <summary>Initialises an instance with no persistence, used by the XAML designer.</summary>
    public ProfileViewModel()
    {
    }

    /// <summary>Initialises the view model.</summary>
    /// <param name="profileStore">Reads and writes the local profile.</param>
    /// <param name="formatter">Formats stored metric values in the user's chosen units.</param>
    public ProfileViewModel(ProfileStore profileStore, IUnitFormatter formatter)
    {
        this.profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    }

    /// <summary>The latest measurements, as label and value pairs.</summary>
    public ObservableCollection<ProfileMetric> BodyMetrics { get; } = [];

    /// <summary>Weight history, newest first.</summary>
    public ObservableCollection<ProfileHistoryEntry> WeightHistory { get; } = [];

    /// <summary>Answers the profile is still missing, each with the reason Forge asks.</summary>
    public ObservableCollection<ProfileGapViewModel> Gaps { get; } = [];

    /// <summary>Whether no weight has ever been recorded.</summary>
    public bool HasNoWeightHistory => !HasWeightHistory;

    /// <summary>Display name shown on the profile card.</summary>
    [ObservableProperty]
    private string displayName = "Local profile";

    /// <summary>Goal summary.</summary>
    [ObservableProperty]
    private string goalSummary = "No goal set yet";

    /// <summary>Current training setup summary.</summary>
    [ObservableProperty]
    private string trainingSummary = "No training setup yet";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private bool hasProfile;

    [ObservableProperty]
    private bool hasWeightHistory;

    [ObservableProperty]
    private bool hasGaps;    [ObservableProperty]
    private double completionProgress;

    [ObservableProperty]
    private string completionSummary = string.Empty;

    [ObservableProperty]
    private string completionDescription = string.Empty;

    [ObservableProperty]
    private string editActionText = "Edit profile";

    partial void OnHasWeightHistoryChanged(bool value) => OnPropertyChanged(nameof(HasNoWeightHistory));

    /// <summary>Reads the profile and rebuilds the screen.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes once the screen has been rebuilt.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (profileStore is null || formatter is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var snapshot = await profileStore.LoadAsync(cancellationToken).ConfigureAwait(true);

            BodyMetrics.Clear();
            WeightHistory.Clear();
            Gaps.Clear();
            HasWeightHistory = false;

            IsEmpty = snapshot is null;
            HasProfile = snapshot is not null;

            var latestMetric = snapshot?.BodyMetrics.Count > 0 ? snapshot.BodyMetrics[0] : null;
            var completion = ProfileCompletionCalculator.Evaluate(snapshot?.Profile, latestMetric);

            CompletionProgress = completion.Fraction;
            CompletionSummary = completion.Summary;
            CompletionDescription = DescribeCompletion(completion);

            foreach (var gap in completion.Gaps)
            {
                Gaps.Add(new ProfileGapViewModel(gap.Label, gap.Reason));
            }

            HasGaps = Gaps.Count > 0;
            EditActionText = completion.IsComplete ? "Edit profile" : "Finish setup";

            if (snapshot is null)
            {
                DisplayName = "Local profile";
                GoalSummary = "No goal set yet";
                TrainingSummary = "Complete onboarding to personalise training.";
                return;
            }

            DisplayName = DescribeName(snapshot.Profile.DisplayName);
            GoalSummary = FormatGoal(snapshot.Profile);
            TrainingSummary = FormatTraining(snapshot.Profile);

            BuildMetrics(snapshot.Profile, latestMetric, formatter);
            BuildHistory(snapshot.BodyMetrics, formatter);
        }
        catch (InvalidOperationException)
        {
            BodyMetrics.Clear();
            WeightHistory.Clear();
            Gaps.Clear();
            IsEmpty = true;
            HasProfile = false;
            HasGaps = false;
            HasWeightHistory = false;
            CompletionProgress = 0d;
            DisplayName = "Local profile";
            GoalSummary = "Local storage is still starting";
            TrainingSummary = "Try again in a moment.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Opens the goal wizard, pre-filled from the stored profile.</summary>
    [RelayCommand]
    private static Task EditProfileAsync() => Shell.Current.GoToAsync(ForgeRoutes.GoalWizard);

    /// <summary>Opens the Settings route owned by the Settings feature.</summary>
    [RelayCommand]
    private static Task OpenSettingsAsync() => Shell.Current.GoToAsync(ForgeRoutes.Settings);

    /// <summary>Opens the body-metric trend surface owned by the Insights feature.</summary>
    [RelayCommand]
    private static Task OpenBodyMetricsAsync() => Shell.Current.GoToAsync(ForgeRoutes.BodyMetrics);

    private void BuildMetrics(UserProfile profile, BodyMetric? latestMetric, IUnitFormatter unitFormatter)
    {
        BodyMetrics.Add(new ProfileMetric(
            "Height",
            profile.Height > Length.Zero ? unitFormatter.FormatLength((double)profile.Height.Centimetres) : "Not set",
            "Used for energy estimates"));

        if (latestMetric is null)
        {
            BodyMetrics.Add(new ProfileMetric("Weight", "Not recorded", "Add one entry to start every trend"));
            return;
        }

        BodyMetrics.Add(new ProfileMetric(
            "Weight",
            unitFormatter.FormatMass((double)latestMetric.Weight.Kilograms),
            string.Create(CultureInfo.CurrentCulture, $"Recorded {latestMetric.RecordedUtc.LocalDateTime:d MMM yyyy}")));

        BodyMetrics.Add(new ProfileMetric(
            "Body fat",
            latestMetric.BodyFatPercentage is { } bodyFat
                ? string.Create(CultureInfo.CurrentCulture, $"{bodyFat.Value:0.#}%")
                : "Not set",
            "Optional percentage"));

        BodyMetrics.Add(new ProfileMetric(
            "Waist",
            latestMetric.WaistCircumference is { } waist
                ? unitFormatter.FormatLength((double)waist.Centimetres)
                : "Not set",
            "Optional circumference"));
    }

    private void BuildHistory(IReadOnlyList<BodyMetric> metrics, IUnitFormatter unitFormatter)
    {
        // Newest first, and each row states the change from the entry before it. A bare list of
        // weights makes the reader do the subtraction; the delta is the only part anyone wanted.
        var weighed = metrics
            .Where(metric => metric.Weight > Mass.Zero)
            .OrderByDescending(metric => metric.RecordedUtc)
            .Take(MaximumHistoryRows)
            .ToList();

        for (var index = 0; index < weighed.Count; index++)
        {
            var metric = weighed[index];
            var previous = index + 1 < weighed.Count ? weighed[index + 1] : null;
            var change = previous is null
                ? "First entry"
                : DescribeChange(metric.Weight.Kilograms - previous.Weight.Kilograms, unitFormatter);

            WeightHistory.Add(new ProfileHistoryEntry(
                metric.RecordedUtc.LocalDateTime.ToString("d MMM yyyy", CultureInfo.CurrentCulture),
                unitFormatter.FormatMass((double)metric.Weight.Kilograms),
                change));
        }

        HasWeightHistory = WeightHistory.Count > 0;
    }

    // Enough to show a trend without turning the profile summary into the Progress screen, which
    // owns full charting.
    private const int MaximumHistoryRows = 12;

    private static string DescribeChange(decimal deltaKilograms, IUnitFormatter unitFormatter)
    {
        if (deltaKilograms == 0m)
        {
            return "No change";
        }

        var magnitude = unitFormatter.FormatMass((double)Math.Abs(deltaKilograms), 2);
        return deltaKilograms > 0m ? $"Up {magnitude}" : $"Down {magnitude}";
    }

    private static string DescribeName(string storedName)
        => string.IsNullOrWhiteSpace(storedName)
            || string.Equals(storedName, ProfileCompletionCalculator.PlaceholderDisplayName, StringComparison.OrdinalIgnoreCase)
                ? "Local profile"
                : storedName;

    private static string DescribeCompletion(ProfileCompletion completion)
    {
        if (!completion.ProfileExists)
        {
            return "Nothing is stored on this device yet.";
        }

        return completion.IsComplete
            ? "Forge has everything it asks for. Anything else you add sharpens the estimates."
            : $"{completion.Summary}. The rest is optional, but each one makes the numbers on Today more specific.";
    }

    private static string FormatGoal(UserProfile profile)
    {
        var goal = ProfileLabels.Describe(profile.Goal);
        if (profile.TargetWeight is not { } target)
        {
            return goal;
        }

        var timeframe = profile.GoalTimeframeWeeks is > 0
            ? string.Create(CultureInfo.CurrentCulture, $" over {profile.GoalTimeframeWeeks} weeks")
            : string.Empty;

        return string.Create(CultureInfo.CurrentCulture, $"{goal} · target {target.Kilograms:0.#} kg{timeframe}");
    }

    private static string FormatTraining(UserProfile profile)
    {
        var equipment = string.IsNullOrWhiteSpace(profile.AvailableEquipment) ? "No equipment listed" : profile.AvailableEquipment;
        return string.Create(
            CultureInfo.CurrentCulture,
            $"{profile.TrainingDaysPerWeek} days/week · {ProfileLabels.Describe(profile.ExperienceLevel)} · {equipment}");
    }
}

/// <summary>A profile metric row.</summary>
/// <param name="Name">What the value is.</param>
/// <param name="Value">The formatted value.</param>
/// <param name="Detail">Supporting context.</param>
public sealed record ProfileMetric(string Name, string Value, string Detail);

/// <summary>One weight entry in the history list.</summary>
/// <param name="When">Formatted local date.</param>
/// <param name="Value">Formatted weight.</param>
/// <param name="Change">Change against the previous entry.</param>
public sealed record ProfileHistoryEntry(string When, string Value, string Change);

/// <summary>An outstanding profile answer, shown with the reason Forge asks for it.</summary>
/// <param name="Label">What is missing.</param>
/// <param name="Reason">Why it helps.</param>
public sealed record ProfileGapViewModel(string Label, string Reason);
