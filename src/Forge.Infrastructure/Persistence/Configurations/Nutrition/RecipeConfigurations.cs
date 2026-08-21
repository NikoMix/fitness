using Forge.Domain.Nutrition;
using Forge.Domain.Nutrition.Recipes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forge.Infrastructure.Persistence.Configurations.Nutrition;

/// <summary>Maps recipe aggregates and owned recipe values.</summary>
public sealed class RecipeConfigurations : IEntityTypeConfiguration<Recipe>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Recipe> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(recipe => recipe.Id);
        builder.Property(recipe => recipe.Name).HasMaxLength(200).IsRequired();
        builder.Property(recipe => recipe.Description).HasMaxLength(500).IsRequired();
        builder.Property(recipe => recipe.BaseServings).IsRequired();
        builder.Property(recipe => recipe.PrepTime).HasConversion(t => t.TotalMinutes, minutes => TimeSpan.FromMinutes(minutes));
        builder.Property(recipe => recipe.CookTime).HasConversion(t => t.TotalMinutes, minutes => TimeSpan.FromMinutes(minutes));
        builder.Property(recipe => recipe.Provenance).HasMaxLength(300).IsRequired();
        builder.HasIndex(recipe => recipe.Name);

        builder.OwnsMany(recipe => recipe.Ingredients, ingredient =>
        {
            ingredient.ToTable("RecipeIngredients");
            ingredient.WithOwner().HasForeignKey("RecipeId");
            ingredient.Property<int>("Id");
            ingredient.HasKey("Id");
            ingredient.Property(i => i.SortOrder).IsRequired();
            ingredient.Property(i => i.Name).HasMaxLength(160).IsRequired();
            ingredient.Property(i => i.Quantity).HasPrecision(10, 3);
            ingredient.Property(i => i.Unit).HasConversion<string>().HasMaxLength(32);
            ingredient.Property(i => i.EdibleMass)
                .HasConversion(m => m.Kilograms, kg => Forge.Domain.Measurement.Mass.FromKilograms(kg))
                .HasPrecision(10, 6)
                .HasColumnName("EdibleMassKilograms");
            ingredient.Property(i => i.Volume)
                .HasConversion(v => v.HasValue ? v.Value.Millilitres : (decimal?)null, ml => ml.HasValue ? Volume.FromMillilitres(ml.Value) : null)
                .HasPrecision(10, 3)
                .HasColumnName("VolumeMillilitres");
            ingredient.Property(i => i.PreparationNote).HasMaxLength(200);
            ingredient.OwnsOne(i => i.Per100Grams, nutrients =>
            {
                nutrients.Property(n => n.EnergyKilocalories).HasPrecision(10, 2).HasColumnName("EnergyKilocaloriesPer100g");
                nutrients.Property(n => n.ProteinGrams).HasPrecision(10, 3).HasColumnName("ProteinGramsPer100g");
                nutrients.Property(n => n.CarbohydrateGrams).HasPrecision(10, 3).HasColumnName("CarbohydrateGramsPer100g");
                nutrients.Property(n => n.FatGrams).HasPrecision(10, 3).HasColumnName("FatGramsPer100g");
                nutrients.Property(n => n.FibreGrams).HasPrecision(10, 3).HasColumnName("FibreGramsPer100g");
                nutrients.Property(n => n.SugarGrams).HasPrecision(10, 3).HasColumnName("SugarGramsPer100g");
                nutrients.Property(n => n.SodiumMilligrams).HasPrecision(10, 3).HasColumnName("SodiumMilligramsPer100g");
            });
        });

        builder.OwnsMany(recipe => recipe.Steps, step =>
        {
            step.ToTable("RecipeSteps");
            step.WithOwner().HasForeignKey("RecipeId");
            step.Property<int>("Id");
            step.HasKey("Id");
            step.Property(s => s.SortOrder).IsRequired();
            step.Property(s => s.Instruction).HasMaxLength(500).IsRequired();
        });

        builder.OwnsMany(recipe => recipe.Tags, tag =>
        {
            tag.ToTable("RecipeTags");
            tag.WithOwner().HasForeignKey("RecipeId");
            tag.Property<int>("Id");
            tag.HasKey("Id");
            tag.Property(t => t.Tag).HasConversion<string>().HasMaxLength(40).IsRequired();
        });
    }
}
