namespace Forge.Infrastructure.Persistence;

/// <summary>Outcome of local database startup initialization.</summary>
public sealed record DatabaseInitializationResult(
    DatabaseInitializationStatus Status,
    string? Message = null,
    Exception? Exception = null,
    IReadOnlyList<string>? IntegrityMessages = null)
{
    /// <summary>Successful initialization result.</summary>
    public static DatabaseInitializationResult Succeeded { get; } =
        new(DatabaseInitializationStatus.Succeeded);
}

/// <summary>Recoverable database startup states.</summary>
public enum DatabaseInitializationStatus
{
    /// <summary>The database was migrated and passed integrity checks.</summary>
    Succeeded,

    /// <summary>Schema migration failed; startup should surface a recovery path instead of looping.</summary>
    MigrationFailed,

    /// <summary>SQLite reported database corruption.</summary>
    Corrupt
}
