namespace Forge.Core.Abstractions.Data;

/// <summary>Provides the local database encryption key.</summary>
public interface IDatabaseKeyProvider
{
    /// <summary>Gets the database encryption key, creating and securely persisting it when missing.</summary>
    Task<string> GetOrCreateKeyAsync(CancellationToken cancellationToken);
}
