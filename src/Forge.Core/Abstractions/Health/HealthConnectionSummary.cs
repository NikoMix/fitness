namespace Forge.Core.Abstractions.Health;

/// <summary>One row of the health connections screen.</summary>
/// <param name="DataType">The health category.</param>
/// <param name="DisplayName">Label for the category.</param>
/// <param name="Purpose">Why Forge asks for it.</param>
/// <param name="Permission">The permission state the platform reported.</param>
/// <param name="IsPermissionVerifiable">
/// Whether the reported state can be trusted as a fact. False on HealthKit read types, where the
/// platform does not disclose refusal.
/// </param>
/// <param name="LastSyncedUtc">When this category last produced a usable read.</param>
/// <param name="StatusLabel">Short status word for the row.</param>
/// <param name="Explanation">Full-sentence explanation, honest about what is not known.</param>
/// <param name="LastSyncLabel">Human-readable last-sync description.</param>
public sealed record HealthConnectionRow(
    HealthDataType DataType,
    string DisplayName,
    string Purpose,
    HealthPermissionStatus Permission,
    bool IsPermissionVerifiable,
    DateTimeOffset? LastSyncedUtc,
    string StatusLabel,
    string Explanation,
    string LastSyncLabel);

/// <summary>The whole health connections screen, as data.</summary>
/// <param name="Platform">The platform store this build talks to.</param>
/// <param name="Availability">Whether the store can currently be used.</param>
/// <param name="ManualEntryAvailable">Whether manual logging still works. Always true.</param>
/// <param name="Headline">One-line summary of the connection.</param>
/// <param name="Explanation">Paragraph explaining the state, including what Forge cannot know.</param>
/// <param name="CanWriteWorkouts">Whether completed workouts can be written back.</param>
/// <param name="Rows">Per-category detail, in catalogue order.</param>
public sealed record HealthConnectionSummary(
    HealthPlatform Platform,
    HealthAvailability Availability,
    bool ManualEntryAvailable,
    string Headline,
    string Explanation,
    bool CanWriteWorkouts,
    IReadOnlyList<HealthConnectionRow> Rows)
{
    /// <summary>Whether any row's permission state is unknowable rather than merely unknown.</summary>
    public bool HasUnverifiablePermission =>
        Rows.Any(row => !row.IsPermissionVerifiable && row.Permission is HealthPermissionStatus.Unknown);

    /// <summary>Whether the user has anything useful to gain from connecting or reconnecting.</summary>
    public bool CanRequestAuthorization =>
        Availability is not HealthAvailability.NotSupportedOnPlatform;
}

/// <summary>
/// Builds the health connections screen from a permission result and recorded sync times.
/// </summary>
/// <remarks>
/// <para>
/// The rule this type exists to enforce: <b>never render a claim Forge cannot verify</b>.
/// </para>
/// <para>
/// HealthKit answers an authorization request the same way whether the user granted read access or
/// refused it, and a subsequent read returns an empty list in both cases. Apple did that
/// deliberately - a refusal would otherwise leak the existence of a condition the user wanted
/// hidden. The temptation is to treat "request completed" as "granted" and show a green tick, which
/// is a lie the user finds out about days later when their rings are still empty.
/// </para>
/// <para>
/// So a HealthKit read type is rendered as <c>Unknown</c> with an explanation of why, and a
/// Health Connect type - which does report refusal truthfully - is rendered as the fact it is.
/// </para>
/// </remarks>
public static class HealthConnectionSummaryFactory
{
    private const string ManualEntryPromise = "Manual entry always works, whatever the health store does.";

    /// <summary>Builds the screen model.</summary>
    /// <param name="platform">The platform store this build talks to.</param>
    /// <param name="permissions">The most recent authorization result.</param>
    /// <param name="lastSyncedUtc">Per-category last successful read times.</param>
    /// <param name="nowUtc">Current time, injected so relative labels are testable.</param>
    /// <returns>The screen model.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static HealthConnectionSummary Create(
        HealthPlatform platform,
        HealthPermissionResult permissions,
        IReadOnlyDictionary<HealthDataType, DateTimeOffset> lastSyncedUtc,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(lastSyncedUtc);

        var storeName = HealthDataTypeCatalog.DisplayName(platform);
        var rows = HealthDataTypeCatalog.ReadTypes
            .Select(dataType => CreateRow(platform, dataType, permissions, lastSyncedUtc, nowUtc, storeName))
            .ToArray();

        var workoutPermission = Lookup(permissions.Permissions, HealthDataType.Workout);
        var canWriteWorkouts =
            permissions.Availability is HealthAvailability.Available or HealthAvailability.PermissionUnknown &&
            workoutPermission is HealthPermissionStatus.Granted;

        return new HealthConnectionSummary(
            platform,
            permissions.Availability,
            ManualEntryAvailable: true,
            Headline(permissions.Availability, storeName),
            Explanation(platform, permissions, storeName),
            canWriteWorkouts,
            rows);
    }

    private static HealthConnectionRow CreateRow(
        HealthPlatform platform,
        HealthDataType dataType,
        HealthPermissionResult permissions,
        IReadOnlyDictionary<HealthDataType, DateTimeOffset> lastSyncedUtc,
        DateTimeOffset nowUtc,
        string storeName)
    {
        var descriptor = HealthDataTypeCatalog.Describe(dataType);
        var status = Lookup(permissions.Permissions, dataType);

        // A read permission is only a verifiable fact if the platform is willing to state it. On
        // HealthKit it is not, so an Unknown there is permanent, not a transient "not asked yet".
        var verifiable = HealthDataTypeCatalog.ReportsReadPermissionHonestly(platform) ||
            status is not HealthPermissionStatus.Unknown;

        var lastSynced = lastSyncedUtc.TryGetValue(dataType, out var synced) ? synced : (DateTimeOffset?)null;

        return new HealthConnectionRow(
            dataType,
            descriptor.DisplayName,
            descriptor.Purpose,
            status,
            verifiable,
            lastSynced,
            StatusLabel(status, verifiable),
            RowExplanation(status, verifiable, descriptor.DisplayName, storeName, lastSynced),
            HealthSyncLabels.DescribeLastSync(lastSynced, nowUtc));
    }

    private static HealthPermissionStatus Lookup(
        IReadOnlyDictionary<HealthDataType, HealthPermissionStatus> permissions,
        HealthDataType dataType) =>
        permissions.TryGetValue(dataType, out var status) ? status : HealthPermissionStatus.Unknown;

    private static string StatusLabel(HealthPermissionStatus status, bool verifiable) => status switch
    {
        HealthPermissionStatus.Granted => "Allowed",
        HealthPermissionStatus.Denied => "Refused",
        HealthPermissionStatus.Unavailable => "Not available",
        _ => verifiable ? "Not requested" : "Cannot be confirmed"
    };

    private static string RowExplanation(
        HealthPermissionStatus status,
        bool verifiable,
        string displayName,
        string storeName,
        DateTimeOffset? lastSynced) => status switch
        {
            HealthPermissionStatus.Granted =>
                $"{storeName} confirmed read access to {displayName.ToLowerInvariant()}.",

            HealthPermissionStatus.Denied =>
                $"You refused {displayName.ToLowerInvariant()} in {storeName}. Change it there if you want it back. {ManualEntryPromise}",

            HealthPermissionStatus.Unavailable =>
                $"{storeName} does not offer {displayName.ToLowerInvariant()} on this device. {ManualEntryPromise}",

            _ when verifiable =>
                $"Forge has not asked for {displayName.ToLowerInvariant()} yet.",

            // The important case. Do not soften this into "connected" - Forge genuinely does not know.
            _ when lastSynced is not null =>
                $"{storeName} never says whether it granted or refused read access, so Forge cannot confirm it. " +
                $"{displayName} data has arrived before, which means access worked at least once. {ManualEntryPromise}",

            _ =>
                $"{storeName} never says whether it granted or refused read access, so Forge cannot confirm it. " +
                $"No {displayName.ToLowerInvariant()} data has arrived yet, which may mean access was refused or simply " +
                $"that there is nothing recorded. {ManualEntryPromise}"
        };

    private static string Headline(HealthAvailability availability, string storeName) => availability switch
    {
        HealthAvailability.Available => $"Connected to {storeName}",
        HealthAvailability.PermissionUnknown => $"Linked to {storeName}, access unconfirmed",
        HealthAvailability.RequiresSetup => $"{storeName} needs setting up",
        _ => "No health store on this device"
    };

    private static string Explanation(
        HealthPlatform platform,
        HealthPermissionResult permissions,
        string storeName)
    {
        if (permissions.Message is { Length: > 0 } message)
        {
            return message;
        }

        return permissions.Availability switch
        {
            HealthAvailability.Available =>
                $"Forge reads only the categories listed below, keeps every reading on this device and never " +
                $"sends health data anywhere. {ManualEntryPromise}",

            HealthAvailability.PermissionUnknown =>
                $"{storeName} does not tell apps whether read access was granted or refused, so Forge shows what it " +
                $"actually knows rather than guessing. Empty categories may mean refused access or simply no data. " +
                $"{ManualEntryPromise}",

            HealthAvailability.RequiresSetup => platform is HealthPlatform.HealthConnect
                ? $"{storeName} is missing or out of date on this device. Install or update it, then connect again. " +
                  $"{ManualEntryPromise}"
                : $"{storeName} is not ready on this device yet. {ManualEntryPromise}",

            _ => $"This device has no health store Forge can talk to. {ManualEntryPromise}"
        };
    }
}
