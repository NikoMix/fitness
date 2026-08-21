using CommunityToolkit.Mvvm.ComponentModel;
using Forge.Domain.Commerce;

namespace Forge.App.Features.Shop.ViewModels;

public sealed partial class ShopProductViewModel : ObservableObject
{
    public ShopProductViewModel(
        string productId,
        ProductKind kind,
        string title,
        string description,
        string localizedPrice,
        bool canPurchase,
        string? subscriptionPeriod = null)
    {
        ProductId = productId;
        Kind = kind;
        Title = title;
        Description = description;
        LocalizedPrice = localizedPrice;
        CanPurchase = canPurchase;
        SubscriptionPeriod = subscriptionPeriod;
    }

    public string ProductId { get; }

    public ProductKind Kind { get; }

    public string Title { get; }

    public string Description { get; }

    public string LocalizedPrice { get; }

    public bool CanPurchase { get; }

    public string? SubscriptionPeriod { get; }

    public bool IsSubscription => Kind == ProductKind.Subscription;

    public string PurchaseLabel => CanPurchase ? $"Unlock {Title}" : "Store price unavailable";

    public string KindLabel => IsSubscription ? "Optional subscription" : "One-off purchase";
}
