#if ANDROID
using Kotlin.Coroutines;

namespace Forge.App.Services.Health;

/// <summary>
/// Bridges a Kotlin <c>suspend</c> function onto a .NET <see cref="Task"/>.
/// </summary>
/// <remarks>
/// <para>
/// Health Connect's entire client API is Kotlin coroutines. Compiled to JVM bytecode, a
/// <c>suspend fun readRecords(request)</c> becomes
/// <c>Object readRecords(request, Continuation&lt;? super ReadRecordsResponse&gt;)</c>, and the
/// binding surfaces exactly that. There is no callback overload and no <c>ListenableFuture</c>
/// overload to fall back on, so calling Health Connect from C# means implementing
/// <c>kotlin.coroutines.Continuation</c>.
/// </para>
/// <para>
/// Two details of the JVM calling convention have to be honoured or the bridge silently misbehaves:
/// </para>
/// <list type="number">
/// <item>
/// The call may complete <i>synchronously</i>. When it does, the method returns the result directly
/// and never touches the continuation. Only when it actually suspends does it return the
/// <c>COROUTINE_SUSPENDED</c> sentinel and resume later. Awaiting the continuation unconditionally
/// would therefore hang on any call that happened to complete inline.
/// </item>
/// <item>
/// <c>resumeWith</c> receives a boxed <c>kotlin.Result</c>: the value itself on success, or a
/// <c>kotlin.Result$Failure</c> wrapper on failure. Treating a failure as a value hands the caller a
/// <see cref="Java.Lang.Object"/> that fails to cast later, far away from the real cause.
/// </item>
/// </list>
/// </remarks>
internal sealed class KotlinTaskContinuation : Java.Lang.Object, IContinuation
{
    private const string CoroutineSuspendedClassName = "kotlin.coroutines.intrinsics.CoroutineSingletons";
    private const string ResultFailureClassName = "kotlin.Result$Failure";

    private readonly TaskCompletionSource<Java.Lang.Object?> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public ICoroutineContext Context => EmptyCoroutineContext.Instance!;

    /// <inheritdoc />
    public void ResumeWith(Java.Lang.Object? result)
    {
        if (TryGetFailure(result, out var failure))
        {
            completion.TrySetException(failure);
            return;
        }

        completion.TrySetResult(result);
    }

    /// <summary>Invokes a Kotlin suspend function and awaits its result.</summary>
    /// <param name="invoke">Calls the suspend function, passing the supplied continuation.</param>
    /// <param name="cancellationToken">Abandons the wait. The Kotlin side keeps running.</param>
    /// <returns>The value the coroutine produced, which may be null.</returns>
    public static async Task<Java.Lang.Object?> InvokeAsync(
        Func<IContinuation, Java.Lang.Object?> invoke,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Deliberately not disposed. Once the call suspends, the Kotlin machinery holds this
        // object and will call back into it; disposing the managed peer first would tear down the
        // Java-callable wrapper underneath a live reference.
        var continuation = new KotlinTaskContinuation();

        var immediate = invoke(continuation);
        if (immediate is not null && !IsCoroutineSuspended(immediate))
        {
            return TryGetFailure(immediate, out var failure) ? throw failure : immediate;
        }

        await using var registration = cancellationToken.Register(
            static state => ((KotlinTaskContinuation)state!).completion.TrySetCanceled(),
            continuation).ConfigureAwait(false);

        return await continuation.completion.Task.ConfigureAwait(false);
    }

    private static bool IsCoroutineSuspended(Java.Lang.Object value) =>
        string.Equals(value.Class?.Name, CoroutineSuspendedClassName, StringComparison.Ordinal);

    private static bool TryGetFailure(Java.Lang.Object? value, out Exception failure)
    {
        failure = null!;

        if (value is null || !string.Equals(value.Class?.Name, ResultFailureClassName, StringComparison.Ordinal))
        {
            return false;
        }

        // Result$Failure.toString() renders as "Failure(java.lang.SecurityException: ...)", which
        // names the Java exception class and its message. Reaching through to the wrapped Throwable
        // by reflection would be tidier in principle, but a Throwable handle cannot be marshalled
        // back as a Java.Lang.Object - Throwable maps onto System.Exception - so the attempt throws
        // an InvalidCastException and loses the diagnostic it was trying to improve.
        failure = new InvalidOperationException($"A Health Connect call failed: {value}");
        return true;
    }
}
#endif
