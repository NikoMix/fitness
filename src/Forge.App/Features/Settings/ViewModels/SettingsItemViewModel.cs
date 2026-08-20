using System.Windows.Input;

namespace Forge.App.Features.Settings.ViewModels;

public sealed record SettingsItemViewModel(
    string Group,
    string Title,
    string Description,
    string Keywords,
    ICommand NavigateCommand);
