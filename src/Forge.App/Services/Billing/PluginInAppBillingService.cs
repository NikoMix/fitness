using Forge.Core.Abstractions.Billing;
using Forge.Domain.Commerce;
using Plugin.InAppBilling;

namespace Forge.App.Services.Billing;

public sealed class PluginInAppBillingService(IEntitlementStore entitlementStore) : IBillingService
{
    private static readonly ProductDefinition[] ActiveProducts =
    [
        ProductCatalogue.Find(ProductCatalogue.ForgeProLifetimeProductId)
            ?? throw new InvalidOperationException("Forge Pro lifetime product is missing from the catalogue.")
    ];

    private readonly LocalEntitlementResolver entitlementResolver = new(entitlementStore);

    public async Task<BillingProductsResult> GetProductsAsync(CancellationToken cancellationToken)
    {
        if (!CrossInAppBilling.IsSupported)
        {
            return new BillingProductsResult(BillingResultStatus.StoreUnavailable, [], "In-app purchases are not supported on this device.");
        }

        try
        {
            var billing = CrossInAppBilling.Current;
            if (!await EnsureConnectedAsync(billing, cancellationToken))
            {
                return new BillingProductsResult(BillingResultStatus.StoreUnavailable, [], "The store is unavailable.");
            }

            var products = new List<BillingProduct>();
            foreach (var group in ActiveProducts.GroupBy(product => product.Kind))
            {
                var storeProducts = await billing.GetProductInfoAsync(ToItemType(group.Key), group.Select(product => product.ProductId).ToArray(), cancellationToken);
                products.AddRange(storeProducts.Select(ToBillingProduct));
            }

            return new BillingProductsResult(BillingResultStatus.Succeeded, products);
        }
        catch (InAppBillingPurchaseException ex)
        {
            return new BillingProductsResult(MapPurchaseError(ex.PurchaseError), [], ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new BillingProductsResult(BillingResultStatus.StoreUnavailable, [], ex.Message);
        }
    }

    public async Task<PurchaseResult> PurchaseAsync(string productId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);

        var definition = ProductCatalogue.Find(productId);
        if (definition is null || !ActiveProducts.Any(product => product.ProductId == productId))
        {
            return new PurchaseResult(BillingResultStatus.ProductUnavailable, Message: "This product is not part of the Forge catalogue.");
        }

        IReadOnlyList<Entitlement> existingEntitlements;
        try
        {
            existingEntitlements = await entitlementStore.GetEntitlementsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new PurchaseResult(BillingResultStatus.Failed, Message: ex.Message);
        }

        var now = DateTimeOffset.UtcNow;
        if (existingEntitlements.Any(entitlement => entitlement.ProductId == productId && entitlement.IsActive(now)))
        {
            return new PurchaseResult(BillingResultStatus.AlreadyOwned, Message: "This purchase is already active on this device.");
        }

        if (!CrossInAppBilling.IsSupported)
        {
            return new PurchaseResult(BillingResultStatus.StoreUnavailable, Message: "In-app purchases are not supported on this device.");
        }

        try
        {
            var billing = CrossInAppBilling.Current;
            if (!await EnsureConnectedAsync(billing, cancellationToken))
            {
                return new PurchaseResult(BillingResultStatus.StoreUnavailable, Message: "The store is unavailable.");
            }

            var purchase = await billing.PurchaseAsync(productId, ToItemType(definition.Kind), string.Empty, string.Empty, string.Empty, cancellationToken);
            return await HandlePurchaseAsync(purchase, existingEntitlements, definition, cancellationToken);
        }
        catch (InAppBillingPurchaseException ex)
        {
            var status = MapPurchaseError(ex.PurchaseError);
            var message = status == BillingResultStatus.AlreadyOwned
                ? "The store says Forge Pro is already owned. Use Restore purchases to refresh this device."
                : ex.Message;
            return new PurchaseResult(status, Message: message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new PurchaseResult(BillingResultStatus.StoreUnavailable, Message: ex.Message);
        }
    }

    public async Task<RestorePurchasesResult> RestorePurchasesAsync(CancellationToken cancellationToken)
    {
        if (!CrossInAppBilling.IsSupported)
        {
            return new RestorePurchasesResult(BillingResultStatus.StoreUnavailable, [], "In-app purchases are not supported on this device.");
        }

        try
        {
            var billing = CrossInAppBilling.Current;
            if (!await EnsureConnectedAsync(billing, cancellationToken))
            {
                return new RestorePurchasesResult(BillingResultStatus.StoreUnavailable, [], "The store is unavailable.");
            }

            var restoredPurchases = new List<StorePurchaseGrant>();
            foreach (var group in ActiveProducts.GroupBy(product => product.Kind))
            {
                var purchases = await billing.GetPurchasesAsync(ToItemType(group.Key), cancellationToken);
                restoredPurchases.AddRange(purchases
                    .Where(IsSuccessfulPurchase)
                    .Select(ToStorePurchaseGrant));
            }

            return await entitlementResolver.RestorePurchasesAsync(restoredPurchases, cancellationToken);
        }
        catch (InAppBillingPurchaseException ex)
        {
            return new RestorePurchasesResult(MapPurchaseError(ex.PurchaseError), [], ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new RestorePurchasesResult(BillingResultStatus.StoreUnavailable, [], ex.Message);
        }
    }

    public Task<IReadOnlyList<Entitlement>> GetEntitlementsAsync(CancellationToken cancellationToken)
    {
        return entitlementStore.GetEntitlementsAsync(cancellationToken);
    }

    private static async Task<bool> EnsureConnectedAsync(IInAppBilling billing, CancellationToken cancellationToken)
    {
        if (!billing.IsConnected && !await billing.ConnectAsync(false, cancellationToken))
        {
            return false;
        }

        return billing.CanMakePayments;
    }

    private async Task<PurchaseResult> HandlePurchaseAsync(
        InAppBillingPurchase? purchase,
        IReadOnlyList<Entitlement> existingEntitlements,
        ProductDefinition definition,
        CancellationToken cancellationToken)
    {
        if (purchase is null)
        {
            return new PurchaseResult(BillingResultStatus.Failed, Message: "The store did not return a purchase.");
        }

        return purchase.State switch
        {
            PurchaseState.Purchased or PurchaseState.Restored => await PersistSuccessfulPurchaseAsync(purchase, existingEntitlements, definition, cancellationToken),
            PurchaseState.Deferred or PurchaseState.PaymentPending or PurchaseState.Purchasing => await entitlementResolver.ApplyPurchaseOutcomeAsync(definition, BillingResultStatus.Pending, null, "The purchase is pending store approval.", cancellationToken),
            PurchaseState.Canceled => await entitlementResolver.ApplyPurchaseOutcomeAsync(definition, BillingResultStatus.UserCancelled, null, "The purchase was cancelled.", cancellationToken),
            PurchaseState.Failed => await entitlementResolver.ApplyPurchaseOutcomeAsync(definition, BillingResultStatus.PaymentDeclined, null, "The payment could not be completed.", cancellationToken),
            _ => await entitlementResolver.ApplyPurchaseOutcomeAsync(definition, BillingResultStatus.Failed, null, "The store returned an unknown purchase state.", cancellationToken)
        };
    }

    private async Task<PurchaseResult> PersistSuccessfulPurchaseAsync(
        InAppBillingPurchase purchase,
        IReadOnlyList<Entitlement> existingEntitlements,
        ProductDefinition definition,
        CancellationToken cancellationToken)
    {
        if (existingEntitlements.Any(entitlement => entitlement.ProductId == definition.ProductId && entitlement.IsActive(DateTimeOffset.UtcNow)))
        {
            return new PurchaseResult(BillingResultStatus.AlreadyOwned, Message: "This purchase is already active on this device.");
        }

        var result = await entitlementResolver.ApplyPurchaseOutcomeAsync(definition, BillingResultStatus.Succeeded, TransactionDate(purchase), "Purchase complete.", cancellationToken);
        await TryFinalizePurchaseAsync(purchase, cancellationToken);

        return result;
    }

    private static async Task TryFinalizePurchaseAsync(InAppBillingPurchase purchase, CancellationToken cancellationToken)
    {
        var identifier = !string.IsNullOrWhiteSpace(purchase.TransactionIdentifier)
            ? purchase.TransactionIdentifier
            : purchase.PurchaseToken;

        if (string.IsNullOrWhiteSpace(identifier))
        {
            return;
        }

        try
        {
            await CrossInAppBilling.Current.FinalizePurchaseAsync([identifier], cancellationToken);
        }
        catch (InAppBillingPurchaseException)
        {
            // Entitlement is already granted locally. Finalization failures are retried by the
            // store/plugin on later calls and should not make a completed purchase look failed.
        }
    }

    private static BillingProduct ToBillingProduct(InAppBillingProduct product)
    {
        var definition = ProductCatalogue.Find(product.ProductId);
        var kind = definition?.Kind ?? ProductKind.NonConsumable;

        return new BillingProduct(
            product.ProductId,
            kind,
            string.IsNullOrWhiteSpace(product.Name) ? definition?.DisplayName ?? product.ProductId : product.Name,
            string.IsNullOrWhiteSpace(product.Description) ? definition?.ValueSummary ?? string.Empty : product.Description,
            product.LocalizedPrice,
            kind == ProductKind.Subscription ? FormatSubscriptionPeriod(product) : null);
    }

    private static string? FormatSubscriptionPeriod(InAppBillingProduct product)
    {
        var applePeriod = product.AppleExtras?.SubscriptionPeriod;
        if (applePeriod is not null && applePeriod.Unit != SubscriptionPeriodUnit.Unknown)
        {
            return $"{applePeriod.NumberOfUnits} {applePeriod.Unit.ToString().ToLowerInvariant()}";
        }

        var androidPhase = product.AndroidExtras?.SubscriptionOfferDetails
            .SelectMany(offer => offer.PricingPhases)
            .FirstOrDefault(phase => !string.IsNullOrWhiteSpace(phase.BillingPeriod));

        return androidPhase?.BillingPeriod;
    }

    private static StorePurchaseGrant ToStorePurchaseGrant(InAppBillingPurchase purchase)
    {
        var productIds = purchase.ProductIds.Count > 0 ? purchase.ProductIds : [purchase.ProductId];
        return new StorePurchaseGrant(productIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray(), TransactionDate(purchase));
    }

    private static DateTimeOffset? TransactionDate(InAppBillingPurchase purchase)
    {
        return purchase.TransactionDateUtc == default
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(purchase.TransactionDateUtc, DateTimeKind.Utc));
    }

    private static bool IsSuccessfulPurchase(InAppBillingPurchase purchase)
    {
        return purchase.State is PurchaseState.Purchased or PurchaseState.Restored;
    }

    private static ItemType ToItemType(ProductKind kind)
    {
        return kind == ProductKind.Subscription ? ItemType.Subscription : ItemType.InAppPurchase;
    }

    private static BillingResultStatus MapPurchaseError(PurchaseError error)
    {
        return error switch
        {
            PurchaseError.UserCancelled => BillingResultStatus.UserCancelled,
            PurchaseError.AlreadyOwned => BillingResultStatus.AlreadyOwned,
            PurchaseError.PaymentInvalid or PurchaseError.PaymentNotAllowed => BillingResultStatus.PaymentDeclined,
            PurchaseError.ItemUnavailable or PurchaseError.InvalidProduct or PurchaseError.ProductRequestFailed => BillingResultStatus.ProductUnavailable,
            PurchaseError.RestoreFailed => BillingResultStatus.RestoreFailed,
            PurchaseError.BillingUnavailable
                or PurchaseError.AppStoreUnavailable
                or PurchaseError.ServiceUnavailable
                or PurchaseError.ServiceDisconnected
                or PurchaseError.ServiceTimeout
                or PurchaseError.NetworkError
                or PurchaseError.FeatureNotSupported => BillingResultStatus.StoreUnavailable,
            _ => BillingResultStatus.Failed
        };
    }
}
