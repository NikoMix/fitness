using System.Globalization;
using Forge.Core.Abstractions.Preferences;

namespace Forge.Core.Tests.Localization;

/// <summary>A preference store that keeps values in memory for the life of a test.</summary>
/// <remarks>
/// Shared by the localization tests and reused across "restarts" - constructing a second service
/// over the same store is how a persisted choice is verified without a device.
/// </remarks>
internal sealed class InMemoryPreferenceStore : IPreferenceStore
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => values.Keys;

    public string GetString(string key, string defaultValue) =>
        values.TryGetValue(key, out var value) ? value : defaultValue;

    public void SetString(string key, string value) => values[key] = value;

    public bool GetBoolean(string key, bool defaultValue) =>
        values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : defaultValue;

    public void SetBoolean(string key, bool value) => values[key] = value.ToString();

    public int GetInt32(string key, int defaultValue) =>
        values.TryGetValue(key, out var value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;

    public void SetInt32(string key, int value) => values[key] = value.ToString(CultureInfo.InvariantCulture);
}
