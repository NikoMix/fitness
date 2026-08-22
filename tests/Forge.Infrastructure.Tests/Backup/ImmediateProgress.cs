using Forge.Core.Abstractions.Backup;

namespace Forge.Infrastructure.Tests.Backup;

/// <summary>
/// Reports progress on the calling thread.
/// </summary>
/// <remarks>
/// <see cref="Progress{T}"/> posts to the thread pool, so a handler that cancels runs some time
/// after the operation has moved on. Tests that interrupt an operation at a precise step need the
/// callback to happen inline, or they race the very thing they are trying to observe.
/// </remarks>
/// <param name="handler">Invoked synchronously for every report.</param>
internal sealed class ImmediateProgress(Action<BackupProgress> handler) : IProgress<BackupProgress>
{
    public void Report(BackupProgress value) => handler(value);
}
