using Forge.Domain.Common;

namespace Forge.Domain.Engagement;

/// <summary>A forgiving training streak that preserves rest, recovery, and user wellbeing.</summary>
public sealed class Streak : Entity
{
    public Guid UserProfileId { get; init; }

    public int CurrentDays { get; private set; }

    public int BestDays { get; private set; }

    public int FreezesRemaining { get; private set; } = 2;

    public bool GamificationEnabled { get; private set; } = true;

    public DateOnly? LastCountedDate { get; private set; }

    public List<StreakDay> History { get; private set; } = [];

    public void SetGamificationEnabled(bool enabled) => GamificationEnabled = enabled;

    public StreakOutcome RecordTrainingDay(DateOnly date)
    {
        if (AlreadyRecorded(date))
        {
            return StreakOutcome.NoChange;
        }

        CurrentDays = LastCountedDate is null ? 1 : CurrentDays + 1;
        BestDays = Math.Max(BestDays, CurrentDays);
        LastCountedDate = date;
        History.Add(new StreakDay(date, StreakDayKind.Training, CurrentDays));
        return StreakOutcome.Extended;
    }

    public StreakOutcome RecordRestDay(DateOnly date)
    {
        if (AlreadyRecorded(date))
        {
            return StreakOutcome.NoChange;
        }

        History.Add(new StreakDay(date, StreakDayKind.Rest, CurrentDays));
        LastCountedDate ??= date;
        return StreakOutcome.ProtectedByRest;
    }

    public StreakOutcome RecordMissedDay(DateOnly date)
    {
        if (AlreadyRecorded(date))
        {
            return StreakOutcome.NoChange;
        }

        if (FreezesRemaining > 0)
        {
            FreezesRemaining--;
            History.Add(new StreakDay(date, StreakDayKind.FreezeUsed, CurrentDays));
            LastCountedDate = date;
            return StreakOutcome.ProtectedByFreeze;
        }

        History.Add(new StreakDay(date, StreakDayKind.Missed, CurrentDays));
        return StreakOutcome.RecoverableMiss;
    }

    public StreakOutcome RecoverAfterMiss(DateOnly date)
    {
        var previous = History.LastOrDefault();
        if (previous?.Kind != StreakDayKind.Missed || date.DayNumber - previous.Date.DayNumber != 1)
        {
            CurrentDays = 1;
            BestDays = Math.Max(BestDays, CurrentDays);
            LastCountedDate = date;
            History.Add(new StreakDay(date, StreakDayKind.Training, CurrentDays));
            return StreakOutcome.RestartedEncouragingly;
        }

        CurrentDays++;
        BestDays = Math.Max(BestDays, CurrentDays);
        LastCountedDate = date;
        History.Add(new StreakDay(date, StreakDayKind.Recovered, CurrentDays));
        return StreakOutcome.Recovered;
    }

    private bool AlreadyRecorded(DateOnly date) => History.Any(day => day.Date == date);
}

public sealed record StreakDay(DateOnly Date, StreakDayKind Kind, int StreakDaysAfter);

public enum StreakDayKind
{
    Training,
    Rest,
    FreezeUsed,
    Missed,
    Recovered
}

public enum StreakOutcome
{
    NoChange,
    Extended,
    ProtectedByRest,
    ProtectedByFreeze,
    RecoverableMiss,
    Recovered,
    RestartedEncouragingly
}
