using Forge.Domain.Commerce;

namespace Forge.Core.Abstractions.Billing;

public interface IBillingService
{
    Task<BillingProductsResult> GetProductsAsync(CancellationToken cancellationToken);

    Task<PurchaseResult> PurchaseAsync(string productId, CancellationToken cancellationToken);

    Task<RestorePurchasesResult> RestorePurchasesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Entitlement>> GetEntitlementsAsync(CancellationToken cancellationToken);
}
