using ForgeSecureStorage = Forge.Core.Abstractions.Data.ISecureStorage;

namespace Forge.App.Services.Storage;

/// <summary>
/// Adapts MAUI <see cref="SecureStorage"/> to Forge's secure-storage abstraction.
/// </summary>
/// <remarks>
/// <para>
/// This exists to satisfy the seam that keeps <c>Forge.Infrastructure</c> free of MAUI. The
/// database encryption key must live in the platform keystore - Android Keystore or the iOS
/// Keychain - and never in preferences, a file, or source. Infrastructure cannot reference
/// MAUI, so the app head supplies this adapter instead.
/// </para>
/// <para>
/// The interface is aliased because MAUI ships its own <c>ISecureStorage</c> in
/// <c>Microsoft.Maui.Storage</c>, and this file necessarily has both in scope.
/// </para>
/// <para>
/// Secure storage genuinely fails on some devices: a keystore corrupted by an OS upgrade, or
/// hardware without a secure element. Those surface as exceptions rather than nulls, and
/// swallowing them would silently mint a fresh encryption key and orphan the user's entire
/// database. The exception is therefore allowed to propagate so startup can report it.
/// </para>
/// </remarks>
internal sealed class MauiSecureStorage : ForgeSecureStorage
{
    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        return await SecureStorage.Default.GetAsync(key).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        await SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false);
    }
}
