using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

public sealed class ExerciseCardViewModel(Exercise exercise)
{
    public Exercise Exercise { get; } = exercise;

    public Guid Id => Exercise.Id;

    public string Name => Exercise.Name;

    public string Pattern => Exercise.Pattern.ToString();

    public string PrimaryMuscle => Exercise.PrimaryMuscle ?? "General";

    public string Equipment => string.IsNullOrWhiteSpace(Exercise.Equipment) ? "Bodyweight" : Exercise.Equipment;

    public string Difficulty => Exercise.Difficulty.ToString();

    public string Summary => $"{PrimaryMuscle} • {Equipment} • {Difficulty}";

    public bool IsUserCreated => Exercise.IsUserCreated;

    public bool IsFavourite => Exercise.IsFavourite;

    public string FavouriteGlyph => IsFavourite ? "★" : "☆";

    public string RecentSummary => Exercise.LastUsedUtc is null
        ? string.Empty
        : $"Last used {Exercise.LastUsedUtc.Value.LocalDateTime:g}";
}
