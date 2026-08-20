using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forge.Infrastructure.Persistence.SeedContent;

internal sealed class SeedContentImportConfiguration : IEntityTypeConfiguration<SeedContentImport>
{
    public void Configure(EntityTypeBuilder<SeedContentImport> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.CatalogueName).HasMaxLength(100).IsRequired();
        builder.HasIndex(e => e.CatalogueName).IsUnique();
    }
}
