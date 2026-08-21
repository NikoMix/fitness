namespace Forge.Domain.Profile;

/// <summary>
/// Decides which of several local profiles is the active one.
/// </summary>
/// <remarks>
/// <para>
/// Activity is derived from <see cref="UserProfile.LastActivatedUtc"/> rather than stored as a
/// flag on each row. A boolean would allow two profiles to claim to be active at once, and the
/// database has no constraint that could prevent it; the failure would surface as the app showing
/// one person's name above another person's training history. A timestamp cannot represent that
/// state at all, so the invariant holds by construction instead of by discipline.
/// </para>
/// <para>
/// It also degrades correctly. A device that has never switched profile has no timestamps, and the
/// fallback reproduces exactly what Forge did when it supported a single profile: the oldest
/// profile wins. Existing installations therefore keep the profile they already had.
/// </para>
/// </remarks>
public static class ActiveProfileSelector
{
    /// <summary>
    /// The largest number of profiles Forge keeps on one device.
    /// </summary>
    /// <remarks>
    /// A flat switcher list stops being scannable at a glance beyond this, and a device shared by
    /// more people than this is a gym kiosk rather than a personal phone. That is a different
    /// product with different privacy obligations, and it should not arrive by accident.
    /// </remarks>
    public const int MaximumProfiles = 8;

    /// <summary>Selects the active profile from every profile stored on the device.</summary>
    /// <param name="profiles">Every stored profile, including soft-deleted ones.</param>
    /// <returns>The active profile, or <see langword="null"/> when the device has none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profiles"/> is <see langword="null"/>.</exception>
    public static UserProfile? SelectActive(IEnumerable<UserProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var live = Live(profiles);
        if (live.Count == 0)
        {
            return null;
        }

        var activated = live
            .Where(profile => profile.LastActivatedUtc.HasValue)
            .OrderByDescending(profile => profile.LastActivatedUtc!.Value)
            .ThenByDescending(profile => profile.CreatedUtc)
            .ThenByDescending(profile => profile.Id)
            .FirstOrDefault();

        if (activated is not null)
        {
            return activated;
        }

        // Nobody has ever switched. Prefer a personal profile so that a device left on a guest
        // profile does not silently become the demo again after a restart, which would look to the
        // owner like their data had vanished.
        return live
            .OrderBy(profile => profile.Kind == ProfileKind.Guest ? 1 : 0)
            .ThenBy(profile => profile.CreatedUtc)
            .ThenBy(profile => profile.Id)
            .First();
    }

    /// <summary>Selects the scope every profile-owned query should run under.</summary>
    /// <param name="profiles">Every stored profile, including soft-deleted ones.</param>
    /// <returns>The active scope, or <see cref="ProfileScope.None"/> when the device has no profile.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profiles"/> is <see langword="null"/>.</exception>
    public static ProfileScope SelectScope(IEnumerable<UserProfile> profiles)
    {
        var active = SelectActive(profiles);
        return active is null ? ProfileScope.None : ProfileScope.For(active);
    }

    /// <summary>
    /// Orders profiles for display in the switcher.
    /// </summary>
    /// <remarks>
    /// The order does not depend on which profile is active, so switching never reshuffles the
    /// list. On a shared device the row position is how people find themselves, and a list that
    /// reorders under the finger is how somebody taps the wrong person.
    /// </remarks>
    /// <param name="profiles">Every stored profile, including soft-deleted ones.</param>
    /// <returns>Live profiles, personal ones first, each group oldest first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profiles"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<UserProfile> OrderForDisplay(IEnumerable<UserProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        return [.. Live(profiles)
            .OrderBy(profile => profile.Kind == ProfileKind.Guest ? 1 : 0)
            .ThenBy(profile => profile.CreatedUtc)
            .ThenBy(profile => profile.Id)];
    }

    /// <summary>Whether another profile can be added to this device.</summary>
    /// <param name="profiles">Every stored profile, including soft-deleted ones.</param>
    /// <returns><see langword="true"/> when the device is below <see cref="MaximumProfiles"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profiles"/> is <see langword="null"/>.</exception>
    public static bool CanAdd(IEnumerable<UserProfile> profiles) => Live(profiles).Count < MaximumProfiles;

    /// <summary>
    /// Whether a profile may be deleted.
    /// </summary>
    /// <remarks>
    /// The last profile cannot be deleted here. Removing it would leave the app with data it can no
    /// longer attribute to anyone rather than with a clean device, and the flow that genuinely
    /// empties the device is erasure under GDPR Article 17, which also destroys the encryption key.
    /// Offering a second, weaker path to "delete everything" would be a way to lose data while
    /// believing it was erased.
    /// </remarks>
    /// <param name="profiles">Every stored profile, including soft-deleted ones.</param>
    /// <param name="profileId">The profile the user wants to delete.</param>
    /// <returns><see langword="true"/> when the profile exists and is not the only one left.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profiles"/> is <see langword="null"/>.</exception>
    public static bool CanDelete(IEnumerable<UserProfile> profiles, Guid profileId)
    {
        var live = Live(profiles);
        return live.Count > 1 && live.Any(profile => profile.Id == profileId);
    }

    /// <summary>
    /// Chooses which profile becomes active after another one is deleted.
    /// </summary>
    /// <param name="profiles">Every stored profile, including soft-deleted ones.</param>
    /// <param name="deletedProfileId">The profile being removed.</param>
    /// <returns>The profile to activate, or <see langword="null"/> when none would remain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profiles"/> is <see langword="null"/>.</exception>
    public static UserProfile? SelectSuccessor(IEnumerable<UserProfile> profiles, Guid deletedProfileId)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        return SelectActive(profiles.Where(profile => profile.Id != deletedProfileId));
    }

    private static IReadOnlyList<UserProfile> Live(IEnumerable<UserProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        return [.. profiles.Where(profile => !profile.IsDeleted)];
    }
}
