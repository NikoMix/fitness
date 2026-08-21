using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Forge.Infrastructure.Persistence;

/// <summary>
/// Supplies a <see cref="ForgeDbContext"/> to the <c>dotnet ef</c> tooling so migrations can be
/// scaffolded from the model.
/// </summary>
/// <remarks>
/// This type is only ever constructed by the design-time tools. It deliberately points at a
/// throwaway file and omits <see cref="SqlitePragmaConnectionInterceptor"/>: the interceptor
/// applies the SQLCipher key, and the tooling has no key to give it. Scaffolding reads the model
/// rather than the database, so no connection is opened and the omission cannot leak an
/// unencrypted database into a real profile directory.
/// </remarks>
public sealed class ForgeDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ForgeDbContext>
{
    /// <inheritdoc />
    public ForgeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), "forge-design-time.db")}")
            .Options;

        return new ForgeDbContext(options);
    }
}
