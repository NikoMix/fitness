namespace Forge.Core.Abstractions.Backup;

/// <summary>
/// Whose data an export is allowed to contain.
/// </summary>
/// <remarks>
/// <para>
/// Forge runs several profiles on one device, so "export my data" and "export this device" are
/// different requests with different consequences. Under GDPR Article 20 a portability export
/// belongs to the person asking for it; handing them the whole database would disclose another
/// person's weight history, food log and training to somebody who has no right to it, performed
/// by the very feature meant to serve privacy.
/// </para>
/// <para>
/// The two values are kept as an explicit enum rather than a boolean so the choice is readable at
/// every call site and cannot be flipped by an accidental argument order. The default of an
/// <see cref="ExportRequest"/> is <see cref="RequestingProfile"/>, and the scope it carries
/// defaults to <c>ProfileScope.None</c>, which matches nothing at all: a request that forgot to
/// say who it is for produces an empty export rather than everybody's.
/// </para>
/// </remarks>
public enum ExportAudience
{
    /// <summary>
    /// Only rows Forge can attribute to the requesting profile. The safe default.
    /// </summary>
    RequestingProfile,

    /// <summary>
    /// Every row on the device, including every other profile's health data.
    /// </summary>
    /// <remarks>
    /// Correct for a device backup the owner restores themselves, and a disclosure of other
    /// people's special-category data in any other context. Choosing this must be a deliberate,
    /// clearly-labelled act by the user, never a default and never a fallback.
    /// </remarks>
    EntireDevice,
}
