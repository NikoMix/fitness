using Forge.Domain.Measurement;
using Forge.Domain.Planning;
using Forge.Domain.Training;
using Forge.Domain.Workout;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Pins the plan reference on a workout session against real SQLite.
/// </summary>
/// <remarks>
/// <para>
/// The columns are nullable on purpose. Every session recorded before this release genuinely was
/// ad hoc - there was no way to start a workout from a plan - so a non-nullable column would have
/// had to invent a plan for all of them. See <c>docs/design/plan-workout-schema-delta.md</c>.
/// </para>
/// <para>
/// Exercised against real SQLite rather than the in-memory provider because the two disagree about
/// <see cref="DateTimeOffset"/>: SQLite has no such type, and a query that compares or orders one
/// throws at runtime while passing every in-memory test. That has shipped twice.
/// </para>
/// </remarks>
public sealed class PlanWorkoutSchemaTests : IAsyncLifetime
{
    private static readonly Guid Owner = Guid.CreateVersion7();

    private SqliteConnection connection = null!;
    private DbContextOptions<ForgeDbContext> options = null!;

    public async ValueTask InitializeAsync()
    {
        connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();

    [Fact]
    public async Task A_session_started_from_a_plan_remembers_which_day_it_was_executing()
    {
        var planId = Guid.CreateVersion7();
        var dayId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();

        await using (var seed = CreateContext())
        {
            seed.Set<WorkoutSession>().Add(new WorkoutSession
            {
                Id = sessionId,
                UserProfileId = Owner,
                StartedUtc = DateTimeOffset.UtcNow.AddHours(-1),
                CompletedUtc = DateTimeOffset.UtcNow,
                Title = "Upper A",
                TrainingPlanId = planId,
                PlanDayId = dayId,
                PlanDayName = "Upper A"
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var stored = await context.Set<WorkoutSession>()
            .SingleAsync(session => session.Id == sessionId, TestContext.Current.CancellationToken);

        stored.TrainingPlanId.ShouldBe(planId);
        stored.PlanDayId.ShouldBe(dayId);
        stored.PlanDayName.ShouldBe("Upper A");
        stored.IsPlanned.ShouldBeTrue();
    }

    [Fact]
    public async Task A_session_written_without_a_plan_round_trips_as_ad_hoc()
    {
        var sessionId = Guid.CreateVersion7();

        await using (var seed = CreateContext())
        {
            seed.Set<WorkoutSession>().Add(new WorkoutSession
            {
                Id = sessionId,
                UserProfileId = Owner,
                StartedUtc = DateTimeOffset.UtcNow.AddHours(-2),
                Title = "Workout"
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var stored = await context.Set<WorkoutSession>()
            .SingleAsync(session => session.Id == sessionId, TestContext.Current.CancellationToken);

        // This is what every pre-existing row looks like after the migration, and it must remain a
        // first-class state rather than a broken one.
        stored.TrainingPlanId.ShouldBeNull();
        stored.PlanDayId.ShouldBeNull();
        stored.PlanDayName.ShouldBeNull();
        stored.IsPlanned.ShouldBeFalse();
    }

    [Fact]
    public async Task Completed_plan_day_sessions_can_be_found_without_translating_a_DateTimeOffset()
    {
        var dayId = Guid.CreateVersion7();

        await using (var seed = CreateContext())
        {
            seed.Set<WorkoutSession>().Add(new WorkoutSession
            {
                UserProfileId = Owner,
                StartedUtc = DateTimeOffset.UtcNow.AddHours(-3),
                CompletedUtc = DateTimeOffset.UtcNow.AddHours(-2),
                PlanDayId = dayId,
                PlanDayName = "Upper A"
            });
            seed.Set<WorkoutSession>().Add(new WorkoutSession
            {
                UserProfileId = Owner,
                StartedUtc = DateTimeOffset.UtcNow.AddHours(-1),
                PlanDayId = dayId,
                PlanDayName = "Upper A"
            });
            seed.Set<WorkoutSession>().Add(new WorkoutSession
            {
                UserProfileId = Owner,
                StartedUtc = DateTimeOffset.UtcNow.AddHours(-4),
                CompletedUtc = DateTimeOffset.UtcNow.AddHours(-3)
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();

        // Both predicates are null checks, which translate. Converting CompletedUtc to a local date
        // happens after materialising, which is the shape LoadPlanDayCompletionsAsync uses.
        var rows = await context.Set<WorkoutSession>()
            .Where(session => session.PlanDayId != null && session.CompletedUtc != null)
            .Select(session => new { session.PlanDayId, session.CompletedUtc })
            .ToListAsync(TestContext.Current.CancellationToken);

        rows.Count.ShouldBe(1);
        rows[0].PlanDayId.ShouldBe(dayId);
    }

    [Fact]
    public async Task A_planned_day_projects_onto_a_queue_that_survives_the_snapshot()
    {
        var exerciseId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var day = new PlanDay { UserProfileId = Owner, Name = "Lower A", Ordinal = 0 };
        var planned = new PlannedExercise
        {
            UserProfileId = Owner,
            ExerciseId = exerciseId,
            ExerciseName = "Back squat",
            PrimaryMuscle = "Quads",
            Ordinal = 0
        };
        planned.Sets.Add(new PlannedSet
        {
            UserProfileId = Owner,
            Ordinal = 1,
            TargetRepsMin = 5,
            TargetRepsMax = 5,
            TargetLoad = Mass.FromKilograms(102.5m),
            Rest = TimeSpan.FromMinutes(3)
        });
        day.Exercises.Add(planned);

        var queue = PlanWorkoutProjection.BuildQueue(
            day,
            [new ActiveWorkoutExercise(exerciseId, "Back squat", "Quads", null, null)]);

        await using (var seed = CreateContext())
        {
            seed.Set<WorkoutSession>().Add(new WorkoutSession { Id = sessionId, UserProfileId = Owner, PlanDayId = day.Id, PlanDayName = day.Name });
            seed.Set<ActiveWorkoutState>().Add(
                ActiveWorkoutState.StartWithQueue(Owner, sessionId, DateTimeOffset.UtcNow, queue));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var stored = await context.Set<ActiveWorkoutState>()
            .SingleAsync(state => state.WorkoutSessionId == sessionId, TestContext.Current.CancellationToken);

        // The prescription travels inside the existing JSON column, so a workout recovered after
        // process death still targets the plan rather than falling back to a constant.
        stored.ExerciseQueue.Count.ShouldBe(1);
        stored.ExerciseQueue[0].IsFromPlan.ShouldBeTrue();
        stored.ResolveCurrentTarget().LoadKilograms.ShouldBe(102.5m);
        stored.ResolveCurrentTarget().Source.ShouldBe(WorkoutTargetSource.Plan);
    }

    [Fact]
    public async Task A_queue_stored_before_plans_reached_workouts_still_loads()
    {
        var sessionId = Guid.CreateVersion7();

        await using (var seed = CreateContext())
        {
            seed.Set<WorkoutSession>().Add(new WorkoutSession { Id = sessionId, UserProfileId = Owner });
            seed.Set<ActiveWorkoutState>().Add(ActiveWorkoutState.Start(
                Owner,
                sessionId,
                DateTimeOffset.UtcNow,
                new ActiveWorkoutExercise(Guid.CreateVersion7(), "Bench press", "Chest", 80m, 8)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var stored = await context.Set<ActiveWorkoutState>()
            .SingleAsync(state => state.WorkoutSessionId == sessionId, TestContext.Current.CancellationToken);

        stored.ExerciseQueue[0].PlannedSets.ShouldBeNull();
        stored.ExerciseQueue[0].IsFromPlan.ShouldBeFalse();
    }

    private ForgeDbContext CreateContext() => new(options);
}
