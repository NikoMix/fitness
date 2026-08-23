using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Insights.Services;
using Forge.Core.Abstractions;
using Forge.Core.Abstractions.Preferences;
using Forge.Domain.Measurement;
using Forge.Domain.Profile;
using Microsoft.Extensions.Logging;

namespace Forge.App.Features.Insights.ViewModels;

/// <summary>
/// The form that records a body measurement.
/// </summary>
/// <remarks>
/// <para>
/// Until this existed, the only way to record a weight was to re-run the whole six-step onboarding
/// wizard: body-metric history, the chart, the change-since-last delta and the unit formatting were
/// all built and working with no way to add a data point to any of them.
/// </para>
/// <para>
/// Input is taken in <b>the user's own unit</b> and converted here, at the one place the preference
/// is known. Labelling the field from <see cref="IUnitFormatter.MassUnitSuffix"/> rather than
/// writing "kg" is the whole point: somebody on imperial who reads "kg", types their weight in
/// pounds and is stored as 84 kg has been given a silently wrong history by a label.
/// </para>
/// <para>
/// The text is kept as text rather than bound to a numeric editor so that "not filled in" and
/// "zero" stay distinguishable. Body fat and waist are optional, and a numeric editor would report
/// an untouched optional field as 0 - which for body fat is a measurement nobody has.
/// </para>
/// </remarks>
public sealed partial class BodyMetricEntryForm : ObservableObject
{
    // Bounds are checked in kilograms and centimetres after conversion, so one set of limits covers
    // both unit systems. They are wide on purpose: this is a guard against a mistyped digit, not a
    // judgement about anybody's body.
    private const double MinimumKilograms = 20;
    private const double MaximumKilograms = 400;
    private const double MinimumWaistCentimetres = 30;
    private const double MaximumWaistCentimetres = 250;
    private const double MinimumBodyFatPercent = 3;
    private const double MaximumBodyFatPercent = 70;

    private readonly IInsightsDataService dataService;
    private readonly IUnitFormatter units;
    private readonly ILogger logger;
    private readonly Func<CancellationToken, Task> onSaved;

    /// <summary>Initialises the form.</summary>
    /// <param name="dataService">Writes the entry to local storage.</param>
    /// <param name="units">Supplies the user's unit and the conversions into storage units.</param>
    /// <param name="logger">Receives exceptions, which never reach the screen.</param>
    /// <param name="onSaved">Invoked after a successful save so the trend can be reloaded.</param>
    public BodyMetricEntryForm(
        IInsightsDataService dataService,
        IUnitFormatter units,
        ILogger logger,
        Func<CancellationToken, Task> onSaved)
    {
        this.dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        this.units = units ?? throw new ArgumentNullException(nameof(units));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.onSaved = onSaved ?? throw new ArgumentNullException(nameof(onSaved));

        RefreshUnitLabels();
    }

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private DateTime date = DateTime.Today;

    [ObservableProperty]
    private string weightText = string.Empty;

    [ObservableProperty]
    private string bodyFatText = string.Empty;

    [ObservableProperty]
    private string waistText = string.Empty;

    [ObservableProperty]
    private string weightLabel = "Weight";

    [ObservableProperty]
    private string waistLabel = "Waist (optional)";

    [ObservableProperty]
    private string weightDescription = "Weight";

    [ObservableProperty]
    private string waistDescription = "Waist circumference, optional";

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool hasStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool isSaving;

    /// <summary>Whether the save button should accept a tap.</summary>
    /// <remarks>
    /// Exposed as a positive property rather than inverting <see cref="IsSaving"/> with a converter
    /// in XAML, because a missing converter resource fails at runtime on a device and leaves the
    /// binding at its fallback - which for <c>IsEnabled</c> is <see langword="true"/>, the exact
    /// value that would let somebody double-submit.
    /// </remarks>
    public bool CanSave => !IsSaving;

    // CA1822 wants these static because they touch no instance state. They must stay instance
    // members: XAML resolves a {Binding Entry.MaximumDate} path against the instance, and a static
    // property is not found there, so the DateEdit would silently lose its bounds and accept a
    // future date. An analyzer suggestion is not worth a date picker that lies about its range.
#pragma warning disable CA1822
    /// <summary>The latest date that can be recorded. Nobody has weighed themselves tomorrow.</summary>
    public DateTime MaximumDate => DateTime.Today;

    /// <summary>The earliest date the picker offers.</summary>
    public DateTime MinimumDate => DateTime.Today.AddYears(-5);
#pragma warning restore CA1822

    /// <summary>Re-reads the unit preference, which the user may have changed while away.</summary>
    public void RefreshUnitLabels()
    {
        WeightLabel = $"Weight ({units.MassUnitSuffix})";
        WaistLabel = $"Waist ({units.CircumferenceUnitSuffix}, optional)";
        WeightDescription = units.MassUnitSuffix == "lb" ? "Weight in pounds" : "Weight in kilograms";
        WaistDescription = units.CircumferenceUnitSuffix == "in"
            ? "Waist circumference in inches, optional"
            : "Waist circumference in centimetres, optional";
    }

    /// <summary>Opens the form on today's date with empty fields.</summary>
    [RelayCommand]
    public void Open()
    {
        Date = DateTime.Today;
        WeightText = string.Empty;
        BodyFatText = string.Empty;
        WaistText = string.Empty;
        StatusMessage = string.Empty;
        HasStatus = false;
        RefreshUnitLabels();
        IsOpen = true;
    }

    /// <summary>Closes the form without writing anything.</summary>
    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        StatusMessage = string.Empty;
        HasStatus = false;
    }

    /// <summary>
    /// Reads the typed values and records them.
    /// </summary>
    /// <remarks>
    /// Validation happens before anything is written, and it reports the first problem in the
    /// order the fields appear on screen, so the message always refers to a field the user can see
    /// without scrolling past a second error.
    /// </remarks>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (IsSaving)
        {
            return;
        }

        if (!TryBuildEntry(out var entry, out var problem))
        {
            Report(problem);
            return;
        }

        IsSaving = true;
        try
        {
            var result = await dataService.SaveBodyMetricAsync(entry, cancellationToken).ConfigureAwait(false);

            switch (result)
            {
                case BodyMetricSaveResult.Added:
                case BodyMetricSaveResult.Replaced:
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        IsOpen = false;
                        Report(result == BodyMetricSaveResult.Added
                            ? $"Saved {units.FormatMass((double)entry.Weight.Kilograms, 1)} for {entry.Date:d MMM yyyy}."
                            : $"Replaced the entry for {entry.Date:d MMM yyyy} with {units.FormatMass((double)entry.Weight.Kilograms, 1)}.");
                    });

                    await onSaved(cancellationToken).ConfigureAwait(false);
                    break;

                case BodyMetricSaveResult.NoActiveProfile:
                    // Fail-closed scoping means a row written now would be owned by nobody and
                    // invisible to every read. Saying so beats a save that silently vanishes.
                    Report("Forge could not tell which profile is active, so nothing was saved. Reopen the app and try again.");
                    break;

                default:
                    Report("Forge could not read that weight, so nothing was saved.");
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Never interpolated. A raw EF message reached a user on the workout summary once.
            LogSaveFailed(logger, exception);
            Report(ForgeUserFacingException.DescribeFor(
                exception,
                "Forge could not save that entry. Nothing was changed — try again in a moment."));
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsSaving = false);
        }
    }

    /// <summary>Reads the typed text into a storage-unit entry.</summary>
    /// <param name="entry">The entry, when this returns <see langword="true"/>.</param>
    /// <param name="problem">What to tell the user, when this returns <see langword="false"/>.</param>
    /// <returns>Whether every filled field could be read.</returns>
    internal bool TryBuildEntry(out BodyMetricEntry entry, out string problem)
    {
        entry = null!;
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(WeightText))
        {
            problem = $"Enter a weight in {units.MassUnitSuffix}.";
            return false;
        }

        if (!MeasurementEntry.TryParse(WeightText, 0.1, 5000, out var typedWeight))
        {
            problem = $"Forge could not read that weight. Enter a number, for example {ExampleWeight()}.";
            return false;
        }

        var kilograms = units.ToKilograms(typedWeight);
        if (kilograms < MinimumKilograms || kilograms > MaximumKilograms)
        {
            problem = $"That weight is outside the range Forge accepts ({units.FormatMass(MinimumKilograms, 0)} to {units.FormatMass(MaximumKilograms, 0)}).";
            return false;
        }

        Percentage? bodyFat = null;
        if (!string.IsNullOrWhiteSpace(BodyFatText))
        {
            if (!MeasurementEntry.TryParse(BodyFatText, MinimumBodyFatPercent, MaximumBodyFatPercent, out var percent))
            {
                problem = $"Body fat should be a percentage between {MinimumBodyFatPercent:0} and {MaximumBodyFatPercent:0}, or left blank.";
                return false;
            }

            bodyFat = Percentage.FromValue(decimal.Round((decimal)percent, 2));
        }

        Length? waist = null;
        if (!string.IsNullOrWhiteSpace(WaistText))
        {
            if (!MeasurementEntry.TryParse(WaistText, 0.1, 5000, out var typedWaist))
            {
                problem = $"Forge could not read that waist measurement. Enter a number in {units.CircumferenceUnitSuffix}, or leave it blank.";
                return false;
            }

            var centimetres = units.ToCentimeters(typedWaist);
            if (centimetres < MinimumWaistCentimetres || centimetres > MaximumWaistCentimetres)
            {
                problem = $"That waist measurement is outside the range Forge accepts ({units.FormatCircumference(MinimumWaistCentimetres)} to {units.FormatCircumference(MaximumWaistCentimetres)}).";
                return false;
            }

            waist = Length.FromCentimetres(decimal.Round((decimal)centimetres, 2));
        }

        var chosen = DateOnly.FromDateTime(Date);
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (chosen > today)
        {
            problem = "Pick today or a past date.";
            return false;
        }

        entry = new BodyMetricEntry(
            chosen,
            Mass.FromKilograms(decimal.Round((decimal)kilograms, 3)),
            bodyFat,
            waist);

        return true;
    }

    private string ExampleWeight() => units.FormatMass(units.MassUnitSuffix == "lb" ? 81.6 : 82.4, 1);

    private void Report(string message)
    {
        StatusMessage = message;
        HasStatus = !string.IsNullOrWhiteSpace(message);
    }

    // Source-generated so CA1848 is satisfied; the codebase uses [LoggerMessage] elsewhere for the
    // same reason. See ForgeStartup.cs.
    [LoggerMessage(EventId = 1510, Level = LogLevel.Error, Message = "Saving a body metric failed.")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);
}
