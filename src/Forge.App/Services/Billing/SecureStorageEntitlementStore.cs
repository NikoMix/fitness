using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Forge.Core.Abstractions.Billing;
using Forge.Domain.Commerce;
using Microsoft.Maui.Storage;

namespace Forge.App.Services.Billing;

/// <summary>
/// Stores entitlements locally with a device-held signature. This is tamper-resistant against
/// casual preference editing, not server-grade DRM: a determined attacker controlling the device
/// can still patch app code or local storage.
/// </summary>
public sealed class SecureStorageEntitlementStore : IEntitlementStore
{
    private const string EnvelopeKey = "forge.billing.entitlements.v1";
    private const string HmacKey = "forge.billing.entitlements.hmac.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<Entitlement>> GetEntitlementsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var envelopeJson = await SecureStorage.Default.GetAsync(EnvelopeKey);
        if (string.IsNullOrWhiteSpace(envelopeJson))
        {
            return [];
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<StoredEntitlementEnvelope>(envelopeJson, JsonOptions);
            if (envelope is null || !await IsValidAsync(envelope, cancellationToken))
            {
                return [];
            }

            return envelope.Entitlements;
        }
        catch (JsonException)
        {
            return [];
        }
        catch (CryptographicException)
        {
            return [];
        }
        catch (FormatException)
        {
            return [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }

    public async Task SaveEntitlementsAsync(IReadOnlyList<Entitlement> entitlements, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entitlements);
        cancellationToken.ThrowIfCancellationRequested();

        var uniqueEntitlements = entitlements
            .GroupBy(entitlement => $"{entitlement.Kind}:{entitlement.ProductId}", StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(entitlement => entitlement.ExpiresAtUtc ?? DateTimeOffset.MaxValue)
                .ThenByDescending(entitlement => entitlement.GrantedAtUtc)
                .First())
            .OrderBy(entitlement => entitlement.Kind)
            .ThenBy(entitlement => entitlement.ProductId, StringComparer.Ordinal)
            .ToArray();

        var signature = await SignAsync(uniqueEntitlements, cancellationToken);
        var envelope = new StoredEntitlementEnvelope(uniqueEntitlements, signature);
        var envelopeJson = JsonSerializer.Serialize(envelope, JsonOptions);

        await SecureStorage.Default.SetAsync(EnvelopeKey, envelopeJson);
    }

    private static async Task<bool> IsValidAsync(StoredEntitlementEnvelope envelope, CancellationToken cancellationToken)
    {
        var expected = await SignAsync(envelope.Entitlements, cancellationToken);
        var actualBytes = Convert.FromBase64String(envelope.Signature);
        var expectedBytes = Convert.FromBase64String(expected);

        return actualBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private static async Task<string> SignAsync(IReadOnlyList<Entitlement> entitlements, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = await GetOrCreateSigningKeyAsync();
        var payload = JsonSerializer.Serialize(entitlements, JsonOptions);
        using var hmac = new HMACSHA256(Convert.FromBase64String(key));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));

        return Convert.ToBase64String(signature);
    }

    private static async Task<string> GetOrCreateSigningKeyAsync()
    {
        var key = await SecureStorage.Default.GetAsync(HmacKey);
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        var bytes = RandomNumberGenerator.GetBytes(32);
        key = Convert.ToBase64String(bytes);
        await SecureStorage.Default.SetAsync(HmacKey, key);

        return key;
    }

    private sealed record StoredEntitlementEnvelope(
        Entitlement[] Entitlements,
        string Signature);
}
