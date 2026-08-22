using Forge.Domain.Common;
using Forge.Domain.Engagement;
using Forge.Domain.Nutrition;
using Forge.Domain.Nutrition.Barcodes;
using Forge.Domain.Nutrition.Recipes;
using Forge.Domain.Planning;
using Forge.Domain.Recovery;
using Forge.Domain.Training;
using Forge.Domain.Workout;

namespace Forge.Domain.Profile;

/// <summary>Whether a kind of data is kept apart per profile.</summary>
public enum ProfileSeparation
{
    /// <summary>Every record carries an owner, so a scoped query cannot return another profile's rows.</summary>
    Separated,

    /// <summary>Records have no owner, so every profile on the device sees the same data.</summary>
    Shared,
}

/// <summary>
/// One kind of data, and whether switching profile changes what the user sees.
/// </summary>
/// <param name="Name">What a user would call this data.</param>
/// <param name="EntityTypes">The persisted types that make it up.</param>
/// <param name="Detail">Plainly what happens today when profiles are switched.</param>
public sealed record ProfileDataArea(string Name, IReadOnlyList<Type> EntityTypes, string Detail)
{
    /// <summary>
    /// Whether this area is separated per profile.
    /// </summary>
    /// <remarks>
    /// Computed from the entity types rather than declared, so it cannot be stale. An area counts
    /// as separated only when every type in it is owned: a plan whose root is scoped but whose days
    /// are not is still a leak the moment anything reads the children directly.
    /// </remarks>
    public ProfileSeparation Separation =>
        EntityTypes.Count > 0 && EntityTypes.All(type => typeof(IProfileOwned).IsAssignableFrom(type))
            ? ProfileSeparation.Separated
            : ProfileSeparation.Shared;
}

/// <summary>
/// What is and is not separated between profiles on this device.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the profile switcher can be honest. Multi-profile support arrived before every
/// feature adopted <see cref="IProfileOwned"/>, and a switcher that silently implies full
/// separation is worse than no switcher: the user acts on a promise the app is not keeping, and in
/// a health app that means training against somebody else's history or logging a meal onto
/// somebody else's day.
/// </para>
/// <para>
/// Nothing here is hard-coded as "done" or "not done". Each area's state is derived from whether
/// its entity types implement the seam, so migrating a feature updates this screen with no edit
/// here. <c>ProfileDataAreasTests</c> additionally fails if a new persisted type is added without
/// being accounted for, which stops the catalogue quietly falling behind the schema.
/// </para>
/// </remarks>
public static class ProfileDataAreas
{
    /// <summary>
    /// The profile itself, which is excluded from the catalogue.
    /// </summary>
    /// <remarks>
    /// A profile is not data belonging to a profile, so asking whether it is separated is a
    /// category error. It is named here so the completeness test can exclude it deliberately
    /// rather than by forgetting it.
    /// </remarks>
    public static Type ProfileEntityType => typeof(UserProfile);

    private static readonly ProfileDataArea[] AreaList =
    [
        new(
            "Body measurements",
            [typeof(BodyMetric)],
            "Weight, body fat and circumference entries belong to one profile and are never shown to another."),
        new(
            "Workout history",
            [typeof(WorkoutSession), typeof(SetEntry)],
            "Completed sessions and every logged set belong to one profile. A set is recorded against whoever was active when it was logged, and no other profile sees it."),
        new(
            "Workout in progress",
            [typeof(ActiveWorkoutState)],
            "An unfinished workout belongs to the profile that started it. Switching profile mid-session leaves that session with its owner rather than handing it over."),
        new(
            "Training plans",
            [typeof(TrainingPlan), typeof(PlanDay), typeof(PlannedExercise), typeof(PlannedSet)],
            "Plans, their days and their prescribed sets belong to one profile. The shipped templates are shared, and adopting one makes an owned copy."),
        new(
            "Food log",
            [typeof(FoodLogEntry)],
            "Logged meals belong to one profile, so calorie and macro totals count only your own food."),
        new(
            "Hydration",
            [typeof(HydrationEntry)],
            "Drinks belong to one profile, so the hydration ring counts only your own intake."),
        new(
            "Recipes",
            [typeof(Recipe)],
            "Recipes you save belong to you alone. The recipes shipped with Forge are shown to every profile because they are published content, not anybody's data."),
        new(
            "Check-ins and soreness",
            [typeof(MorningCheckIn), typeof(SorenessEntry)],
            "Readiness check-ins and soreness reports belong to one profile, so coaching advice is computed only from your own answers."),
        new(
            "Streaks",
            [typeof(Streak)],
            "Your training rhythm and any period you marked as illness, injury or a deload belong to you alone, and no other profile sees them."),
        new(
            "Achievements",
            [typeof(Achievement)],
            "Badges are earned per profile from that profile's own training, so one person unlocking something does not make it appear for anybody else."),
        new(
            "Exercise library",
            [typeof(Exercise)],
            "The shipped catalogue is shared on purpose. Custom exercises, favourites and recently used are not separated, so those choices are visible to every profile."),
        new(
            "Food catalogue",
            [typeof(FoodItem), typeof(FoodBarcode)],
            "The shipped catalogue is shared on purpose. Foods a user adds themselves, and the barcodes they scan and save, are not separated and appear for every profile."),
    ];

    /// <summary>Every kind of data, separated areas first.</summary>
    /// <returns>The catalogue, ordered so the honest answer is read before the caveats.</returns>
    public static IReadOnlyList<ProfileDataArea> Describe() =>
        [.. AreaList.OrderBy(area => area.Separation == ProfileSeparation.Separated ? 0 : 1).ThenBy(area => area.Name, StringComparer.Ordinal)];

    /// <summary>The areas a profile switch genuinely keeps apart.</summary>
    /// <returns>Only the separated areas.</returns>
    public static IReadOnlyList<ProfileDataArea> Separated() =>
        [.. Describe().Where(area => area.Separation == ProfileSeparation.Separated)];

    /// <summary>The areas every profile on the device still shares.</summary>
    /// <returns>Only the shared areas.</returns>
    public static IReadOnlyList<ProfileDataArea> Shared() =>
        [.. Describe().Where(area => area.Separation == ProfileSeparation.Shared)];

    /// <summary>Whether switching profile changes everything a user would expect it to.</summary>
    public static bool IsFullySeparated => AreaList.All(area => area.Separation == ProfileSeparation.Separated);

    /// <summary>Every persisted type the catalogue accounts for.</summary>
    /// <returns>The union of entity types across all areas.</returns>
    public static IReadOnlyList<Type> CoveredEntityTypes() => [.. AreaList.SelectMany(area => area.EntityTypes).Distinct()];

    /// <summary>
    /// A single sentence stating what a profile switch does and does not do.
    /// </summary>
    /// <returns>Text safe to show verbatim next to the switch action.</returns>
    public static string SummariseSeparation()
    {
        if (IsFullySeparated)
        {
            return "Switching profile changes every screen in Forge. Nothing is shared between profiles on this device.";
        }

        var separated = Separated().Count;
        var shared = Shared().Count;

        return separated == 0
            ? $"Profiles keep their own name, goals and setup. All {shared} other kinds of data on this device are still shared between profiles."
            : $"{separated} of {separated + shared} kinds of data are kept separate. The rest are shared between every profile on this device.";
    }

    /// <summary>
    /// The types a delete must remove to erase one profile without touching another.
    /// </summary>
    /// <remarks>
    /// Only owned types appear. Deleting shared rows during a profile delete would destroy data
    /// belonging to people who did not ask for it, which is a far worse failure than leaving
    /// unattributable rows behind for a later migration to assign.
    /// </remarks>
    /// <returns>Every separated entity type.</returns>
    public static IReadOnlyList<Type> DeletableEntityTypes() =>
        [.. CoveredEntityTypes().Where(type => typeof(IProfileOwned).IsAssignableFrom(type) && typeof(Entity).IsAssignableFrom(type))];
}
