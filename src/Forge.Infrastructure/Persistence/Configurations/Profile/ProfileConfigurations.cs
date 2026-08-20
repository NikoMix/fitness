using Forge.Domain.Measurement;
using Forge.Domain.Profile;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forge.Infrastructure.Persistence.Configurations.Profile;

/// <summary>Maps <see cref="UserProfile"/>.</summary>
public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(e => e.AvailableEquipment).HasMaxLength(500);
        builder.Property(e => e.DateOfBirth).HasConversion(d => d, d => d);
        builder.Property(e => e.Height)
               .HasConversion(l => l.Centimetres, cm => Length.FromCentimetres(cm))
               .HasPrecision(8, 2)
               .HasColumnName("HeightCentimetres");

        builder.HasIndex(e => e.DisplayName);
    }
}

/// <summary>Maps <see cref="BodyMetric"/>.</summary>
public sealed class BodyMetricConfiguration : IEntityTypeConfiguration<BodyMetric>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BodyMetric> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Weight)
               .HasConversion(m => m.Kilograms, kg => Mass.FromKilograms(kg))
               .HasPrecision(10, 3)
               .HasColumnName("WeightKilograms");

        builder.Property(e => e.BodyFatPercentage)
               .HasConversion(p => p.HasValue ? p.Value.Value : (decimal?)null, value => value.HasValue ? Percentage.FromValue(value.Value) : null)
               .HasPrecision(5, 2)
               .HasColumnName("BodyFatPercentage");

        ConfigureOptionalLength(builder.Property(e => e.WaistCircumference), "WaistCentimetres");
        ConfigureOptionalLength(builder.Property(e => e.HipCircumference), "HipCentimetres");
        ConfigureOptionalLength(builder.Property(e => e.ChestCircumference), "ChestCentimetres");
        ConfigureOptionalLength(builder.Property(e => e.ThighCircumference), "ThighCentimetres");

        builder.HasIndex(e => new { e.UserProfileId, e.RecordedUtc });
    }

    private static void ConfigureOptionalLength(PropertyBuilder<Length?> property, string columnName)
    {
        property.HasConversion(l => l.HasValue ? l.Value.Centimetres : (decimal?)null, value => value.HasValue ? Length.FromCentimetres(value.Value) : null)
                .HasPrecision(8, 2)
                .HasColumnName(columnName);
    }
}
