using Forge.Domain.Nutrition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forge.Infrastructure.Persistence.Configurations.Nutrition;

/// <summary>Maps <see cref="FoodLogEntry" />.</summary>
public sealed class FoodLogEntryConfiguration : IEntityTypeConfiguration<FoodLogEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FoodLogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.ConsumedUtc);
        builder.HasIndex(e => e.FoodItemId);
        builder.HasIndex(e => new { e.UserProfileId, e.ConsumedUtc });
        builder.Property(e => e.MealSlot).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(e => e.Food)
               .WithMany()
               .HasForeignKey(e => e.FoodItemId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.OwnsOne(e => e.Serving, serving =>
        {
            serving.Property(s => s.ServingName).HasMaxLength(100).IsRequired();
            serving.Property(s => s.Quantity).HasPrecision(10, 3);
            serving.Property(s => s.GramsPerServing).HasPrecision(10, 3);
        });
    }
}
