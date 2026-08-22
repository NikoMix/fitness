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

        // The uniqueness is per profile, not per device. A unique index on the date alone let the
        // first person to check in on a given morning block everybody else on a shared device from
        // checking in at all, and the failure surfaced as a database exception on save rather than
        // as anything the user could act on.
        builder.HasIndex(entry => new { entry.UserProfileId, entry.Date }).IsUnique();
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
        builder.HasIndex(entry => new { entry.UserProfileId, entry.RecordedOn });
    }
}
