using Forge.Domain.Common;

namespace Forge.Core.Abstractions.Data;

/// <summary>
/// One unit of work over the local database.
/// </summary>
/// <remarks>
/// <para>
/// Every repository handed out by a session shares that session's underlying context, so
/// changes made through several repositories commit together in a single
/// <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </para>
/// <para>
/// This exists because resolving <see cref="IRepository{T}"/> and a separate unit of work from
/// the container cannot work: both are transient, so each would receive its own context and a
/// save would silently persist nothing. Rather than rely on everyone remembering to build the
/// repositories over one shared context by hand - which also drags the persistence
/// implementation into feature code - the session owns that pairing and makes it the only
/// available shape.
/// </para>
/// </remarks>
public interface IDataSession : IUnitOfWork, IAsyncDisposable
{
    /// <summary>Gets the repository for an entity type, bound to this session.</summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <returns>A repository sharing this session's change tracker.</returns>
    IRepository<T> Repository<T>()
        where T : Entity;
}

/// <summary>Creates <see cref="IDataSession"/> instances.</summary>
/// <remarks>
/// Sessions are short-lived and owned by the caller: open one per logical operation and dispose
/// it. A long-lived session would accumulate tracked entities for the life of the app and is not
/// safe to share across threads, which matters because Forge reads on background threads while a
/// workout is in progress.
/// </remarks>
public interface IDataSessionFactory
{
    /// <summary>Opens a new session. The caller disposes it.</summary>
    /// <returns>A session owning its own database context.</returns>
    IDataSession Create();
}
