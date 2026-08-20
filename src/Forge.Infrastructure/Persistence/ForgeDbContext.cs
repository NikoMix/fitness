using Forge.Domain.Common;
using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Microsoft.EntityFrameworkCore;

namespace Forge.Infrastructure.Persistence;

/// <summary>
/// The Forge local database.
/// </summary>
/// <remarks>
/// <para>
/// This is the sole system of record. There is no server-side copy of anything, so a data-loss
/// defect here is unrecoverable for the user and there is no support team who can restore it.
/// That shapes several choices below.
/// </para>
/// </remarks>
public sealed class ForgeDbContext(DbContextOptions<ForgeDbContext> options) : DbContext(options)
{
    /// <summary>The exercise catalogue, both shipped and user-created.</summary>
    public DbSet<Exercise> Exercises => Set<Exercise>();

    /// <summary>Training sessions.</summary>
    public DbSet<WorkoutSession> WorkoutSessions => Set<WorkoutSession>();

    /// <summary>Individual performed sets.</summary>
    public DbSet<SetEntry> SetEntries => Set<SetEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Exercise>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.PrimaryMuscle).HasMaxLength(100);
            entity.Property(e => e.Equipment).HasMaxLength(100);

            // The catalogue is browsed and searched constantly, and filtering by equipment is
            // the highest-value filter in the product because it answers "what can I actually
            // do with what is in front of me right now".
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.Equipment);
            entity.HasIndex(e => e.Pattern);
        });

        modelBuilder.Entity<WorkoutSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(200);

            entity.HasMany(e => e.Sets)
                  .WithOne()
                  .HasForeignKey(s => s.WorkoutSessionId)
                  .OnDelete(DeleteBehavior.Cascade);

            // History is read newest-first almost everywhere in the app.
            entity.HasIndex(e => e.StartedUtc);

            // Finding an unfinished session is the first thing the app does after a crash or
            // process death, so it must not require scanning the table.
            entity.HasIndex(e => e.CompletedUtc);
        });

        modelBuilder.Entity<SetEntry>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Mass is a value type over decimal. Storing the canonical kilograms keeps the
            // database unit-unambiguous and lets display preference change freely.
            // The precision is chosen to represent the smallest real plate increment
            // (1.25 kg, and 0.25 kg for micro-plates) without floating-point drift.
            entity.Property(e => e.Load)
                  .HasConversion(m => m.Kilograms, kg => Mass.FromKilograms(kg))
                  .HasPrecision(10, 3)
                  .HasColumnName("LoadKilograms");

            // The dominant query is "every set of this exercise over time", which drives the
            // progression charts and personal-record detection.
            entity.HasIndex(e => new { e.ExerciseId, e.CompletedUtc });
            entity.HasIndex(e => e.WorkoutSessionId);
        });

        // Soft-deleted rows are filtered out globally so no query has to remember to exclude
        // them. Forgetting that filter even once would resurrect deleted data in a chart.
        modelBuilder.Entity<Exercise>().HasQueryFilter(e => e.DeletedUtc == null);
        modelBuilder.Entity<WorkoutSession>().HasQueryFilter(e => e.DeletedUtc == null);
        modelBuilder.Entity<SetEntry>().HasQueryFilter(e => e.DeletedUtc == null);

        base.OnModelCreating(modelBuilder);
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
    /// produce a row that a future sync considers stale, and the resulting data loss would be
    /// silent and very hard to trace back.
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
