using Forge.Domain.Profile;
using Forge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Forge.Infrastructure.Backup;

/// <summary>
/// How a table's rows can be attributed to a single profile, and the SQL that does it.
/// </summary>
/// <param name="Table">The database table.</param>
/// <param name="Predicate">
/// A SQL boolean expression over the table, parameterised by <c>@profileId</c>, or
/// <see langword="null"/> when Forge cannot tell whose rows these are.
/// </param>
/// <param name="UnassignedPredicate">
/// A SQL boolean expression matching rows that carry an owner column nobody filled in, or
/// <see langword="null"/> when the question does not apply.
/// </param>
/// <param name="ClrTypes">The entity types mapped to the table.</param>
internal sealed record TableAttribution(string Table, string? Predicate, string? UnassignedPredicate, IReadOnlyList<Type> ClrTypes)
{
    /// <summary>Whether a scoped export may include rows from this table at all.</summary>
    public bool IsAttributable => Predicate is not null;
}

/// <summary>
/// Works out, from the database model alone, which tables carry an owner.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here names an entity type. Attribution is derived from
/// <c>typeof(IProfileOwned).IsAssignableFrom(clrType)</c>, exactly as
/// <see cref="ProfileDataAreas"/> derives Separated from Shared, so a type that adopts the seam in
/// another branch becomes exportable here with no edit to this file. A hard-coded list would be
/// correct on the day it was written and silently wrong on the day a feature migrated - and the
/// symptom of "silently wrong" in this direction is one person receiving another person's health
/// data in a file they asked for.
/// </para>
/// <para>
/// Everything is fail-closed. A table whose owner column cannot be located, whose foreign key is
/// composite, or which several entity types disagree about, is reported as unattributable and left
/// out of a scoped export rather than guessed at.
/// </para>
/// <para>
/// The predicates are built as SQL text against model metadata rather than as LINQ over the
/// generic entity type on purpose. Forge exports by reading tables through ADO.NET, and the
/// alternative - resolving <c>Set&lt;T&gt;()</c> for a type discovered at runtime - needs
/// <c>MakeGenericMethod</c>, which works on Android and throws on an ahead-of-time compiled iOS
/// build.
/// </para>
/// </remarks>
internal sealed class ProfileAttributionMap
{
    /// <summary>The SQL parameter every predicate is written against.</summary>
    internal const string ProfileParameterName = "@profileId";

    /// <summary>The SQL parameter standing for "no profile", used to find unassigned rows.</summary>
    internal const string UnassignedParameterName = "@unassignedProfileId";

    private readonly IReadOnlyDictionary<string, TableAttribution> byTable;

    private ProfileAttributionMap(IReadOnlyDictionary<string, TableAttribution> byTable) => this.byTable = byTable;

    /// <summary>Builds the map for a context's model.</summary>
    /// <param name="dbContext">The context whose model describes the tables.</param>
    /// <returns>An attribution for every mapped table.</returns>
    internal static ProfileAttributionMap Build(ForgeDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var byTable = new Dictionary<string, TableAttribution>(StringComparer.Ordinal);
        foreach (var group in dbContext.Model.GetEntityTypes()
            .Select(entityType => (EntityType: entityType, Table: PortableBackupFormat.GetTableName(entityType)))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Table))
            .GroupBy(pair => pair.Table!, StringComparer.Ordinal))
        {
            var entityTypes = group.Select(pair => pair.EntityType).ToList();
            var predicates = entityTypes
                .Select(entityType => BuildPredicate(entityType, []))
                .ToList();

            // Several entity types can share one table through inheritance. The table is only
            // attributable when every one of them agrees on the same filter; one unattributable
            // type in the set would otherwise let its rows through unfiltered.
            var distinct = predicates.Distinct(StringComparer.Ordinal).ToList();
            var predicate = predicates.Any(static value => value is null) || distinct.Count != 1
                ? null
                : distinct[0];

            var unassigned = predicate is null
                ? null
                : BuildUnassignedPredicate(entityTypes);

            byTable[group.Key] = new TableAttribution(
                group.Key,
                predicate,
                unassigned,
                entityTypes.Select(static entityType => entityType.ClrType).Distinct().ToList());
        }

        return new ProfileAttributionMap(byTable);
    }

    /// <summary>Attribution for one table.</summary>
    /// <param name="table">The table name.</param>
    /// <returns>Its attribution, or an unattributable placeholder for an unknown table.</returns>
    internal TableAttribution For(string table)
        => byTable.TryGetValue(table, out var attribution) ? attribution : new TableAttribution(table, null, null, []);

    /// <summary>
    /// Finds rows whose owner column exists but was never filled in.
    /// </summary>
    /// <remarks>
    /// A type joins the profile boundary by gaining a <c>UserProfileId</c>, and every row that
    /// already existed gets whatever the migration defaulted to - which for a non-nullable Guid is
    /// the empty one. Those rows belong to a real person and match no scope, so without this they
    /// would disappear from every personal export with nothing said about them. That is the exact
    /// shape of failure this feature exists to prevent, so they are counted and reported.
    /// </remarks>
    private static string? BuildUnassignedPredicate(IReadOnlyList<IEntityType> entityTypes)
    {
        var owner = entityTypes
            .Where(static entityType => typeof(IProfileOwned).IsAssignableFrom(entityType.ClrType))
            .Select(entityType => (Store: StoreObjectIdentifier.Create(entityType, StoreObjectType.Table), Property: entityType.FindProperty(nameof(IProfileOwned.UserProfileId))))
            .Where(static pair => pair.Store is not null && pair.Property is not null)
            .Select(pair => pair.Property!.GetColumnName(pair.Store!.Value))
            .FirstOrDefault(static column => column is not null);

        if (owner is null)
        {
            return null;
        }

        var quoted = PortableBackupFormat.QuoteIdentifier(owner);
        return $"({quoted} IS NULL OR {quoted} = {UnassignedParameterName})";
    }

    private static string? BuildPredicate(IEntityType entityType, HashSet<IEntityType> visited)
    {
        if (!visited.Add(entityType))
        {
            return null;
        }

        var storeObject = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);
        if (storeObject is null)
        {
            return null;
        }

        if (typeof(IProfileOwned).IsAssignableFrom(entityType.ClrType))
        {
            var owner = entityType.FindProperty(nameof(IProfileOwned.UserProfileId));
            var column = owner?.GetColumnName(storeObject.Value);
            return column is null
                ? null
                : $"{PortableBackupFormat.QuoteIdentifier(column)} = {ProfileParameterName}";
        }

        if (entityType.ClrType == ProfileDataAreas.ProfileEntityType)
        {
            // The profile row is not owned by a profile, it is the profile. A subject access
            // request plainly covers the requester's own name, goals and setup.
            var key = entityType.FindPrimaryKey()?.Properties;
            var column = key is { Count: 1 } ? key[0].GetColumnName(storeObject.Value) : null;
            return column is null
                ? null
                : $"{PortableBackupFormat.QuoteIdentifier(column)} = {ProfileParameterName}";
        }

        var ownership = entityType.FindOwnership();
        return ownership is null ? null : BuildOwnedPredicate(entityType, ownership, storeObject.Value, visited);
    }

    private static string? BuildOwnedPredicate(
        IEntityType entityType,
        IForeignKey ownership,
        StoreObjectIdentifier storeObject,
        HashSet<IEntityType> visited)
    {
        var principal = ownership.PrincipalEntityType;
        var principalPredicate = BuildPredicate(principal, visited);
        if (principalPredicate is null)
        {
            return null;
        }

        var principalTable = PortableBackupFormat.GetTableName(principal);
        if (principalTable is null)
        {
            return null;
        }

        // An owned reference shares its owner's row, so the owner's own filter already applies.
        if (string.Equals(principalTable, entityType.GetTableName(), StringComparison.Ordinal))
        {
            return principalPredicate;
        }

        // An owned collection lives in its own table and is reachable only through the owner's key.
        if (ownership.Properties.Count != 1 || ownership.PrincipalKey.Properties.Count != 1)
        {
            return null;
        }

        var principalStore = StoreObjectIdentifier.Create(principal, StoreObjectType.Table);
        if (principalStore is null)
        {
            return null;
        }

        var foreignColumn = ownership.Properties[0].GetColumnName(storeObject);
        var principalColumn = ownership.PrincipalKey.Properties[0].GetColumnName(principalStore.Value);
        if (foreignColumn is null || principalColumn is null)
        {
            return null;
        }

        return $"{PortableBackupFormat.QuoteIdentifier(foreignColumn)} IN (SELECT {PortableBackupFormat.QuoteIdentifier(principalColumn)} FROM {PortableBackupFormat.QuoteIdentifier(principalTable)} WHERE {principalPredicate})";
    }
}
