using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace Forge.Infrastructure.Persistence.Configurations.Training;

/// <summary>Maps <see cref="Exercise"/>.</summary>
public sealed class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Exercise> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.PrimaryMuscle).HasMaxLength(100);
        builder.Property(e => e.SecondaryMuscles)
               .HasConversion(
                   muscles => SerializeList(muscles),
                   value => DeserializeList(value));
        builder.Property(e => e.Equipment).HasMaxLength(100);
        builder.Property(e => e.ExecutionSteps)
               .HasConversion(
                   steps => SerializeList(steps),
                   value => DeserializeList(value));
        builder.Property(e => e.CommonMistakes)
               .HasConversion(
                   mistakes => SerializeList(mistakes),
                   value => DeserializeList(value));
        builder.Property(e => e.CoachingCues)
               .HasConversion(
                   cues => SerializeList(cues),
                   value => DeserializeList(value));
        builder.Property(e => e.SafetyNotes)
               .HasConversion(
                   notes => SerializeList(notes),
                   value => DeserializeList(value));

        // The catalogue is browsed and searched constantly. Filtering by equipment is the
        // highest-value filter in the product because it answers "what can I actually do with
        // what is in front of me right now".
        builder.HasIndex(e => e.Name);
        builder.HasIndex(e => e.Equipment);
        builder.HasIndex(e => e.Pattern);
        builder.HasIndex(e => e.IsFavourite);
        builder.HasIndex(e => e.LastUsedUtc);
    }

    private static string SerializeList(List<string> values)
        => JsonSerializer.Serialize(values, JsonOptions);

    private static List<string> DeserializeList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : JsonSerializer.Deserialize<List<string>>(value, JsonOptions) ?? [];
}

/// <summary>Maps <see cref="WorkoutSession"/>.</summary>
public sealed class WorkoutSessionConfiguration : IEntityTypeConfiguration<WorkoutSession>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WorkoutSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Title).HasMaxLength(200);

        builder.HasMany(e => e.Sets)
               .WithOne()
               .HasForeignKey(s => s.WorkoutSessionId)
               .OnDelete(DeleteBehavior.Cascade);

        // History is read newest-first almost everywhere in the app.
        builder.HasIndex(e => e.StartedUtc);

        // Every read of this table is confined to one profile, so the owner leads the composite
        // index rather than sitting in one of its own.
        builder.HasIndex(e => new { e.UserProfileId, e.StartedUtc });

        // Locating an unfinished session is the first thing the app does after a crash or
        // process death, so it must not require scanning the table.
        builder.HasIndex(e => e.CompletedUtc);
        builder.HasIndex(e => new { e.UserProfileId, e.CompletedUtc });
    }
}

/// <summary>Maps <see cref="SetEntry"/>.</summary>
public sealed class SetEntryConfiguration : IEntityTypeConfiguration<SetEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SetEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);

        // Mass is a value type over decimal. Storing canonical kilograms keeps the database
        // unit-unambiguous and lets display preference change freely. The precision represents
        // the smallest real plate increment (1.25 kg, or 0.25 kg micro-plates) exactly, which
        // a float column could not.
        builder.Property(e => e.Load)
               .HasConversion(m => m.Kilograms, kg => Mass.FromKilograms(kg))
               .HasPrecision(10, 3)
               .HasColumnName("LoadKilograms");

        // The dominant query is "every set of this exercise over time", which drives both the
        // progression charts and personal-record detection. Since that query is now confined to
        // one profile, the owner leads the index: an index that does not start with the column
        // every read filters on cannot be used to satisfy it.
        builder.HasIndex(e => new { e.ExerciseId, e.CompletedUtc });
        builder.HasIndex(e => new { e.UserProfileId, e.ExerciseId, e.CompletedUtc });
        builder.HasIndex(e => e.WorkoutSessionId);
        builder.HasIndex(e => e.UserProfileId);
    }
}
