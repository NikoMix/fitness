namespace Forge.Core.Abstractions;

/// <summary>
/// An error whose message was written to be read by the person using Forge.
/// </summary>
/// <remarks>
/// <para>
/// Screens catch broadly, because a failure to load must still leave something on screen that says
/// what happened. The tempting way to write that is to interpolate <c>ex.Message</c>, and it has
/// shipped twice: once as
/// <c>"Forge could not open your workout: SQLite does not support expressions of type
/// 'DateTimeOffset' in ORDER BY clauses"</c>, and once as a whole LINQ expression with a Microsoft
/// support URL on the screen shown immediately after a completed workout.
/// </para>
/// <para>
/// Neither told the user anything they could act on, and both made a working app look broken in a
/// way that invites a one-star review rather than a bug report.
/// </para>
/// <para>
/// Banning the pattern outright is not right either, because some failures genuinely do have
/// something worth saying - "no profile is active, so this workout was not started" is useful.
/// This type is how a screen tells the two apart: if the exception is one of these, show its
/// message; otherwise log the exception and show a fixed sentence.
/// </para>
/// </remarks>
public sealed class ForgeUserFacingException : Exception
{
    /// <summary>Creates an error whose message is safe to show to a user.</summary>
    /// <param name="message">Plain language, no type names, no stack detail, no URLs.</param>
    public ForgeUserFacingException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an error whose message is safe to show to a user.</summary>
    /// <param name="message">Plain language, no type names, no stack detail, no URLs.</param>
    /// <param name="innerException">The underlying failure, for the log rather than the screen.</param>
    public ForgeUserFacingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The text to show for <paramref name="exception"/>, or <paramref name="fallback"/> when it
    /// has nothing a user should read.
    /// </summary>
    /// <param name="exception">The caught exception.</param>
    /// <param name="fallback">A complete sentence describing what failed, in the app's own words.</param>
    public static string DescribeFor(Exception exception, string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);

        return exception is ForgeUserFacingException ? exception.Message : fallback;
    }
}
