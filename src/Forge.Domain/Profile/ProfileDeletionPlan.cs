namespace Forge.Domain.Profile;

/// <summary>One line of a deletion preview.</summary>
/// <param name="Name">The kind of data.</param>
/// <param name="RecordCount">How many records it covers, or <see langword="null"/> when the count is not attributable.</param>
/// <param name="Detail">What happens to it.</param>
public sealed record ProfileDeletionLine(string Name, int? RecordCount, string Detail);

/// <summary>
/// Exactly what deleting one profile removes, and exactly what it leaves behind.
/// </summary>
/// <remarks>
/// <para>
/// The retained list matters as much as the removed one, and it is the reason this is a modelled
/// plan rather than a confirmation string. Because most of Forge is not profile-separated yet,
/// deleting "Alex" removes Alex's measurements but cannot remove Alex's workout history: those
/// rows carry no owner, so a delete would have to guess, and guessing would destroy the remaining
/// user's training log. Leaving them is the safe choice, but only if the user is told, because
/// somebody deleting a profile for privacy reasons is entitled to know what actually goes.
/// </para>
/// <para>
/// Nothing here is phrased as reassurance. The counts come from the database and the wording
/// states the outcome, so the dialog cannot imply an erasure that did not happen.
/// </para>
/// </remarks>
/// <param name="ProfileId">The profile the plan applies to.</param>
/// <param name="ProfileName">Its display name.</param>
/// <param name="IsPermitted">Whether the delete may proceed.</param>
/// <param name="Refusal">Why it may not, empty when permitted.</param>
/// <param name="Removed">What will be deleted.</param>
/// <param name="Retained">What survives, with the reason.</param>
/// <param name="SuccessorName">The profile that becomes active afterwards, empty when none.</param>
public sealed record ProfileDeletionPlan(
    Guid ProfileId,
    string ProfileName,
    bool IsPermitted,
    string Refusal,
    IReadOnlyList<ProfileDeletionLine> Removed,
    IReadOnlyList<ProfileDeletionLine> Retained,
    string SuccessorName)
{
    /// <summary>How many records the delete removes.</summary>
    public int RemovedRecordCount => Removed.Sum(line => line.RecordCount ?? 0);

    /// <summary>The dialog title.</summary>
    public string Headline => $"Delete \"{ProfileName}\"?";

    /// <summary>
    /// The dialog body, stating what goes, what stays and what becomes active.
    /// </summary>
    /// <returns>Text safe to show verbatim in a confirmation dialog.</returns>
    public string Describe()
    {
        var lines = new List<string>
        {
            RemovedRecordCount == 0
                ? "This profile has no data of its own yet, so nothing is deleted apart from the profile."
                : $"Deletes {DescribeCount(RemovedRecordCount)} belonging only to this profile:",
        };

        lines.AddRange(Removed
            .Where(line => line.RecordCount is > 0)
            .Select(line => $"  \u2022 {line.Name}: {DescribeCount(line.RecordCount!.Value)}"));

        if (Retained.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Kept, because it is not separated by profile on this device and deleting it would remove it for everyone:");
            lines.AddRange(Retained.Select(line => $"  \u2022 {line.Name}"));
        }

        lines.Add(string.Empty);
        lines.Add(string.IsNullOrEmpty(SuccessorName)
            ? "No other profile would remain."
            : $"\"{SuccessorName}\" becomes the active profile.");
        lines.Add("This cannot be undone. Forge has no cloud backup.");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Builds a plan from the profile, its record counts and the successor.</summary>
    /// <param name="profile">The profile being deleted.</param>
    /// <param name="recordCountsByEntityType">Live record counts owned by the profile, keyed by entity type.</param>
    /// <param name="deletableEntityTypes">
    /// The types the caller performing the delete actually removes. An owned area whose types are
    /// not all listed is reported as retained, so a feature that adopts the seam without the delete
    /// being extended cannot cause the dialog to claim an erasure that never happens.
    /// </param>
    /// <param name="successor">The profile that would become active, or <see langword="null"/>.</param>
    /// <param name="refusal">Why the delete is not permitted, or <see langword="null"/> when it is.</param>
    /// <returns>The plan to show before deleting.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    public static ProfileDeletionPlan Create(
        UserProfile profile,
        IReadOnlyDictionary<Type, int> recordCountsByEntityType,
        IReadOnlyCollection<Type> deletableEntityTypes,
        UserProfile? successor,
        string? refusal = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(recordCountsByEntityType);
        ArgumentNullException.ThrowIfNull(deletableEntityTypes);

        var areas = ProfileDataAreas.Describe();

        var removable = areas
            .Where(area => area.Separation == ProfileSeparation.Separated
                && area.EntityTypes.All(deletableEntityTypes.Contains))
            .ToArray();

        var removed = removable
            .Select(area => new ProfileDeletionLine(
                area.Name,
                area.EntityTypes.Sum(type => recordCountsByEntityType.TryGetValue(type, out var count) ? count : 0),
                area.Detail))
            .ToArray();

        var retained = areas
            .Except(removable)
            .Select(area => new ProfileDeletionLine(area.Name, null, area.Detail))
            .ToArray();

        return new ProfileDeletionPlan(
            profile.Id,
            profile.DisplayName,
            string.IsNullOrEmpty(refusal),
            refusal ?? string.Empty,
            removed,
            retained,
            successor?.DisplayName ?? string.Empty);
    }

    private static string DescribeCount(int count) => count == 1 ? "1 record" : $"{count} records";
}
