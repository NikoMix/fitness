using Forge.Domain.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forge.Infrastructure.Persistence.Configurations.Recovery;

/// <summary>Maps <see cref="MorningCheckIn" />.</summary>
public sealed class MorningCheckInConfiguration : IEntityTypeConfiguration<MorningCheckIn>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MorningCheckIn> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.SleepHours).HasPrecision(4, 2);
        builder.HasIndex(entry => entry.Date).IsUnique();
    }
}

/// <summary>Maps <see cref="SorenessEntry" />.</summary>
public sealed class SorenessEntryConfiguration : IEntityTypeConfiguration<SorenessEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SorenessEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.MuscleGroup).HasMaxLength(100).IsRequired();
        builder.HasIndex(entry => new { entry.MuscleGroup, entry.RecordedOn });
    }
}
