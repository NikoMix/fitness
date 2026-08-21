using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Forge.Domain.Workout;
using Shouldly;

namespace Forge.Domain.Tests.Workout;

public sealed class WorkoutHistoryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void History_is_ordered_newest_first_by_when_a_session_finished()
    {
        var older = BuildSession("Monday", Now.AddDays(-3), Now.AddDays(-3).AddHours(1));
        var newer = BuildSession("Wednesday", Now.AddDays(-1), Now.AddDays(-1).AddHours(1));

        var history = WorkoutHistoryBuilder.Build([older, newer], Names(), Now);

        history.Select(entry => entry.Title).ShouldBe(["Wednesday", "Monday"]);
    }

    [Fact]
    public void A_session_finished_after_midnight_stays_ahead_of_one_started_later_the_same_day()
    {
        var lateNight = BuildSession("Late night", Now.AddHours(-20), Now.AddHours(-13));
        var morning = BuildSession("Morning", Now.AddHours(-16), Now.AddHours(-15));

        var history = WorkoutHistoryBuilder.Build([morning, lateNight], Names(), Now);

        history[0].Title.ShouldBe("Late night");
    }

    [Fact]
    public void Volume_counts_working_sets_and_excludes_warm_ups()
    {
        var session = BuildSession("Push", Now.AddHours(-2), Now.AddHours(-1));
        AddSet(session, Squat, 60m, 10, isWarmUp: true);
        AddSet(session, Squat, 100m, 5);
        AddSet(session, Squat, 100m, 5);

        var entry = WorkoutHistoryBuilder.Build([session], Names(), Now).Single();

        entry.WorkingSetCount.ShouldBe(2);
        entry.TotalVolume.ShouldBe(Mass.FromKilograms(1000m));
    }

    [Fact]
    public void Exercise_names_are_ordered_by_how_much_work_each_took()
    {
        var session = BuildSession("Full body", Now.AddHours(-2), Now.AddHours(-1));
        AddSet(session, Curl, 20m, 10);
        AddSet(session, Squat, 100m, 5);

        var entry = WorkoutHistoryBuilder.Build([session], Names(), Now).Single();

        entry.ExerciseNames.ShouldBe(["Back squat", "Cable curl"]);
    }

    [Fact]
    public void An_unknown_exercise_falls_back_to_a_neutral_label_rather_than_throwing()
    {
        var session = BuildSession("Mystery", Now.AddHours(-2), Now.AddHours(-1));
        AddSet(session, Guid.CreateVersion7(), 50m, 5);

        var entry = WorkoutHistoryBuilder.Build([session], Names(), Now).Single();

        entry.ExerciseNames.ShouldBe(["Exercise"]);
    }

    [Fact]
    public void An_abandoned_session_is_flagged_and_measured_against_now()
    {
        var session = BuildSession("Abandoned", Now.AddHours(-2), completedUtc: null);

        var entry = WorkoutHistoryBuilder.Build([session], Names(), Now).Single();

        entry.IsInProgress.ShouldBeTrue();
        entry.CompletedUtc.ShouldBeNull();
        entry.Duration.ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void A_session_without_a_title_gets_a_readable_default()
    {
        var session = BuildSession(title: null, Now.AddHours(-2), Now.AddHours(-1));

        WorkoutHistoryBuilder.Build([session], Names(), Now).Single().Title.ShouldBe("Workout");
    }

    [Fact]
    public void No_sessions_produces_an_empty_history_rather_than_null()
        => WorkoutHistoryBuilder.Build([], Names(), Now).ShouldBeEmpty();

    private static readonly Guid Squat = Guid.CreateVersion7();
    private static readonly Guid Curl = Guid.CreateVersion7();

    private static Dictionary<Guid, string> Names() => new()
    {
        [Squat] = "Back squat",
        [Curl] = "Cable curl"
    };

    private static WorkoutSession BuildSession(string? title, DateTimeOffset startedUtc, DateTimeOffset? completedUtc)
        => new()
        {
            Id = Guid.CreateVersion7(),
            Title = title,
            StartedUtc = startedUtc,
            CompletedUtc = completedUtc
        };

    private static void AddSet(WorkoutSession session, Guid exerciseId, decimal kilograms, int repetitions, bool isWarmUp = false)
        => session.Sets.Add(new SetEntry
        {
            Id = Guid.CreateVersion7(),
            WorkoutSessionId = session.Id,
            ExerciseId = exerciseId,
            Ordinal = session.Sets.Count + 1,
            Load = Mass.FromKilograms(kilograms),
            Repetitions = repetitions,
            IsWarmUp = isWarmUp,
            CompletedUtc = session.StartedUtc.AddMinutes(session.Sets.Count * 3)
        });
}
