using Forge.Core.Abstractions;

namespace Forge.App.Navigation;

/// <summary>
/// <see cref="INavigationService"/> implemented over MAUI Shell.
/// </summary>
/// <remarks>
/// This type is the only place in the application that knows navigation is implemented with
/// Shell. Keeping that knowledge here is what allows view models to remain testable and what
/// would allow the navigation stack to be replaced without touching feature code.
/// </remarks>
internal sealed class ShellNavigationService : INavigationService
{
    /// <summary>
    /// Key under which a typed navigation argument travels through Shell's loosely typed
    /// parameter dictionary. Callers stay typed; only this class deals with the dictionary.
    /// </summary>
    internal const string ParameterKey = "forge.parameter";

    /// <inheritdoc />
    public Task GoToAsync(string route, object? parameter = null, CancellationToken cancellationToken = default)
        => NavigateAsync(route, parameter, modal: false, cancellationToken);

    /// <inheritdoc />
    public Task ShowModalAsync(string route, object? parameter = null, CancellationToken cancellationToken = default)
        => NavigateAsync(route, parameter, modal: true, cancellationToken);

    /// <inheritdoc />
    public Task GoBackAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RequireShell().GoToAsync("..");
    }

    private static Task NavigateAsync(string route, object? parameter, bool modal, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        cancellationToken.ThrowIfCancellationRequested();

        // A leading slash pushes the destination outside the tab hierarchy, which is what
        // gives a modal flow such as an active workout the full screen with no tab bar.
        var target = modal ? $"//{route}" : route;

        return parameter is null
            ? RequireShell().GoToAsync(target)
            : RequireShell().GoToAsync(target, new Dictionary<string, object> { [ParameterKey] = parameter });
    }

    private static Microsoft.Maui.Controls.Shell RequireShell()
        => Microsoft.Maui.Controls.Shell.Current
           ?? throw new InvalidOperationException(
               "Navigation was requested before the application shell existed. Resolve " +
               "INavigationService lazily rather than navigating from a constructor.");
}
