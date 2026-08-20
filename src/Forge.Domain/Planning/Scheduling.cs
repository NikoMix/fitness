namespace Forge.Domain.Planning;

/// <summary>One planned calendar occurrence.</summary>
public sealed record ScheduledPlanSession(DateOnly Date, PlanDay Day, int Sequence, bool WasShifted = false);

/// <summary>Creates humane plan schedules for fixed and flexible programmes.</summary>
public static class PlanScheduler
{
    /// <summary>Builds upcoming occurrences. Fixed plans use each day's weekday; flexible plans spread sessions evenly.</summary>
    public static IReadOnlyList<ScheduledPlanSession> Schedule(TrainingPlan plan, DateOnly weekStart, int weeks)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentOutOfRangeException.ThrowIfLessThan(weeks, 1);

        return plan.ScheduleMode == PlanScheduleMode.FixedDays
            ? ScheduleFixed(plan, weekStart, weeks)
            : ScheduleFlexible(plan, weekStart, weeks);
    }

    /// <summary>
    /// Handles a missed session by shifting that occurrence and the following occurrences forward.
    /// The returned schedule marks shifted sessions but does not delete the missed work or break a
    /// streak; the user simply keeps going from the next available day.
    /// </summary>
    public static IReadOnlyList<ScheduledPlanSession> ShiftForMissedSession(
        IReadOnlyList<ScheduledPlanSession> schedule,
        int missedSequence,
        DateOnly nextAvailableDate)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var missed = schedule.First(session => session.Sequence == missedSequence);
        var shiftDays = Math.Max(0, nextAvailableDate.DayNumber - missed.Date.DayNumber);
        if (shiftDays == 0)
        {
            return schedule;
        }

        return schedule
            .Select(session => session.Sequence < missedSequence
                ? session
                : session with { Date = session.Date.AddDays(shiftDays), WasShifted = true })
            .ToList();
    }

    private static List<ScheduledPlanSession> ScheduleFixed(TrainingPlan plan, DateOnly weekStart, int weeks)
    {
        var sessions = new List<ScheduledPlanSession>();
        var sequence = 0;
        for (var week = 0; week < weeks; week++)
        {
            foreach (var day in plan.Days.OrderBy(day => day.ScheduledDay ?? DayOfWeek.Monday).ThenBy(day => day.Ordinal))
            {
                var scheduledDay = day.ScheduledDay ?? weekStart.DayOfWeek;
                var offset = ((int)scheduledDay - (int)weekStart.DayOfWeek + 7) % 7;
                sessions.Add(new ScheduledPlanSession(weekStart.AddDays(week * 7 + offset), day, sequence++));
            }
        }

        return sessions.OrderBy(session => session.Date).ThenBy(session => session.Sequence).ToList();
    }

    private static List<ScheduledPlanSession> ScheduleFlexible(TrainingPlan plan, DateOnly weekStart, int weeks)
    {
        var days = plan.Days.OrderBy(day => day.Ordinal).ToList();
        var target = Math.Clamp(plan.TargetSessionsPerWeek, 1, Math.Max(1, days.Count));
        var spacing = 7m / target;
        var sessions = new List<ScheduledPlanSession>();
        var sequence = 0;

        for (var week = 0; week < weeks; week++)
        {
            for (var session = 0; session < target; session++)
            {
                var day = days[(week * target + session) % days.Count];
                var offset = (int)Math.Round(session * spacing, MidpointRounding.AwayFromZero);
                sessions.Add(new ScheduledPlanSession(weekStart.AddDays(week * 7 + offset), day, sequence++));
            }
        }

        return sessions;
    }
}
