using Forge.Domain.Engagement;
using Shouldly;

namespace Forge.Domain.Tests.Engagement;

public sealed class StreakTests
{
    [Fact]
    public void Scheduled_rest_day_does_not_break_streak()
    {
        var streak = new Streak();
        var monday = new DateOnly(2026, 8, 17);

        streak.RecordTrainingDay(monday).ShouldBe(StreakOutcome.Extended);
        streak.RecordRestDay(monday.AddDays(1)).ShouldBe(StreakOutcome.ProtectedByRest);
        streak.RecordTrainingDay(monday.AddDays(2)).ShouldBe(StreakOutcome.Extended);

        streak.CurrentDays.ShouldBe(2);
        streak.BestDays.ShouldBe(2);
        streak.History.ShouldContain(day => day.Kind == StreakDayKind.Rest);
    }

    [Fact]
    public void Freeze_protects_streak_after_missed_day()
    {
        var streak = new Streak();
        var day = new DateOnly(2026, 8, 17);

        streak.RecordTrainingDay(day);
        streak.RecordMissedDay(day.AddDays(1)).ShouldBe(StreakOutcome.ProtectedByFreeze);

        streak.CurrentDays.ShouldBe(1);
        streak.FreezesRemaining.ShouldBe(1);
        streak.History.Last().Kind.ShouldBe(StreakDayKind.FreezeUsed);
    }

    [Fact]
    public void Next_day_training_recovers_after_unprotected_miss()
    {
        var streak = new Streak();
        var day = new DateOnly(2026, 8, 17);

        streak.RecordTrainingDay(day);
        streak.RecordMissedDay(day.AddDays(1));
        streak.RecordMissedDay(day.AddDays(2));
        streak.RecordMissedDay(day.AddDays(3)).ShouldBe(StreakOutcome.RecoverableMiss);

        streak.RecoverAfterMiss(day.AddDays(4)).ShouldBe(StreakOutcome.Recovered);

        streak.CurrentDays.ShouldBe(2);
        streak.History.Last().Kind.ShouldBe(StreakDayKind.Recovered);
    }
}
