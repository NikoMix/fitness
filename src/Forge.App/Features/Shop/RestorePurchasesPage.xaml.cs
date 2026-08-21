using Forge.App.Features.Shop.ViewModels;

namespace Forge.App.Features.Shop;

public partial class RestorePurchasesPage : ContentPage
{
    public RestorePurchasesPage(RestorePurchasesPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
