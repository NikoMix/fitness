using System.Security.Cryptography;
using Forge.Core.Abstractions.Data;

namespace Forge.Infrastructure.Persistence;

/// <summary>Gets the SQLCipher key from platform secure storage, generating it on first run.</summary>
/// <remarks>
/// This type depends only on <see cref="ISecureStorage"/>. The MAUI head must adapt
/// Microsoft.Maui.Storage.SecureStorage to that interface; Infrastructure never writes the key
/// to preferences, files or source and never references MAUI directly.
/// </remarks>
public sealed class SecureStorageDatabaseKeyProvider(ISecureStorage secureStorage) : IDatabaseKeyProvider
{
    private const string StorageKey = "forge.database.encryption-key.v1";

    /// <inheritdoc />
    public async Task<string> GetOrCreateKeyAsync(CancellationToken cancellationToken)
    {
        var existing = await secureStorage.GetAsync(StorageKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var created = Convert.ToBase64String(keyBytes);
        await secureStorage.SetAsync(StorageKey, created, cancellationToken);
        return created;
    }
}
