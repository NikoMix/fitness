using Forge.Domain.Commerce;

namespace Forge.Core.Abstractions.Billing;

public sealed record BillingProduct(
    string ProductId,
    ProductKind Kind,
    string Title,
    string Description,
    string LocalizedPrice,
    string? SubscriptionPeriod = null);

public sealed record BillingProductsResult(
    BillingResultStatus Status,
    IReadOnlyList<BillingProduct> Products,
    string? Message = null)
{
    public bool IsSuccess => Status == BillingResultStatus.Succeeded;
}

public sealed record PurchaseResult(
    BillingResultStatus Status,
    Entitlement? Entitlement = null,
    string? Message = null)
{
    public bool IsSuccess => Status == BillingResultStatus.Succeeded;
}

public sealed record RestorePurchasesResult(
    BillingResultStatus Status,
    IReadOnlyList<Entitlement> Entitlements,
    string? Message = null)
{
    public bool IsSuccess => Status == BillingResultStatus.Succeeded;
}

/// <summary>
/// A successful store-owned transaction that can grant one or more local entitlements.
/// </summary>
/// <param name="ProductIds">Store product identifiers included in the transaction.</param>
/// <param name="GrantedAtUtc">Store transaction time, or <see langword="null" /> to use the device clock.</param>
public sealed record StorePurchaseGrant(
    IReadOnlyList<string> ProductIds,
    DateTimeOffset? GrantedAtUtc = null);

public enum BillingResultStatus
{
    Succeeded,
    UserCancelled,
    Pending,
    AlreadyOwned,
    PaymentDeclined,
    StoreUnavailable,
    ProductUnavailable,
    RestoreFailed,
    Failed
}
