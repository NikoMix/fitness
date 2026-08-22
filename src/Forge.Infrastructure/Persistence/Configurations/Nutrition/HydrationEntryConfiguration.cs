using Forge.Domain.Nutrition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forge.Infrastructure.Persistence.Configurations.Nutrition;

/// <summary>Maps <see cref="HydrationEntry" />.</summary>
public sealed class HydrationEntryConfiguration : IEntityTypeConfiguration<HydrationEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<HydrationEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.ConsumedUtc);
        builder.HasIndex(e => new { e.UserProfileId, e.ConsumedUtc });
        builder.Property(e => e.BeverageType).HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.CaffeineMilligrams).HasPrecision(10, 2);
        builder.Property(e => e.Volume)
               .HasConversion(v => v.Millilitres, ml => Volume.FromMillilitres(ml))
               .HasPrecision(10, 3)
               .HasColumnName("VolumeMillilitres");
    }
}
