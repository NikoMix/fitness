using Forge.Domain.Analytics;
using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Analytics;

public sealed class PersonalRecordDetectorTests
{
    private static readonly Guid SquatId = Guid.CreateVersion7();
    private static readonly Guid SessionOne = Guid.CreateVersion7();
    private static readonly Guid SessionTwo = Guid.CreateVersion7();

    [Fact]
    public void Empty_log_has_no_records()
    {
        var records = PersonalRecordDetector.DetectAll([]);

        records.ShouldBeEmpty();
    }

    [Fact]
    public void Detects_heaviest_load_estimated_one_rep_max_reps_at_load_and_session_volume()
    {
        var sets = new[]
        {
            Set(100m, 5, new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), SessionOne),
            Set(110m, 3, new DateTimeOffset(2026, 1, 8, 10, 0, 0, TimeSpan.Zero), SessionTwo),
            Set(100m, 8, new DateTimeOffset(2026, 1, 8, 10, 5, 0, TimeSpan.Zero), SessionTwo),
            Set(60m, 20, new DateTimeOffset(2026, 1, 8, 10, 10, 0, TimeSpan.Zero), SessionTwo),
        };

        var records = PersonalRecordDetector.DetectAll(sets);

        records.ShouldContain(record => record.Type == PersonalRecordType.HeaviestLoad && record.Load.Kilograms == 110m);
        records.ShouldContain(record => record.Type == PersonalRecordType.EstimatedOneRepMax && record.Set.Repetitions == 8);
        records.ShouldContain(record => record.Type == PersonalRecordType.MostRepsAtLoad && record.Load.Kilograms == 100m && record.Repetitions == 8);
        records.ShouldContain(record => record.Type == PersonalRecordType.GreatestSessionVolume && record.Value.Kilograms == 2330m);
    }

    [Fact]
    public void Warm_up_sets_do_not_create_records_or_session_volume()
    {
        var warmUp = Set(200m, 5, new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), SessionOne, isWarmUp: true);
        var working = Set(100m, 5, new DateTimeOffset(2026, 1, 1, 10, 5, 0, TimeSpan.Zero), SessionOne);

        var records = PersonalRecordDetector.DetectAll([warmUp, working]);

        records.Single(record => record.Type == PersonalRecordType.HeaviestLoad).Load.Kilograms.ShouldBe(100m);
        records.Single(record => record.Type == PersonalRecordType.GreatestSessionVolume).Value.Kilograms.ShouldBe(500m);
    }

    private static SetEntry Set(decimal kilograms, int reps, DateTimeOffset completed, Guid session, bool isWarmUp = false)
        => new()
        {
            WorkoutSessionId = session,
            ExerciseId = SquatId,
            Ordinal = 1,
            Load = Mass.FromKilograms(kilograms),
            Repetitions = reps,
            IsWarmUp = isWarmUp,
            CompletedUtc = completed
        };
}
