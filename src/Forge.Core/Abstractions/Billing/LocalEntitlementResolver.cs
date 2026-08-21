using Forge.Domain.Commerce;

namespace Forge.Core.Abstractions.Billing;

/// <summary>
/// Applies successful store transactions to Forge's local entitlement store.
/// </summary>
/// <remarks>
/// Forge v1 intentionally has no backend. This resolver only records purchases that the platform
/// store already reported as purchased/restored. It never treats errors, cancellation or pending
/// approval as a grant.
/// </remarks>
public sealed class LocalEntitlementResolver(IEntitlementStore entitlementStore, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Applies a store purchase outcome, granting only when the store reported success.
    /// </summary>
    /// <param name="product">The catalogue product being purchased.</param>
    /// <param name="status">The terminal outcome returned by the store layer.</param>
    /// <param name="grantedAtUtc">The store transaction time, or <see langword="null" /> to use the device clock.</param>
    /// <param name="message">Optional user-visible result detail.</param>
    /// <param name="cancellationToken">Cancels storage access.</param>
    /// <returns>A purchase result whose entitlement is set only for successful grants.</returns>
    public Task<PurchaseResult> ApplyPurchaseOutcomeAsync(
        ProductDefinition product,
        BillingResultStatus status,
        DateTimeOffset? grantedAtUtc,
        string? message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (status == BillingResultStatus.Succeeded)
        {
            return GrantPurchaseAsync(product, grantedAtUtc, cancellationToken);
        }

        return Task.FromResult(new PurchaseResult(status, Message: message));
    }

    /// <summary>
    /// Grants the entitlement for a completed purchase unless the same active product already exists.
    /// </summary>
    /// <param name="product">The catalogue product that the store completed.</param>
    /// <param name="grantedAtUtc">The store transaction time, or <see langword="null" /> to use the device clock.</param>
    /// <param name="cancellationToken">Cancels storage access.</param>
    /// <returns>The purchase result to show in the app.</returns>
    public async Task<PurchaseResult> GrantPurchaseAsync(
        ProductDefinition product,
        DateTimeOffset? grantedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);

        var existing = await entitlementStore.GetEntitlementsAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Any(entitlement => entitlement.ProductId == product.ProductId && entitlement.IsActive(clock.GetUtcNow())))
        {
            return new PurchaseResult(BillingResultStatus.AlreadyOwned, Message: "This purchase is already active on this device.");
        }

        var entitlement = CreateEntitlement(product, grantedAtUtc);
        await entitlementStore.SaveEntitlementsAsync(Merge(existing, [entitlement]), cancellationToken).ConfigureAwait(false);

        return new PurchaseResult(BillingResultStatus.Succeeded, entitlement, "Purchase complete.");
    }

    /// <summary>
    /// Merges restored store purchases into the local entitlement store.
    /// </summary>
    /// <param name="purchases">The purchases returned by the platform restore API.</param>
    /// <param name="cancellationToken">Cancels storage access.</param>
    /// <returns>The restored entitlements. Empty success means the store account had no Forge purchases.</returns>
    public async Task<RestorePurchasesResult> RestorePurchasesAsync(
        IEnumerable<StorePurchaseGrant> purchases,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(purchases);

        var restored = purchases
            .SelectMany(ToEntitlements)
            .ToArray();

        if (restored.Length > 0)
        {
            var existing = await entitlementStore.GetEntitlementsAsync(cancellationToken).ConfigureAwait(false);
            await entitlementStore.SaveEntitlementsAsync(Merge(existing, restored), cancellationToken).ConfigureAwait(false);
        }

        return new RestorePurchasesResult(
            BillingResultStatus.Succeeded,
            restored,
            restored.Length == 0 ? "No previous purchases were found for this store account." : "Purchases restored.");
    }

    /// <summary>
    /// Combines entitlement sets, keeping the newest grant per product and entitlement kind.
    /// </summary>
    /// <param name="existing">Previously stored entitlements.</param>
    /// <param name="incoming">New entitlements from successful store transactions.</param>
    /// <returns>The merged entitlement set.</returns>
    public static Entitlement[] Merge(IEnumerable<Entitlement> existing, IEnumerable<Entitlement> incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        return existing
            .Concat(incoming)
            .GroupBy(entitlement => $"{entitlement.Kind}:{entitlement.ProductId}", StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(entitlement => entitlement.ExpiresAtUtc ?? DateTimeOffset.MaxValue)
                .ThenByDescending(entitlement => entitlement.GrantedAtUtc)
                .First())
            .OrderBy(entitlement => entitlement.Kind)
            .ThenBy(entitlement => entitlement.ProductId, StringComparer.Ordinal)
            .ToArray();
    }

    private Entitlement CreateEntitlement(ProductDefinition product, DateTimeOffset? grantedAtUtc)
    {
        return new Entitlement(
            product.EntitlementKind,
            product.ProductId,
            grantedAtUtc ?? clock.GetUtcNow(),
            ExpiresAtUtc: null);
    }

    private IEnumerable<Entitlement> ToEntitlements(StorePurchaseGrant purchase)
    {
        foreach (var productId in purchase.ProductIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            var definition = ProductCatalogue.Find(productId);
            if (definition is not null)
            {
                yield return CreateEntitlement(definition, purchase.GrantedAtUtc);
            }
        }
    }
}
