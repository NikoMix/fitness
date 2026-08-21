using Forge.App.Features.Backup.ViewModels;

namespace Forge.App.Features.Backup;

public partial class ImportDataPage : ContentPage
{
    public ImportDataPage(ImportDataViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
