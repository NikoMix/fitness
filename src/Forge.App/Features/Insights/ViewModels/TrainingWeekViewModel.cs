using System.Globalization;
using Forge.Core.Abstractions.Preferences;
using Forge.Domain.Analytics;

namespace Forge.App.Features.Insights.ViewModels;

/// <summary>One week of volume and intensity, formatted for display.</summary>
/// <param name="WeekLabel">Short label for the week, for example "17 Aug".</param>
/// <param name="VolumeKilograms">Working volume in the week, in canonical kilograms.</param>
/// <param name="MeanLoadKilograms">Repetition-weighted mean load across loaded sets, in canonical kilograms.</param>
/// <param name="Volume">Working volume in the unit the user reads.</param>
/// <param name="MeanLoad">Mean load in the unit the user reads.</param>
/// <param name="HasLoadedSets">Whether any set in the week carried external load.</param>
/// <param name="Detail">One line describing the week in full.</param>
/// <remarks>
/// <para>
/// Shared by the Progress overview and the per-muscle and per-pattern breakdowns on Insights, so
/// a week reads identically wherever it appears.
/// </para>
/// <para>
/// The canonical kilogram values and the display values are separate members rather than one
/// converted field. A property called <c>VolumeKilograms</c> holding pounds is exactly the kind of
/// thing that survives review and is then read as kilograms by the next caller; keeping both means
/// every binding site has to state which one it wanted.
/// </para>
/// </remarks>
public sealed record TrainingWeekViewModel(
    string WeekLabel,
    double VolumeKilograms,
    double MeanLoadKilograms,
    double Volume,
    double MeanLoad,
    bool HasLoadedSets,
    string Detail)
{
    /// <summary>
    /// Projects an aggregated week into display form, worded in canonical kilograms.
    /// </summary>
    /// <remarks>
    /// Retained for the Progress screen, which another stream owns and which still renders
    /// kilograms unconditionally. Prefer <see cref="From(TrainingWeek, IUnitFormatter)"/>: this
    /// overload states kilograms whatever the user chose, which is the defect being swept, and it
    /// exists only so that sweep can land as its own change.
    /// </remarks>
    /// <param name="week">The aggregated week.</param>
    /// <returns>The display model, worded in kilograms.</returns>
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
            (double)week.Volume.Kilograms,
            (double)week.MeanLoad.Kilograms,
            week.LoadedWorkingSets > 0,
            detail);
    }

    /// <summary>Projects an aggregated week into display form, in the user's chosen unit.</summary>
    /// <param name="week">The aggregated week.</param>
    /// <param name="units">Converts and formats the stored kilograms.</param>
    /// <returns>The display model.</returns>
    public static TrainingWeekViewModel From(TrainingWeek week, IUnitFormatter units)
    {
        ArgumentNullException.ThrowIfNull(week);
        ArgumentNullException.ThrowIfNull(units);

        var volume = units.FormatMass((double)week.Volume.Kilograms, 2);
        var detail = week.LoadedWorkingSets > 0
            ? $"{volume} over {week.WorkingSets} sets · mean load {units.FormatMass((double)week.MeanLoad.Kilograms, 2)} · heaviest {units.FormatMass((double)week.HeaviestLoad.Kilograms, 2)}"
            : $"{volume} over {week.WorkingSets} sets · bodyweight only, so no mean load";

        return new TrainingWeekViewModel(
            week.WeekStarting.ToString("d MMM", CultureInfo.CurrentCulture),
            (double)week.Volume.Kilograms,
            (double)week.MeanLoad.Kilograms,
            units.ToDisplayMass((double)week.Volume.Kilograms),
            units.ToDisplayMass((double)week.MeanLoad.Kilograms),
            week.LoadedWorkingSets > 0,
            detail);
    }
}
