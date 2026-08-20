using System.Text.Json;
using Forge.Domain.Engagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forge.Infrastructure.Persistence.Configurations.Engagement;

public sealed class StreakConfiguration : IEntityTypeConfiguration<Streak>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Streak> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.History)
               .HasConversion(
                   history => JsonSerializer.Serialize(history, JsonOptions),
                   value => JsonSerializer.Deserialize<List<StreakDay>>(value, JsonOptions) ?? new List<StreakDay>());

        builder.HasIndex(e => e.UserProfileId);
    }
}

public sealed class AchievementConfiguration : IEntityTypeConfiguration<Achievement>
{
    public void Configure(EntityTypeBuilder<Achievement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Code).HasMaxLength(80).IsRequired();
        builder.Property(e => e.Title).HasMaxLength(120).IsRequired();
        builder.Property(e => e.EncouragingDescription).HasMaxLength(280).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasIndex(e => e.Category);
    }
}
