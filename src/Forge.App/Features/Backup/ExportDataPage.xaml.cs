using Forge.App.Features.Backup.ViewModels;

namespace Forge.App.Features.Backup;

public partial class ExportDataPage : ContentPage
{
    public ExportDataPage(ExportDataViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
