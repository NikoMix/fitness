using Forge.App.Navigation;
using Forge.Core.Abstractions;

namespace Forge.App.Features.Scanning.Services;

/// <summary>
/// The scanner page's side of a pending scan.
/// </summary>
/// <remarks>
/// Separate from <see cref="IBarcodeScanCoordinator"/> so that a caller asking for a scan cannot
/// accidentally complete one, and the scanner cannot accidentally start one. Only the barcode
/// scanner screen should depend on this.
/// </remarks>
public interface IBarcodeScanSession
{
    /// <summary>Completes the pending scan, if there is one.</summary>
    /// <param name="result">The result to hand back to the caller.</param>
    void Complete(BarcodeScanResult result);
}

/// <summary>
/// Bridges one-way Shell navigation into an awaitable scan.
/// </summary>
/// <remarks>
/// A singleton holding at most one pending scan. Concurrent scans are not a real scenario - there
/// is one camera and one screen - so a second request supersedes the first by cancelling it rather
/// than queueing, which keeps the earlier caller from waiting on a page that is no longer theirs.
/// </remarks>
internal sealed class BarcodeScanCoordinator(INavigationService navigation) : IBarcodeScanCoordinator, IBarcodeScanSession
{
    private readonly Lock gate = new();
    private TaskCompletionSource<BarcodeScanResult>? pending;

    /// <inheritdoc />
    public async Task<BarcodeScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        // Continuations run asynchronously so that completing a scan from the page never runs the
        // caller's follow-up work inline on the UI thread mid-navigation.
        var completion = new TaskCompletionSource<BarcodeScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        TaskCompletionSource<BarcodeScanResult>? superseded;
        lock (gate)
        {
            superseded = pending;
            pending = completion;
        }

        superseded?.TrySetResult(BarcodeScanResult.Cancelled);

        try
        {
            await navigation.GoToAsync(ForgeRoutes.BarcodeScanner, cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
        }
        catch
        {
            // The page never opened, so nothing will ever complete this scan.
            Forget(completion);
            throw;
        }

        using var registration = cancellationToken.Register(static state =>
        {
            var (coordinator, source, token) = ((BarcodeScanCoordinator, TaskCompletionSource<BarcodeScanResult>, CancellationToken))state!;
            if (source.TrySetCanceled(token))
            {
                coordinator.Forget(source);
            }
        }, (this, completion, cancellationToken));

        return await completion.Task.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Complete(BarcodeScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        TaskCompletionSource<BarcodeScanResult>? completion;
        lock (gate)
        {
            completion = pending;
            pending = null;
        }

        completion?.TrySetResult(result);
    }

    private void Forget(TaskCompletionSource<BarcodeScanResult> completion)
    {
        lock (gate)
        {
            if (ReferenceEquals(pending, completion))
            {
                pending = null;
            }
        }
    }
}
