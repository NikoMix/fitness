using Forge.Domain.Measurement;
using Forge.Domain.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace Forge.Infrastructure.Persistence.Configurations.Planning;

/// <summary>Maps <see cref="TrainingPlan"/>.</summary>
public sealed class TrainingPlanConfiguration : IEntityTypeConfiguration<TrainingPlan>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TrainingPlan> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.Name).HasMaxLength(200).IsRequired();
        builder.Property(plan => plan.Description).HasMaxLength(1200);
        builder.HasMany(plan => plan.Days).WithOne().HasForeignKey(day => day.TrainingPlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(plan => plan.IsTemplate);
    }
}

/// <summary>Maps <see cref="PlanDay"/>.</summary>
public sealed class PlanDayConfiguration : IEntityTypeConfiguration<PlanDay>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlanDay> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(day => day.Id);
        builder.Property(day => day.Name).HasMaxLength(160).IsRequired();
        builder.HasMany(day => day.Exercises).WithOne().HasForeignKey(exercise => exercise.PlanDayId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(day => new { day.TrainingPlanId, day.Ordinal });
        builder.HasIndex(day => day.ScheduledDay);
    }
}

/// <summary>Maps <see cref="PlannedExercise"/>.</summary>
public sealed class PlannedExerciseConfiguration : IEntityTypeConfiguration<PlannedExercise>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlannedExercise> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(exercise => exercise.Id);
        builder.Property(exercise => exercise.ExerciseName).HasMaxLength(200).IsRequired();
        builder.Property(exercise => exercise.PrimaryMuscle).HasMaxLength(100);
        builder.Property(exercise => exercise.GroupKey).HasMaxLength(32);
        builder.Property(exercise => exercise.SecondaryMuscles)
               .HasConversion(muscles => JsonSerializer.Serialize(muscles, JsonOptions), value => DeserializeList(value));
        builder.HasMany(exercise => exercise.Sets).WithOne().HasForeignKey(set => set.PlannedExerciseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(exercise => new { exercise.PlanDayId, exercise.Ordinal });
        builder.HasIndex(exercise => exercise.Pattern);
    }

    private static List<string> DeserializeList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : JsonSerializer.Deserialize<List<string>>(value, JsonOptions) ?? [];
}

/// <summary>Maps <see cref="PlannedSet"/>.</summary>
public sealed class PlannedSetConfiguration : IEntityTypeConfiguration<PlannedSet>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlannedSet> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(set => set.Id);
        builder.Property(set => set.TargetLoad)
               .HasConversion(load => load.HasValue ? load.Value.Kilograms : (decimal?)null,
                   kilograms => kilograms.HasValue ? Mass.FromKilograms(kilograms.Value) : null)
               .HasPrecision(10, 3)
               .HasColumnName("TargetLoadKilograms");
        builder.Property(set => set.TargetRpe).HasPrecision(4, 1);
        builder.HasIndex(set => new { set.PlannedExerciseId, set.Ordinal });
    }
}
