namespace Forge.Infrastructure.Persistence.SeedContent;

/// <summary>Result of a versioned seed catalogue import.</summary>
public sealed record SeedContentImportResult(int Version, bool Imported, int Added, int Updated, int SkippedUserCreated);
