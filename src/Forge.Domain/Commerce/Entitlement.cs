namespace Forge.Domain.Commerce;

/// <summary>
/// A locally held right to use an additive paid capability.
/// </summary>
public sealed record Entitlement(
    EntitlementKind Kind,
    string ProductId,
    DateTimeOffset GrantedAtUtc,
    DateTimeOffset? ExpiresAtUtc = null)
{
    public bool IsActive(DateTimeOffset atUtc)
    {
        return GrantedAtUtc <= atUtc && (!ExpiresAtUtc.HasValue || ExpiresAtUtc.Value > atUtc);
    }
}

public enum EntitlementKind
{
    ForgePro,
    FutureContent
}
