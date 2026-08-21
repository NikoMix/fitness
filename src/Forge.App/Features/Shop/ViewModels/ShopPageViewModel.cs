using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;
using Forge.Core.Abstractions.Billing;
using Forge.Domain.Commerce;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;

namespace Forge.App.Features.Shop.ViewModels;

public sealed partial class ShopPageViewModel(IBillingService billingService) : ObservableObject
{
    private bool hasLoaded;

    public ObservableCollection<ShopProductViewModel> Products { get; } = [];

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasProducts;

    [ObservableProperty]
    private bool hasActivePro;

    [ObservableProperty]
    private bool canManageSubscription;

    [ObservableProperty]
    private string statusMessage = "Store prices load from Apple or Google for your region.";

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (hasLoaded && Products.Count > 0)
        {
            return;
        }

        await RefreshAsync(cancellationToken);
        hasLoaded = true;
    }

    [RelayCommand]
    private static Task RestoreAsync()
    {
        return Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.RestorePurchases);
    }

    [RelayCommand(CanExecute = nameof(CanPurchase))]
    private async Task PurchaseAsync(ShopProductViewModel product, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);

        IsBusy = true;
        try
        {
            var result = await billingService.PurchaseAsync(product.ProductId, cancellationToken);
            StatusMessage = result.Status switch
            {
                BillingResultStatus.Succeeded => "Purchase complete. Forge Pro features are now unlocked on this device.",
                BillingResultStatus.Pending => "Purchase pending. Forge will unlock it after the store approves it.",
                BillingResultStatus.AlreadyOwned => "This purchase is already active.",
                BillingResultStatus.UserCancelled => "Purchase cancelled.",
                BillingResultStatus.PaymentDeclined => "The payment was declined or could not be completed.",
                BillingResultStatus.StoreUnavailable => "The store is unavailable. Check your connection and store account.",
                BillingResultStatus.ProductUnavailable => "This product is not available from the store right now.",
                _ => result.Message ?? "The purchase could not be completed."
            };

            await RefreshEntitlementsAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool CanPurchase(ShopProductViewModel? product) => product?.CanPurchase == true;

    [RelayCommand]
    private async Task ManageSubscriptionsAsync()
    {
        if (!CanManageSubscription)
        {
            return;
        }

        var uri = DeviceInfo.Platform == DevicePlatform.Android
            ? new Uri($"https://play.google.com/store/account/subscriptions?sku={ProductCatalogue.FutureContentMonthlyProductId}&package={AppInfo.PackageName}")
            : new Uri("https://apps.apple.com/account/subscriptions");

        await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            await RefreshEntitlementsAsync(cancellationToken);

            var result = await billingService.GetProductsAsync(cancellationToken);
            Products.Clear();

            if (result.IsSuccess)
            {
                foreach (var product in result.Products.OrderBy(product => product.Kind))
                {
                    Products.Add(new ShopProductViewModel(
                        product.ProductId,
                        product.Kind,
                        product.Title,
                        product.Description,
                        string.IsNullOrWhiteSpace(product.LocalizedPrice) ? "Store price unavailable" : product.LocalizedPrice,
                        !string.IsNullOrWhiteSpace(product.LocalizedPrice),
                        product.SubscriptionPeriod));
                }

                StatusMessage = "Prices are shown exactly as returned by the store for your region.";
            }
            else
            {
                AddUnavailableCatalogue();
                StatusMessage = result.Message ?? "The store is unavailable. You can still use the free training loop.";
            }

            HasProducts = Products.Count > 0;
            CanManageSubscription = Products.Any(product => product.IsSubscription);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshEntitlementsAsync(CancellationToken cancellationToken)
    {
        var entitlements = await billingService.GetEntitlementsAsync(cancellationToken);
        HasActivePro = entitlements.Any(entitlement => entitlement.Kind == EntitlementKind.ForgePro && entitlement.IsActive(DateTimeOffset.UtcNow));
    }

    private void AddUnavailableCatalogue()
    {
        Products.Clear();
        foreach (var product in ProductCatalogue.All)
        {
            Products.Add(new ShopProductViewModel(
                product.ProductId,
                product.Kind,
                product.DisplayName,
                product.ValueSummary,
                "Store price unavailable",
                canPurchase: false));
        }
    }
}
