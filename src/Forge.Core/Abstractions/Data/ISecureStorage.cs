namespace Forge.Core.Abstractions.Data;

/// <summary>
/// Minimal secure-storage seam supplied by the platform head.
/// </summary>
/// <remarks>
/// Forge.Infrastructure deliberately cannot reference MAUI. The Android/iOS head adapts
/// Microsoft.Maui.Storage.SecureStorage to this interface so database keys are kept in platform
/// secure storage without leaking MAUI into Core or Infrastructure.
/// </remarks>
public interface ISecureStorage
{
    /// <summary>Reads a value from platform secure storage.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Writes a value to platform secure storage.</summary>
    Task SetAsync(string key, string value, CancellationToken cancellationToken);
}
