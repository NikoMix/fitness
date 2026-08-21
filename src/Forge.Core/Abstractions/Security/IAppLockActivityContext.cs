namespace Forge.Core.Abstractions.Security;

/// <summary>
/// Tells the app lock when the user is in the middle of something a lock screen would ruin.
/// </summary>
/// <remarks>
/// <para>
/// A workout is the case this exists for. The phone goes on a bench, the screen turns off
/// between sets, the user answers a message, changes a track, or picks it up with chalk on
/// their hands. Every one of those backgrounds the app. A lock that fires on the way back is
/// not a minor annoyance: fingerprints fail on sweat, face unlock fails at the angle you hold a
/// phone from under a bar, and the cost lands mid-set when the user has the least attention to
/// spare and a rest timer running.
/// </para>
/// <para>
/// This is a seam rather than a dependency on the Workout feature, so the lock has no
/// knowledge of training and the training code has no knowledge of security.
/// </para>
/// </remarks>
public interface IAppLockActivityContext
{
    /// <summary>Whether something the user would hate to be interrupted is running right now.</summary>
    bool IsActivityInProgress { get; }

    /// <summary>
    /// Marks the start of an uninterruptible activity. Dispose the returned value to end it.
    /// </summary>
    /// <returns>A scope that ends the activity when disposed.</returns>
    /// <remarks>
    /// Scopes nest and are counted, so an inner rest timer inside an outer workout does not end
    /// the workout when it finishes.
    /// </remarks>
    IDisposable BeginActivity();
}

/// <summary>Counts nested uninterruptible activities.</summary>
/// <remarks>
/// The counter is guarded because a workout is started from the UI thread while sensor and
/// timer code can end scopes from a background thread.
/// </remarks>
public sealed class AppLockActivityContext : IAppLockActivityContext
{
    private readonly Lock gate = new();
    private int depth;

    /// <inheritdoc />
    public bool IsActivityInProgress
    {
        get
        {
            lock (gate)
            {
                return depth > 0;
            }
        }
    }

    /// <inheritdoc />
    public IDisposable BeginActivity()
    {
        lock (gate)
        {
            depth++;
        }

        return new ActivityScope(this);
    }

    private void End()
    {
        lock (gate)
        {
            // Clamped rather than allowed to go negative. A double dispose is a caller bug, but
            // letting the count drop below zero would leave the lock permanently relaxed, which
            // is the failure that actually costs the user something.
            depth = Math.Max(0, depth - 1);
        }
    }

    private sealed class ActivityScope(AppLockActivityContext owner) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            owner.End();
        }
    }
}
