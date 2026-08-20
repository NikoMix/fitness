using Forge.Core.Abstractions.Data;

namespace Forge.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IUnitOfWork"/>.</summary>
public sealed class EfUnitOfWork(ForgeDbContext dbContext) : IUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
