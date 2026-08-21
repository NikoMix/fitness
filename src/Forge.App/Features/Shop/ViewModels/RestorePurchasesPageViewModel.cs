using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Core.Abstractions.Billing;
using Forge.Domain.Commerce;

namespace Forge.App.Features.Shop.ViewModels;

public sealed partial class RestorePurchasesPageViewModel(IBillingService billingService) : ObservableObject
{
    public ObservableCollection<string> RestoredItems { get; } = [];

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasRestoredItems;

    [ObservableProperty]
    private string statusMessage = "Use this if you reinstalled Forge or changed devices with the same store account.";

    [RelayCommand]
    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var result = await billingService.RestorePurchasesAsync(cancellationToken);
            RestoredItems.Clear();

            foreach (var entitlement in result.Entitlements)
            {
                RestoredItems.Add(Describe(entitlement));
            }

            HasRestoredItems = RestoredItems.Count > 0;
            StatusMessage = result.Status switch
            {
                BillingResultStatus.Succeeded => result.Message ?? "Purchases restored.",
                BillingResultStatus.StoreUnavailable => "The store is unavailable. Check your connection and store account.",
                BillingResultStatus.RestoreFailed => "The store could not restore purchases right now.",
                _ => result.Message ?? "Restore could not be completed."
            };
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Describe(Entitlement entitlement)
    {
        var product = ProductCatalogue.Find(entitlement.ProductId);
        var name = product?.DisplayName ?? entitlement.ProductId;
        return entitlement.ExpiresAtUtc.HasValue
            ? $"{name} — active until {entitlement.ExpiresAtUtc.Value.LocalDateTime:d}"
            : $"{name} — active";
    }
}
