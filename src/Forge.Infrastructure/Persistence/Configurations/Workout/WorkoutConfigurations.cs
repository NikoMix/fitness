using System.Text.Json;
using Forge.Domain.Workout;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forge.Infrastructure.Persistence.Configurations.Workout;

/// <summary>Maps the recoverable active workout snapshot.</summary>
public sealed class ActiveWorkoutStateConfiguration : IEntityTypeConfiguration<ActiveWorkoutState>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<ActiveWorkoutState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.CurrentExerciseName).HasMaxLength(200);
        builder.Property(e => e.ExerciseQueue)
               .HasConversion(v => JsonSerializer.Serialize(v, JsonOptions), v => JsonSerializer.Deserialize<List<ActiveWorkoutExercise>>(v, JsonOptions) ?? new());
        builder.Property(e => e.CompletedSets)
               .HasConversion(v => JsonSerializer.Serialize(v, JsonOptions), v => JsonSerializer.Deserialize<List<CompletedWorkoutSet>>(v, JsonOptions) ?? new());
        builder.OwnsOne(e => e.ActiveRestTimer);

        builder.HasIndex(e => e.WorkoutSessionId).IsUnique();
        builder.HasIndex(e => e.CompletedUtc);
        builder.HasIndex(e => e.UserProfileId);
    }
}
