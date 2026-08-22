using System.Text.Json;
using Forge.Domain.Engagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forge.Infrastructure.Persistence.Configurations.Engagement;

/// <summary>Maps the per-profile engagement record.</summary>
public sealed class StreakConfiguration : IEntityTypeConfiguration<Streak>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Streak> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);

        // Replaces the per-day history the daily streak needed. A protected period is a short list
        // of date ranges, so a JSON column keeps this one row per profile rather than introducing
        // a child table for at most a handful of entries.
        builder.Property(e => e.ProtectedPeriods)
               .HasConversion(
                   periods => JsonSerializer.Serialize(periods, JsonOptions),
                   value => JsonSerializer.Deserialize<List<ProtectedPeriod>>(value, JsonOptions) ?? new List<ProtectedPeriod>());

        builder.HasIndex(e => e.UserProfileId);
    }
}

/// <summary>Maps earned badges.</summary>
public sealed class AchievementConfiguration : IEntityTypeConfiguration<Achievement>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Achievement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Code).HasMaxLength(80).IsRequired();
        builder.Property(e => e.Title).HasMaxLength(120).IsRequired();
        builder.Property(e => e.EncouragingDescription).HasMaxLength(280).IsRequired();

        // The uniqueness is per profile, not per device. A globally unique Code meant the second
        // person on a shared tablet could never earn a badge the first person already held: the
        // insert would fail, and it would look like a bug in the evaluator rather than in the
        // schema. The owner leads the composite index because every read filters on it first.
        builder.HasIndex(e => new { e.UserProfileId, e.Code }).IsUnique();
        builder.HasIndex(e => e.UserProfileId);
        builder.HasIndex(e => e.Category);
    }
}
