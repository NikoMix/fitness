using CommunityToolkit.Mvvm.ComponentModel;

namespace Forge.App.Hosting;

/// <summary>
/// Backs the application shell.
/// </summary>
/// <remarks>
/// Deliberately free of DevExpress and of any feature dependency. The shell is the one screen
/// every wave touches, so keeping it thin is what stops it becoming a permanent merge-conflict
/// hotspot as features land in parallel.
/// </remarks>
public sealed partial class AppShellViewModel : ObservableObject
{
    /// <summary>Index of the selected primary destination.</summary>
    /// <remarks>Persisted on suspend so a process kill returns the user where they were.</remarks>
    [ObservableProperty]
    private int selectedTabIndex;
}
