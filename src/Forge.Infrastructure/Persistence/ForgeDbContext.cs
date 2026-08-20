using System.Linq.Expressions;
using System.Reflection;
using Forge.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Forge.Infrastructure.Persistence;

/// <summary>
/// The Forge local database.
/// </summary>
/// <remarks>
/// <para>
/// This is the sole system of record. There is no server-side copy of anything, so a data-loss
/// defect here is unrecoverable for the user and there is no support team who can restore it.
/// </para>
/// <para>
/// Deliberately, this class declares no <see cref="DbSet{TEntity}"/> properties and no
/// per-entity mapping. Both are discovered from
/// <see cref="Microsoft.EntityFrameworkCore.IEntityTypeConfiguration{TEntity}"/> implementations
/// in this assembly. That is a parallel-development seam: a contributor adding a nutrition
/// entity writes one configuration file under Persistence/Configurations/Nutrition and touches
/// nothing shared, instead of every feature branch editing this file and conflicting. Access
/// entities with <c>Set&lt;T&gt;()</c>, normally through a repository rather than directly.
/// </para>
/// </remarks>
public sealed class ForgeDbContext(DbContextOptions<ForgeDbContext> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        ApplySoftDeleteFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Applies the soft-delete query filter to every entity deriving from <see cref="Entity"/>.
    /// </summary>
    /// <remarks>
    /// Applied centrally rather than in each configuration. Requiring every contributor to
    /// remember the filter would guarantee that one day someone forgets, and the symptom -
    /// deleted records reappearing inside a progress chart - is confusing and easy to miss in
    /// review.
    /// </remarks>
    private static void ApplySoftDeleteFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var property = Expression.Property(parameter, nameof(Entity.DeletedUtc));
            var nullConstant = Expression.Constant(null, typeof(DateTimeOffset?));
            var body = Expression.Equal(property, nullConstant);

            modelBuilder.Entity(entityType.ClrType)
                        .HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }

    /// <inheritdoc />
    public override int SaveChanges()
    {
        StampModified();
        return base.SaveChanges();
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampModified();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Maintains <see cref="Entity.ModifiedUtc"/> centrally.
    /// </summary>
    /// <remarks>
    /// Done here rather than at each call site because a single forgotten assignment would
    /// produce a row a future sync considers stale, and the resulting data loss would be silent
    /// and very hard to trace back.
    /// </remarks>
    private void StampModified()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.ModifiedUtc = now;
            }
        }
    }
}
