using Forge.Core.Abstractions.Data;
using Forge.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Forge.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IRepository{T}"/>.</summary>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class EfRepository<T>(ForgeDbContext dbContext) : IRepository<T>
    where T : Entity
{
    /// <inheritdoc />
    public Task<T?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<T>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<T>().ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(T entity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await dbContext.Set<T>().AddAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateAsync(T entity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        cancellationToken.ThrowIfCancellationRequested();
        dbContext.Set<T>().Update(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await GetAsync(id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.DeletedUtc = DateTimeOffset.UtcNow;
        dbContext.Set<T>().Update(entity);
    }
}
