namespace Forge.App.Motion;

/// <summary>
/// Mirrors ForgeTokens.xaml motion durations and central easing choices.
/// Small local state changes use fast timings; transitions that move many pixels use longer timings.
/// </summary>
public static class MotionTokens
{
    public const uint Instant = 0;
    public const uint Fast = 150;
    public const uint Medium = 250;
    public const uint Slow = 400;
    public const uint Celebration = 900;

    public static readonly Easing Emphasized = Easing.CubicOut;
    public static readonly Easing Standard = Easing.CubicInOut;
    public static readonly Easing Entrance = Easing.CubicOut;
    public static readonly Easing Exit = Easing.CubicIn;
    public static readonly Easing Press = Easing.SinOut;
    public static readonly Easing Count = Easing.CubicOut;

    public static int FastMilliseconds => (int)Fast;
    public static int MediumMilliseconds => (int)Medium;
    public static int SlowMilliseconds => (int)Slow;
    public static int CelebrationMilliseconds => (int)Celebration;
}
