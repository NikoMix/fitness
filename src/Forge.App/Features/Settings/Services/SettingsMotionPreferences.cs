using Forge.App.Motion;
using Forge.Core.Abstractions.Preferences;

namespace Forge.App.Features.Settings.Services;

/// <summary>Combines platform motion accessibility settings with Forge's haptic preference.</summary>
public sealed class SettingsMotionPreferences(IMotionPreferences platformPreferences, IForgePreferences forgePreferences) : IMotionPreferences
{
    /// <inheritdoc />
    public bool IsReduceMotionEnabled => platformPreferences.IsReduceMotionEnabled;

    /// <inheritdoc />
    public bool IsHapticFeedbackEnabled => platformPreferences.IsHapticFeedbackEnabled && forgePreferences.HapticFeedbackEnabled;
}
