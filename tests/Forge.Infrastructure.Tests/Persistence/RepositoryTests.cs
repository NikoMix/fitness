using Forge.Domain.Training;
using Forge.Infrastructure.Persistence;
using Forge.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

public sealed class RepositoryTests : IAsyncLifetime
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
    public async Task Repository_round_trips_entities_through_unit_of_work()
    {
        var id = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            var repository = new EfRepository<Exercise>(context);
            var unitOfWork = new EfUnitOfWork(context);

            await repository.AddAsync(new Exercise { Id = id, Name = "Front Squat", Pattern = MovementPattern.Squat }, TestContext.Current.CancellationToken);
            await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = CreateContext();
        var loaded = await new EfRepository<Exercise>(verify).GetAsync(id, TestContext.Current.CancellationToken);

        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("Front Squat");
        loaded.Pattern.ShouldBe(MovementPattern.Squat);
    }

    [Fact]
    public async Task Soft_deleted_entities_are_filtered_from_repository_queries()
    {
        var deletedId = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            var deletingRepository = new EfRepository<Exercise>(context);
            var unitOfWork = new EfUnitOfWork(context);

            await deletingRepository.AddAsync(new Exercise { Name = "Visible" }, TestContext.Current.CancellationToken);
            await deletingRepository.AddAsync(new Exercise { Id = deletedId, Name = "Hidden" }, TestContext.Current.CancellationToken);
            await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

            await deletingRepository.SoftDeleteAsync(deletedId, TestContext.Current.CancellationToken);
            await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = CreateContext();
        var repository = new EfRepository<Exercise>(verify);

        (await repository.GetAsync(deletedId, TestContext.Current.CancellationToken)).ShouldBeNull();
        var visible = await repository.ListAsync(TestContext.Current.CancellationToken);
        visible.ShouldHaveSingleItem().Name.ShouldBe("Visible");
    }

    private ForgeDbContext CreateContext() => new(options);
}
