using Forge.Domain.Common;

namespace Forge.Infrastructure.Persistence.SeedContent;

internal sealed class SeedContentImport : Entity
{
    public required string CatalogueName { get; set; }

    public required int Version { get; set; }
}
