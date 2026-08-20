using Forge.App.Features.Coaching.ViewModels;

namespace Forge.App.Features.Coaching;

public partial class MorningCheckInPage : ContentPage
{
    public MorningCheckInPage(MorningCheckInViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
