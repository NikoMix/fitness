namespace Forge.Core.Abstractions;

/// <summary>
/// Navigation as seen by a view model.
/// </summary>
/// <remarks>
/// Declared in <c>Forge.Core</c> rather than in the MAUI head so that view models can be
/// exercised in plain unit tests with a substitute, without instantiating any MAUI type and
/// without an emulator. Nothing in this interface may expose a MAUI or DevExpress type.
/// </remarks>
public interface INavigationService
{
    /// <summary>Navigates to a registered route.</summary>
    /// <param name="route">A route constant. Passing an unregistered route throws.</param>
    /// <param name="parameter">Optional typed argument handed to the destination.</param>
    /// <param name="cancellationToken">Cancels the navigation.</param>
    Task GoToAsync(string route, object? parameter = null, CancellationToken cancellationToken = default);

    /// <summary>Returns to the previous destination.</summary>
    /// <param name="cancellationToken">Cancels the navigation.</param>
    Task GoBackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Presents a route modally over the current shell, used for full-screen flows such as
    /// an active workout that must not expose the tab bar.
    /// </summary>
    /// <param name="route">A route constant.</param>
    /// <param name="parameter">Optional typed argument handed to the destination.</param>
    /// <param name="cancellationToken">Cancels the navigation.</param>
    Task ShowModalAsync(string route, object? parameter = null, CancellationToken cancellationToken = default);
}
