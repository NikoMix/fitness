using Forge.Core.Abstractions.Data;
using Forge.Domain.Common;

namespace Forge.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IDataSession"/>.</summary>
/// <param name="dbContext">The context this session owns and disposes.</param>
public sealed class EfDataSession(ForgeDbContext dbContext) : IDataSession
{
    private readonly Dictionary<Type, object> repositories = [];

    /// <inheritdoc />
    /// <remarks>
    /// Repositories are cached per entity type so repeated calls return the same instance. They
    /// are stateless wrappers over the shared context, so this is purely to avoid churn.
    /// </remarks>
    public IRepository<T> Repository<T>()
        where T : Entity
    {
        if (repositories.TryGetValue(typeof(T), out var existing))
        {
            return (IRepository<T>)existing;
        }

        var repository = new EfRepository<T>(dbContext);
        repositories[typeof(T)] = repository;
        return repository;
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => dbContext.DisposeAsync();
}

/// <summary>Creates sessions over freshly opened database contexts.</summary>
/// <param name="contextFactory">Supplies a new context per session.</param>
/// <remarks>
/// Takes a factory delegate rather than a context so that the caller decides how the context is
/// configured - notably the database path and encryption key, which are only known after startup
/// has resolved them from platform secure storage.
/// </remarks>
public sealed class EfDataSessionFactory(Func<ForgeDbContext> contextFactory) : IDataSessionFactory
{
    /// <inheritdoc />
    public IDataSession Create() => new EfDataSession(contextFactory());
}
