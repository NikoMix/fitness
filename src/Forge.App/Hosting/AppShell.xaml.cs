namespace Forge.App.Hosting;

/// <summary>The application shell hosting the five primary destinations.</summary>
public partial class AppShell : Microsoft.Maui.Controls.Shell
{
    /// <summary>Initialises the shell.</summary>
    public AppShell(AppShellViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
