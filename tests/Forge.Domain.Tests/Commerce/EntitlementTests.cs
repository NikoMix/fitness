using Forge.Domain.Commerce;
using Shouldly;

namespace Forge.Domain.Tests.Commerce;

public sealed class EntitlementTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Lifetime_entitlement_remains_active_after_grant_time()
    {
        var entitlement = new Entitlement(EntitlementKind.ForgePro, ProductCatalogue.ForgeProLifetimeProductId, Now.AddDays(-10));

        entitlement.IsActive(Now).ShouldBeTrue();
    }

    [Fact]
    public void Entitlement_is_not_active_before_grant_time()
    {
        var entitlement = new Entitlement(EntitlementKind.ForgePro, ProductCatalogue.ForgeProLifetimeProductId, Now.AddDays(1));

        entitlement.IsActive(Now).ShouldBeFalse();
    }

    [Fact]
    public void Expired_entitlement_is_not_active_at_or_after_expiry()
    {
        var entitlement = new Entitlement(
            EntitlementKind.FutureContent,
            ProductCatalogue.FutureContentMonthlyProductId,
            Now.AddDays(-30),
            Now);

        entitlement.IsActive(Now).ShouldBeFalse();
        entitlement.IsActive(Now.AddTicks(1)).ShouldBeFalse();
    }

    [Fact]
    public void Entitlement_is_active_before_expiry()
    {
        var entitlement = new Entitlement(
            EntitlementKind.FutureContent,
            ProductCatalogue.FutureContentMonthlyProductId,
            Now.AddDays(-30),
            Now.AddTicks(1));

        entitlement.IsActive(Now).ShouldBeTrue();
    }
}
