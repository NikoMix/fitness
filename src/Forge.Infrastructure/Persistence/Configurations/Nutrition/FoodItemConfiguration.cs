using Forge.Domain.Nutrition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forge.Infrastructure.Persistence.Configurations.Nutrition;

/// <summary>Maps <see cref="FoodItem" />.</summary>
public sealed class FoodItemConfiguration : IEntityTypeConfiguration<FoodItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FoodItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Brand).HasMaxLength(200);
        builder.HasIndex(e => e.Name);
        builder.HasIndex(e => e.Brand);

        builder.OwnsOne(e => e.Per100Grams, nutrients =>
        {
            nutrients.Property(n => n.EnergyKilocalories).HasPrecision(10, 2).HasColumnName("EnergyKilocaloriesPer100g");
            nutrients.Property(n => n.ProteinGrams).HasPrecision(10, 3).HasColumnName("ProteinGramsPer100g");
            nutrients.Property(n => n.CarbohydrateGrams).HasPrecision(10, 3).HasColumnName("CarbohydrateGramsPer100g");
            nutrients.Property(n => n.FatGrams).HasPrecision(10, 3).HasColumnName("FatGramsPer100g");
            nutrients.Property(n => n.FibreGrams).HasPrecision(10, 3).HasColumnName("FibreGramsPer100g");
            nutrients.Property(n => n.SugarGrams).HasPrecision(10, 3).HasColumnName("SugarGramsPer100g");
            nutrients.Property(n => n.SodiumMilligrams).HasPrecision(10, 3).HasColumnName("SodiumMilligramsPer100g");
        });

        builder.OwnsMany(e => e.Servings, serving =>
        {
            serving.ToTable("FoodItemServingDefinitions");
            serving.WithOwner().HasForeignKey("FoodItemId");
            serving.Property<int>("Id");
            serving.HasKey("Id");
            serving.Property(s => s.Name).HasMaxLength(100).IsRequired();
            serving.Property(s => s.Mass)
                   .HasConversion(m => m.Kilograms, kg => Forge.Domain.Measurement.Mass.FromKilograms(kg))
                   .HasPrecision(10, 6)
                   .HasColumnName("MassKilograms");
            serving.Property(s => s.Volume)
                   .HasConversion(v => v.HasValue ? v.Value.Millilitres : (decimal?)null, ml => ml.HasValue ? Volume.FromMillilitres(ml.Value) : null)
                   .HasPrecision(10, 3)
                   .HasColumnName("VolumeMillilitres");
        });
    }
}
