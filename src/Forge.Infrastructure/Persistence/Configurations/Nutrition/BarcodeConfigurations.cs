using Forge.Domain.Nutrition.Barcodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forge.Infrastructure.Persistence.Configurations.Nutrition;

/// <summary>Maps <see cref="FoodBarcode" />.</summary>
/// <remarks>
/// Discovered by <c>ForgeDbContext</c> from this assembly, so adding barcode storage needed no
/// edit to the context or to any shared registration.
/// </remarks>
public sealed class FoodBarcodeConfiguration : IEntityTypeConfiguration<FoodBarcode>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FoodBarcode> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);

        // Fixed at 14 because Gtin14 is always zero-padded to the full GS1 key width.
        builder.Property(e => e.Gtin14).HasMaxLength(14).IsRequired();
        builder.Property(e => e.ScannedValue).HasMaxLength(14).IsRequired();

        // Stored as text. A scanned barcode outlives any renumbering of the enum, and a database
        // full of bare integers is unreadable the first time someone has to inspect it after a
        // restore has gone wrong.
        builder.Property(e => e.Symbology).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.Provenance).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(e => e.TimesScanned).IsRequired();

        // One food per code. Two rows for the same code would make which food a scan resolves to
        // depend on row order, so the same packet could log different things on different days.
        // The filter keeps a soft-deleted mapping from blocking the code being remembered again.
        builder.HasIndex(e => e.Gtin14).IsUnique().HasFilter("\"DeletedUtc\" IS NULL");

        builder.HasIndex(e => e.FoodItemId);

        // Cascade because a mapping to a deleted food resolves to nothing: a scan would find a row
        // and then fail to show a food, which reads as a bug rather than as an unknown barcode.
        builder.HasOne(e => e.Food)
               .WithMany()
               .HasForeignKey(e => e.FoodItemId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
