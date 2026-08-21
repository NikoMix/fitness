using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Services.Health;
using Forge.Core.Abstractions.Health;

namespace Forge.App.Features.Health.ViewModels;

/// <summary>One health category row, as the screen renders it.</summary>
/// <param name="DisplayName">Category name.</param>
/// <param name="Purpose">Why Forge asks for it.</param>
/// <param name="StatusLabel">Short status word.</param>
/// <param name="Explanation">Full-sentence explanation, honest about what is unknown.</param>
/// <param name="LastSyncLabel">When the category last produced data.</param>
/// <param name="IsUncertain">Whether the platform refuses to confirm the permission.</param>
public sealed record HealthConnectionRowViewModel(
    string DisplayName,
    string Purpose,
    string StatusLabel,
    string Explanation,
    string LastSyncLabel,
    bool IsUncertain);

/// <summary>
/// Backs the health connections screen.
/// </summary>
/// <remarks>
/// The screen's one job is to be truthful. It shows what each category is for, what the platform
/// has actually confirmed, when data last arrived, and - where the platform will not say - that
/// Forge cannot know. Every state keeps manual entry available, so no branch here is an error path.
/// </remarks>
public sealed partial class HealthConnectionsViewModel : ObservableObject
{
    private readonly HealthConnectionService connections;

    /// <summary>Creates the view model.</summary>
    /// <param name="connections">Health orchestration service.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connections"/> is null.</exception>
    public HealthConnectionsViewModel(HealthConnectionService connections)
    {
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));
        Rows = [];
    }

    /// <summary>The published privacy policy, required by the Play Health Apps declaration.</summary>
    public static string PrivacyPolicyUrl => "https://nikomix.github.io/fitness/privacy/";

    /// <summary>Per-category rows.</summary>
    public ObservableCollection<HealthConnectionRowViewModel> Rows { get; }

    /// <summary>Whether an operation is in flight.</summary>
    [ObservableProperty]
    private bool isBusy = true;

    /// <summary>One-line summary of the connection.</summary>
    [ObservableProperty]
    private string headline = "Checking your health store";

    /// <summary>Paragraph explaining the state, including what Forge cannot know.</summary>
    [ObservableProperty]
    private string explanation =
        "Forge is reading what your device's health store will tell it. Manual entry always works.";

    /// <summary>Whether the store can be connected to at all.</summary>
    [ObservableProperty]
    private bool canConnect;

    /// <summary>Whether at least one category's permission cannot be confirmed by the platform.</summary>
    [ObservableProperty]
    private bool hasUnverifiablePermission;

    /// <summary>Copy shown when the platform will not confirm read permission.</summary>
    [ObservableProperty]
    private string unverifiableExplanation = string.Empty;

    /// <summary>Whether completed workouts can be written back.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CannotWriteWorkouts))]
    private bool canWriteWorkouts;

    /// <summary>Summary of the most recent import, or an explanation of why nothing arrived.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastImportSummary))]
    private string lastImportSummary = string.Empty;

    /// <summary>Whether an import summary is available to show.</summary>
    public bool HasLastImportSummary => !string.IsNullOrEmpty(LastImportSummary);

    /// <summary>Whether workout write-back is currently unavailable.</summary>
    public bool CannotWriteWorkouts => !CanWriteWorkouts;

    /// <summary>Loads current state without prompting for permissions.</summary>
    /// <returns>A task that completes when the screen is populated.</returns>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Apply(await connections.GetSummaryAsync(CancellationToken.None).ConfigureAwait(true));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Requests authorization and imports the recent window.</summary>
    /// <returns>A task that completes when the screen is updated.</returns>
    [RelayCommand]
    public async Task ConnectAsync()
    {
        IsBusy = true;
        try
        {
            var result = await connections.ConnectAsync(CancellationToken.None).ConfigureAwait(true);
            Apply(result.Summary);
            LastImportSummary = DescribeImport(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Re-imports using whatever access already exists.</summary>
    /// <returns>A task that completes when the screen is updated.</returns>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var result = await connections.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            Apply(result.Summary);
            LastImportSummary = DescribeImport(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Clears recorded sync times so the screen stops implying a live link.</summary>
    /// <returns>A task that completes when the screen is updated.</returns>
    [RelayCommand]
    public async Task DisconnectAsync()
    {
        IsBusy = true;
        try
        {
            Apply(await connections.DisconnectAsync(CancellationToken.None).ConfigureAwait(true));
            LastImportSummary = "Forge has forgotten its sync history. Revoke access in your " +
                "device's health settings to stop it reading anything further.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(HealthConnectionSummary summary)
    {
        Headline = summary.Headline;
        Explanation = summary.Explanation;
        CanConnect = summary.CanRequestAuthorization;
        CanWriteWorkouts = summary.CanWriteWorkouts;
        HasUnverifiablePermission = summary.HasUnverifiablePermission;

        UnverifiableExplanation = summary.HasUnverifiablePermission
            ? $"{HealthDataTypeCatalog.DisplayName(summary.Platform)} does not tell apps whether read " +
              "access was granted or refused. Forge will not claim a connection it cannot verify, so " +
              "these categories stay marked as unconfirmed even after you allow them. If data appears " +
              "below, access is working."
            : string.Empty;

        Rows.Clear();
        foreach (var row in summary.Rows)
        {
            Rows.Add(new HealthConnectionRowViewModel(
                row.DisplayName,
                row.Purpose,
                row.StatusLabel,
                row.Explanation,
                row.LastSyncLabel,
                !row.IsPermissionVerifiable && row.Permission is HealthPermissionStatus.Unknown));
        }
    }

    private static string DescribeImport(HealthRefreshResult result)
    {
        if (result.SyncedTypes.Count is 0)
        {
            // Two different facts, and the platform decides which one Forge is entitled to state.
            // Where read permission is confirmed - Health Connect - an empty result genuinely means
            // there is nothing recorded, and hedging about a refusal that did not happen is its own
            // kind of dishonesty. Where it cannot be confirmed - HealthKit - the two are
            // indistinguishable and the hedge is the only truthful answer.
            return result.Summary.HasUnverifiablePermission
                ? "Nothing arrived from your health store. That may mean access was refused, or " +
                  "simply that there is nothing recorded for the last week. Manual entry always works."
                : "Your health store has nothing recorded for the last week. Manual entry always works.";
        }

        var parts = new List<string>();
        var totals = result.Totals;

        if (totals.Steps is { } steps)
        {
            parts.Add($"{steps:N0} steps");
        }

        if (totals.Sleep is { } sleep)
        {
            parts.Add($"{sleep.TotalHours:N1} h sleep");
        }

        if (totals.WaterLitres is { } water)
        {
            parts.Add($"{water:N1} L water");
        }

        if (totals.ActiveEnergyKilocalories is { } energy)
        {
            parts.Add($"{energy:N0} kcal active");
        }

        if (totals.AverageHeartRate is { } heartRate)
        {
            parts.Add($"{heartRate:N0} bpm average");
        }

        if (totals.BodyMassKilograms is { } mass)
        {
            parts.Add($"{mass:N1} kg latest weight");
        }

        return $"Imported from the last 7 days: {string.Join(" · ", parts)}.";
    }
}
