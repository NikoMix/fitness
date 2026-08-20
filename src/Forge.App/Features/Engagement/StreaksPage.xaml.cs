using Forge.App.Features.Engagement.ViewModels;

namespace Forge.App.Features.Engagement;

public partial class StreaksPage : ContentPage
{
    public StreaksPage(StreaksPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
