namespace Forge.Core.Abstractions.Data;

/// <summary>Commits pending persistence changes as one unit.</summary>
public interface IUnitOfWork
{
    /// <summary>Saves pending changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
