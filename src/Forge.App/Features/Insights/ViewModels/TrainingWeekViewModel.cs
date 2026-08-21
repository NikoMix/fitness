using System.Globalization;
using Forge.Domain.Analytics;

namespace Forge.App.Features.Insights.ViewModels;

/// <summary>One week of volume and intensity, formatted for display.</summary>
/// <param name="WeekLabel">Short label for the week, for example "17 Aug".</param>
/// <param name="VolumeKilograms">Working volume in the week.</param>
/// <param name="MeanLoadKilograms">Repetition-weighted mean load across loaded sets.</param>
/// <param name="HasLoadedSets">Whether any set in the week carried external load.</param>
/// <param name="Detail">One line describing the week in full.</param>
/// <remarks>
/// Shared by the Progress overview and the per-muscle and per-pattern breakdowns on Insights, so
/// a week reads identically wherever it appears.
/// </remarks>
public sealed record TrainingWeekViewModel(
    string WeekLabel,
    double VolumeKilograms,
    double MeanLoadKilograms,
    bool HasLoadedSets,
    string Detail)
{
    /// <summary>Projects an aggregated week into display form.</summary>
    /// <param name="week">The aggregated week.</param>
    /// <returns>The display model.</returns>
    public static TrainingWeekViewModel From(TrainingWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);

        var detail = week.LoadedWorkingSets > 0
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{week.Volume.Kilograms:0.##} kg over {week.WorkingSets} sets · mean load {week.MeanLoad.Kilograms:0.##} kg · heaviest {week.HeaviestLoad.Kilograms:0.##} kg")
            : string.Create(
                CultureInfo.CurrentCulture,
                $"{week.Volume.Kilograms:0.##} kg over {week.WorkingSets} sets · bodyweight only, so no mean load");

        return new TrainingWeekViewModel(
            week.WeekStarting.ToString("d MMM", CultureInfo.CurrentCulture),
            (double)week.Volume.Kilograms,
            (double)week.MeanLoad.Kilograms,
            week.LoadedWorkingSets > 0,
            detail);
    }
}
