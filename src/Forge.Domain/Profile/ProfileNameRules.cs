namespace Forge.Domain.Profile;

/// <summary>
/// Validates the name a profile is identified by on a shared device.
/// </summary>
/// <remarks>
/// On a single-profile device a display name is decoration. On a shared one it is the only thing
/// distinguishing two people before a set is logged against one of them, so it is validated rather
/// than merely stored. Two profiles called "Alex" is not an aesthetic problem; it is how somebody
/// ends up with a training history that is not theirs.
/// </remarks>
public static class ProfileNameRules
{
    /// <summary>The longest name a profile may have, matching the persisted column.</summary>
    public const int MaximumLength = 120;

    /// <summary>Trims and collapses whitespace so stored names compare predictably.</summary>
    /// <param name="name">The name as typed.</param>
    /// <returns>The normalised name, which may be empty.</returns>
    public static string Normalise(string? name) =>
        string.IsNullOrWhiteSpace(name) ? string.Empty : string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Checks a proposed name against every other profile on the device.</summary>
    /// <param name="name">The name as typed.</param>
    /// <param name="existingProfiles">Every stored profile, including the one being renamed.</param>
    /// <param name="profileId">The profile being renamed, or <see langword="null"/> when creating.</param>
    /// <returns>The outcome, carrying the normalised name when it is accepted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="existingProfiles"/> is <see langword="null"/>.</exception>
    public static ProfileNameResult Validate(string? name, IEnumerable<UserProfile> existingProfiles, Guid? profileId = null)
    {
        ArgumentNullException.ThrowIfNull(existingProfiles);

        var normalised = Normalise(name);

        if (normalised.Length == 0)
        {
            return ProfileNameResult.Rejected("Give the profile a name so it can be told apart from the others.");
        }

        if (normalised.Length > MaximumLength)
        {
            return ProfileNameResult.Rejected($"Keep the name to {MaximumLength} characters or fewer.");
        }

        var clash = existingProfiles.Any(profile =>
            !profile.IsDeleted
            && profile.Id != profileId
            && string.Equals(profile.DisplayName, normalised, StringComparison.OrdinalIgnoreCase));

        return clash
            ? ProfileNameResult.Rejected($"This device already has a profile called \"{normalised}\". Two identical names is how a set gets logged against the wrong person.")
            : ProfileNameResult.Accepted(normalised);
    }
}

/// <summary>The outcome of validating a profile name.</summary>
/// <param name="IsAccepted">Whether the name may be used.</param>
/// <param name="Name">The normalised name, empty when rejected.</param>
/// <param name="Problem">Why the name was rejected, empty when accepted.</param>
public sealed record ProfileNameResult(bool IsAccepted, string Name, string Problem)
{
    /// <summary>Creates an accepted result.</summary>
    /// <param name="name">The normalised name.</param>
    /// <returns>An accepted result.</returns>
    public static ProfileNameResult Accepted(string name) => new(true, name, string.Empty);

    /// <summary>Creates a rejected result.</summary>
    /// <param name="problem">What the user needs to change, in their words.</param>
    /// <returns>A rejected result.</returns>
    public static ProfileNameResult Rejected(string problem) => new(false, string.Empty, problem);
}
