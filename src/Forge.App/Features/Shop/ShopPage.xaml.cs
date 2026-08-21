using Forge.App.Features.Shop.ViewModels;

namespace Forge.App.Features.Shop;

public partial class ShopPage : ContentPage
{
    public ShopPage(ShopPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ((ShopPageViewModel)BindingContext).LoadCommand.Execute(null);
    }
}
