using Forge.Domain.Common;

namespace Forge.Core.Abstractions.Data;

/// <summary>Persistence-agnostic repository for aggregate entities.</summary>
/// <typeparam name="T">The entity type.</typeparam>
public interface IRepository<T>
    where T : Entity
{
    /// <summary>Gets a live entity by stable identifier, or <see langword="null"/> when absent.</summary>
    Task<T?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Lists all live entities of this type.</summary>
    Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Adds a new entity.</summary>
    Task AddAsync(T entity, CancellationToken cancellationToken);

    /// <summary>Marks an existing entity as modified.</summary>
    Task UpdateAsync(T entity, CancellationToken cancellationToken);

    /// <summary>Soft-deletes a live entity by stable identifier.</summary>
    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken);
}
