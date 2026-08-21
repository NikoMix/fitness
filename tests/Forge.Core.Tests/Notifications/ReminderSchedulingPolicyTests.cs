using Forge.Core.Abstractions.Notifications;
using Shouldly;

namespace Forge.Core.Tests.Notifications;

public sealed class ReminderSchedulingPolicyTests
{
    [Fact]
    public void Plan_suppresses_candidates_during_quiet_hours()
    {
        var input = CreateInput(
            new DateTimeOffset(2026, 1, 12, 20, 0, 0, TimeSpan.Zero),
            CreatePreferences(
                quietHours: new QuietHoursPolicy(true, new TimeOnly(22, 0), new TimeOnly(7, 0)),
                workoutTime: new TimeOnly(22, 15),
                hydrationTime: new TimeOnly(22, 30),
                checkInTime: new TimeOnly(23, 0),
                streakTime: new TimeOnly(23, 30)));

        var decisions = new ReminderSchedulingPolicy().Plan(input);

        decisions.Where(decision => decision.SuppressionReason != ReminderSuppressionReason.NotApplicable)
            .ShouldAllBe(decision => decision.SuppressionReason == ReminderSuppressionReason.QuietHours);
    }

    [Fact]
    public void Plan_enforces_daily_cap()
    {
        var input = CreateInput(
            new DateTimeOffset(2026, 1, 12, 6, 0, 0, TimeSpan.Zero),
            CreatePreferences(dailyCap: 2));

        var decisions = new ReminderSchedulingPolicy().Plan(input);

        decisions.Count(decision => decision.SuppressionReason is null).ShouldBe(2);
        decisions.Count(decision => decision.SuppressionReason == ReminderSuppressionReason.DailyCapReached).ShouldBe(2);
    }

    [Fact]
    public void Plan_suppresses_already_completed_actions()
    {
        var input = CreateInput(
            new DateTimeOffset(2026, 1, 12, 6, 0, 0, TimeSpan.Zero),
            CreatePreferences(),
            snapshot: new ReminderUserSnapshot(
                new DateOnly(2026, 1, 12),
                true,
                "Upper A",
                true,
                2000,
                true,
                true));

        var decisions = new ReminderSchedulingPolicy().Plan(input);

        decisions.ShouldAllBe(decision => decision.SuppressionReason == ReminderSuppressionReason.AlreadyCompleted);
    }

    [Fact]
    public void Plan_resolves_dst_gaps_as_wall_clock_and_still_respects_quiet_hours()
    {
        var timeZone = GetCentralEuropeanTimeZone();
        var localDate = new DateOnly(2026, 3, 29);
        var now = ReminderSchedulingPolicy.ResolveWallClock(localDate, new TimeOnly(0, 30), timeZone);
        var input = CreateInput(
            now,
            CreatePreferences(
                workoutEnabled: false,
                checkInEnabled: false,
                streakEnabled: false,
                quietHours: new QuietHoursPolicy(true, new TimeOnly(22, 0), new TimeOnly(7, 0)),
                hydrationTime: new TimeOnly(2, 30)),
            timeZone,
            new ReminderUserSnapshot(localDate, false, null, false, 0, false, false));

        var decisions = new ReminderSchedulingPolicy().Plan(input);
        var hydration = decisions.Single(decision => decision.Kind == ReminderKind.Hydration);

        hydration.Request.DeliverAtLocal.Hour.ShouldBe(3);
        hydration.SuppressionReason.ShouldBe(ReminderSuppressionReason.QuietHours);
    }

    private static ReminderSchedulingInput CreateInput(
        DateTimeOffset now,
        ReminderPreferences preferences,
        TimeZoneInfo? timeZone = null,
        ReminderUserSnapshot? snapshot = null,
        int alreadyScheduled = 0)
        => new(
            now,
            timeZone ?? TimeZoneInfo.Utc,
            ForgeNotificationPermissionState.Authorized,
            preferences,
            snapshot ?? new ReminderUserSnapshot(new DateOnly(2026, 1, 12), true, "Upper A", false, 0, false, true),
            alreadyScheduled);

    private static ReminderPreferences CreatePreferences(
        bool workoutEnabled = true,
        bool hydrationEnabled = true,
        bool checkInEnabled = true,
        bool streakEnabled = true,
        QuietHoursPolicy? quietHours = null,
        int dailyCap = 4,
        TimeOnly? workoutTime = null,
        TimeOnly? hydrationTime = null,
        TimeOnly? checkInTime = null,
        TimeOnly? streakTime = null)
        => new(
            workoutEnabled,
            hydrationEnabled,
            checkInEnabled,
            streakEnabled,
            quietHours ?? new QuietHoursPolicy(true, new TimeOnly(22, 0), new TimeOnly(7, 0)),
            dailyCap,
            workoutTime ?? new TimeOnly(8, 0),
            hydrationTime ?? new TimeOnly(9, 0),
            checkInTime ?? new TimeOnly(10, 0),
            streakTime ?? new TimeOnly(20, 0),
            2000);

    private static TimeZoneInfo GetCentralEuropeanTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
    }
}
