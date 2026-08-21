namespace Forge.App.Branding;

/// <summary>
/// The single source of truth for Forge brand colour.
/// </summary>
/// <remarks>
/// DevExpress <c>ThemeManager</c> derives a complete Material Design 3 palette - light and
/// dark, with every semantic role - from one seed colour. Screens therefore consume semantic
/// roles such as <c>{dx:ThemeColor OnSurface}</c> rather than literal hex values, so a
/// rebrand or a dark-mode correction is a change here and nowhere else.
/// </remarks>
public static class ForgeBrand
{
    /// <summary>
    /// Seed colour for the generated theme, a warm forge ember.
    /// </summary>
    /// <remarks>
    /// Chosen for energy and warmth while retaining enough luminance contrast to satisfy
    /// WCAG 2.2 AA against both the light and dark surfaces that MD3 derives from it.
    /// </remarks>
    public const string SeedHex = "#E2571F";

    /// <summary>Deep neutral used for the splash screen and app icon background.</summary>
    public const string CanvasHex = "#0B0E14";
}
