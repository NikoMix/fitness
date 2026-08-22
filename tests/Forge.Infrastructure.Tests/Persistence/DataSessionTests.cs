using Forge.Domain.Planning;
using Forge.Domain.Training;
using Forge.Infrastructure.Persistence;
using Forge.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Guards the data-session seam. The bug these tests exist to prevent is subtle: when a
/// repository and a unit of work are resolved separately from a transient-scoped container they
/// each receive their own context, so the save commits an empty change tracker and the caller's
/// writes are lost without any error. A session must therefore hand out repositories that share
/// one change tracker.
/// </summary>
public sealed class DataSessionTests : IAsyncLifetime
{
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
    public async Task Writes_through_different_repositories_commit_in_one_save()
    {
        var exerciseId = Guid.CreateVersion7();
        var planId = Guid.CreateVersion7();

        await using (var session = new EfDataSession(CreateContext()))
        {
            await session.Repository<Exercise>()
                .AddAsync(new Exercise { Id = exerciseId, Name = "Deadlift", Pattern = MovementPattern.Hinge }, TestContext.Current.CancellationToken);
            await session.Repository<TrainingPlan>()
                .AddAsync(new TrainingPlan { Id = planId, UserProfileId = Guid.CreateVersion7(), Name = "Strength" }, TestContext.Current.CancellationToken);

            var written = await session.SaveChangesAsync(TestContext.Current.CancellationToken);

            written.ShouldBe(2, "both repositories must enlist in the same change tracker");
        }

        await using var verify = new EfDataSession(CreateContext());
        (await verify.Repository<Exercise>().GetAsync(exerciseId, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        (await verify.Repository<TrainingPlan>().GetAsync(planId, TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Pending_writes_are_discarded_when_the_session_is_disposed_without_saving()
    {
        var exerciseId = Guid.CreateVersion7();

        await using (var session = new EfDataSession(CreateContext()))
        {
            await session.Repository<Exercise>()
                .AddAsync(new Exercise { Id = exerciseId, Name = "Never saved" }, TestContext.Current.CancellationToken);
        }

        await using var verify = new EfDataSession(CreateContext());
        (await verify.Repository<Exercise>().GetAsync(exerciseId, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Repository_of_the_same_entity_type_is_reused_within_a_session()
    {
        await using var session = new EfDataSession(CreateContext());

        session.Repository<Exercise>().ShouldBeSameAs(session.Repository<Exercise>());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Factory_hands_out_independent_sessions()
    {
        var factory = new EfDataSessionFactory(CreateContext);
        var exerciseId = Guid.CreateVersion7();

        await using var writer = factory.Create();
        await using var reader = factory.Create();

        await writer.Repository<Exercise>()
            .AddAsync(new Exercise { Id = exerciseId, Name = "Row" }, TestContext.Current.CancellationToken);

        // Not yet committed, so a concurrently open session must not observe the row. This is what
        // makes a session safe to use for a background read while a workout is being written.
        (await reader.Repository<Exercise>().GetAsync(exerciseId, TestContext.Current.CancellationToken)).ShouldBeNull();

        await writer.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var afterCommit = factory.Create();
        (await afterCommit.Repository<Exercise>().GetAsync(exerciseId, TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    private ForgeDbContext CreateContext() => new(options);
}
