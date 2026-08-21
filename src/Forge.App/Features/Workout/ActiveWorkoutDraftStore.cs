using System.Text.Json;
using Forge.Domain.Workout;

namespace Forge.App.Features.Workout;

public interface IActiveWorkoutDraftStore
{
    ActiveWorkoutState? Load();

    void Save(ActiveWorkoutState state);

    void Clear();
}

internal sealed class ActiveWorkoutDraftStore : IActiveWorkoutDraftStore
{
    private const string ActiveWorkoutKey = "forge.workout.active-state.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ActiveWorkoutState? Load()
    {
        var json = Preferences.Default.Get(ActiveWorkoutKey, string.Empty);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ActiveWorkoutState>(json, JsonOptions);
    }

    public void Save(ActiveWorkoutState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Preferences.Default.Set(ActiveWorkoutKey, JsonSerializer.Serialize(state, JsonOptions));
    }

    public void Clear() => Preferences.Default.Remove(ActiveWorkoutKey);
}
