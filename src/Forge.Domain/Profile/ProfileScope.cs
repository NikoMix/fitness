using System.Linq.Expressions;

namespace Forge.Domain.Profile;

/// <summary>
/// Marks a persisted record as belonging to exactly one local profile.
/// </summary>
/// <remarks>
/// <para>
/// This is the adoption seam for multi-profile support. An entity joins the profile boundary by
/// declaring a <see cref="UserProfileId"/> property and adding this interface to its declaration;
/// every query over it then becomes correct by adding one <c>OwnedBy(scope)</c> call. Nothing
/// else about the entity changes.
/// </para>
/// <para>
/// The interface exists rather than a plain convention because it makes the boundary checkable.
/// <see cref="ProfileDataAreas"/> reflects over it to report which parts of Forge are genuinely
/// separated per profile, which is what lets the profile switcher tell the truth instead of
/// implying a separation that does not exist yet. A convention could not be inspected, so the UI
/// would have to hard-code a claim that silently rots as features are migrated.
/// </para>
/// </remarks>
public interface IProfileOwned
{
    /// <summary>The profile that owns this record.</summary>
    Guid UserProfileId { get; }
}

/// <summary>
/// The single profile that a read or a write is confined to.
/// </summary>
/// <remarks>
/// <para>
/// Passing a scope explicitly, rather than reading an ambient "current profile" inside each
/// query, is deliberate. Forge reads on background threads while a workout is being written, and
/// an ambient value that changes mid-operation would produce a half-scoped result set: some rows
/// from one person, some from another. An explicit value cannot drift once an operation starts.
/// </para>
/// <para>
/// The default value is <see cref="None"/> and it matches nothing. Filtering is fail-closed
/// because this is a privacy boundary in a health app: showing one person an empty screen is a
/// bug worth a support message, whereas showing them somebody else's training history is a breach
/// of the promise that makes Forge local-only in the first place.
/// </para>
/// </remarks>
/// <param name="ProfileId">The owning profile identifier.</param>
public readonly record struct ProfileScope(Guid ProfileId)
{
    /// <summary>A scope that resolves to no profile and therefore matches no records.</summary>
    public static ProfileScope None => default;

    /// <summary>Whether this scope names a real profile.</summary>
    public bool IsResolved => ProfileId != Guid.Empty;

    /// <summary>Creates a scope for a profile.</summary>
    /// <param name="profile">The profile to scope to.</param>
    /// <returns>A scope naming that profile.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is <see langword="null"/>.</exception>
    public static ProfileScope For(UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new ProfileScope(profile.Id);
    }

    /// <summary>Whether a record belongs to this scope.</summary>
    /// <param name="owned">The record to test.</param>
    /// <returns><see langword="true"/> only when the scope is resolved and owns the record.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="owned"/> is <see langword="null"/>.</exception>
    public bool Owns(IProfileOwned owned)
    {
        ArgumentNullException.ThrowIfNull(owned);
        return IsResolved && owned.UserProfileId == ProfileId;
    }
}

/// <summary>
/// Confines a sequence or a query to one profile.
/// </summary>
/// <remarks>
/// Adoption is intended to be a single inserted call. A query that reads
/// <c>(await repository.ListAsync(token)).Where(x =&gt; !x.IsDeleted)</c> becomes correct as
/// <c>(await repository.ListAsync(token)).OwnedBy(scope).Where(x =&gt; !x.IsDeleted)</c>.
/// </remarks>
public static class ProfileScopeExtensions
{
    /// <summary>Filters an in-memory sequence to the records owned by a scope.</summary>
    /// <typeparam name="T">The profile-owned record type.</typeparam>
    /// <param name="source">The sequence to filter.</param>
    /// <param name="scope">The profile to confine the sequence to.</param>
    /// <returns>Only the records owned by <paramref name="scope"/>, or nothing when it is unresolved.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static IEnumerable<T> OwnedBy<T>(this IEnumerable<T> source, ProfileScope scope)
        where T : IProfileOwned
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!scope.IsResolved)
        {
            return [];
        }

        var profileId = scope.ProfileId;
        return source.Where(record => record.UserProfileId == profileId);
    }

    /// <summary>Filters a database query to the records owned by a scope.</summary>
    /// <typeparam name="T">The profile-owned entity type.</typeparam>
    /// <param name="source">The query to filter.</param>
    /// <param name="scope">The profile to confine the query to.</param>
    /// <returns>A query returning only the records owned by <paramref name="scope"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The predicate is built against the concrete entity type rather than written as
    /// <c>record =&gt; record.UserProfileId == id</c>. A lambda over the generic parameter compiles
    /// to member access on <see cref="IProfileOwned"/>, which EF Core cannot map to a column, so
    /// the filter would fail to translate and quietly fall back to evaluating the whole table.
    /// </remarks>
    public static IQueryable<T> OwnedBy<T>(this IQueryable<T> source, ProfileScope scope)
        where T : class, IProfileOwned
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!scope.IsResolved)
        {
            return source.Where(_ => false);
        }

        var record = Expression.Parameter(typeof(T), "record");
        var owner = Expression.Property(record, nameof(IProfileOwned.UserProfileId));

        // Reading the identifier off a captured object rather than embedding it as a constant is
        // what the C# compiler does for a closure, and it is what makes EF Core emit a SQL
        // parameter. A literal would produce a distinct query per profile and defeat the plan cache.
        var capture = Expression.Property(
            Expression.Constant(new ScopeCapture(scope.ProfileId)),
            nameof(ScopeCapture.ProfileId));

        return source.Where(Expression.Lambda<Func<T, bool>>(Expression.Equal(owner, capture), record));
    }

    private sealed class ScopeCapture(Guid profileId)
    {
        public Guid ProfileId { get; } = profileId;
    }
}
