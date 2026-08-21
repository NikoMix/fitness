namespace Forge.Core.Adaptive;

/// <summary>
/// How much horizontal room a surface has to work with.
/// </summary>
/// <remarks>
/// The names and thresholds follow the Material 3 window size classes, which Apple's own
/// size-class guidance lines up with closely enough that one scale serves both platforms. The
/// class is deliberately derived from the width of the <em>window</em> rather than the device,
/// because an iPad running Forge in Slide Over is 320 points wide and must be laid out as a
/// phone, while the same iPad in full screen must not.
/// </remarks>
public enum LayoutSizeClass
{
    /// <summary>A phone, or a tablet window narrow enough to behave like one.</summary>
    Compact,

    /// <summary>A small tablet, a large phone in landscape, or a half-width tablet window.</summary>
    Medium,

    /// <summary>A tablet with room for more than one column of real content.</summary>
    Expanded
}
