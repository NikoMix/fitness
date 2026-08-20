using Forge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

public sealed class DatabaseInitializerTests
{
    [Fact]
    public async Task Migration_failure_returns_recoverable_state_instead_of_throwing()
    {
        var missingDirectory = Path.Combine(
            Environment.CurrentDirectory,
            "missing-database-directory",
            Guid.CreateVersion7().ToString("N"));

        var options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseSqlite($"Data Source={Path.Combine(missingDirectory, "forge.db")}")
            .Options;

        await using var context = new ForgeDbContext(options);
        var initializer = new DatabaseInitializer(context);

        var result = await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        result.Status.ShouldBe(DatabaseInitializationStatus.MigrationFailed);
        result.Exception.ShouldNotBeNull();
    }
}
