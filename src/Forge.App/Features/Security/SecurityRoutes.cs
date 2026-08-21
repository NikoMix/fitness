namespace Forge.App.Features.Security;

/// <summary>
/// Routes owned by the Security feature that are not yet in <c>ForgeRoutes</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>ForgeRoutes</c> declares the whole v1 route table up front precisely so parallel branches
/// never have to edit it, and <c>ForgeRoutes.AppLock</c> is already there. The app lock also
/// needs a settings destination, which is not, and adding one would mean touching the shared
/// file this convention exists to protect.
/// </para>
/// <para>
/// Declaring it here keeps the merge surface at zero. Fold the constant into <c>ForgeRoutes</c>
/// alongside <c>AppLock</c> whenever that file is next open, and delete this type.
/// </para>
/// </remarks>
public static class SecurityRoutes
{
    /// <summary>Where the app lock is turned on, tuned, or turned off.</summary>
    public const string AppLockSettings = "settings-app-lock";
}
