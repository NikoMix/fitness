using Forge.App.Features.Backup.ViewModels;

namespace Forge.App.Features.Backup;

public partial class BackupRestorePage : ContentPage
{
    private readonly BackupRestoreViewModel viewModel;

    public BackupRestorePage(BackupRestoreViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (viewModel.LoadBackupsCommand.CanExecute(null))
        {
            viewModel.LoadBackupsCommand.Execute(null);
        }
    }
}
