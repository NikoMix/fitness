namespace Forge.Domain.Commerce;

/// <summary>
/// Store product identifiers and the entitlement each product grants.
/// </summary>
public static class ProductCatalogue
{
    public const string ForgeProLifetimeProductId = "forge.pro.lifetime";
    public const string FutureContentMonthlyProductId = "forge.content.monthly";

    private static readonly ProductDefinition[] Products =
    [
        new(
            ForgeProLifetimeProductId,
            ProductKind.NonConsumable,
            EntitlementKind.ForgePro,
            "Forge Pro",
            "One-off unlock for advanced planning, analysis and personalisation."),
        new(
            FutureContentMonthlyProductId,
            ProductKind.Subscription,
            EntitlementKind.FutureContent,
            "Future content subscription",
            "Optional support for future paid template packs and additions.")
    ];

    public static IReadOnlyList<ProductDefinition> All => Products;

    public static ProductDefinition? Find(string productId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);

        return Products.FirstOrDefault(product => product.ProductId.Equals(productId, StringComparison.Ordinal));
    }
}

public sealed record ProductDefinition(
    string ProductId,
    ProductKind Kind,
    EntitlementKind EntitlementKind,
    string DisplayName,
    string ValueSummary);

public enum ProductKind
{
    NonConsumable,
    Subscription
}
