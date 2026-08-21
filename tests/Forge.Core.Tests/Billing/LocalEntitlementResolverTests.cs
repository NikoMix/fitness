using Forge.Core.Abstractions.Billing;
using Forge.Domain.Commerce;
using Shouldly;

namespace Forge.Core.Tests.Billing;

public sealed class LocalEntitlementResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GrantPurchaseAsync_stores_active_forge_pro_entitlement()
    {
        var store = new MemoryEntitlementStore();
        var resolver = new LocalEntitlementResolver(store, new FixedTimeProvider(Now));

        var result = await resolver.GrantPurchaseAsync(ForgePro(), Now, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(BillingResultStatus.Succeeded);
        result.Entitlement.ShouldNotBeNull();
        result.Entitlement.Kind.ShouldBe(EntitlementKind.ForgePro);
        result.Entitlement.ProductId.ShouldBe(ProductCatalogue.ForgeProLifetimeProductId);
        store.Entitlements.ShouldContain(entitlement => entitlement.IsActive(Now));
    }

    [Fact]
    public async Task GrantPurchaseAsync_returns_already_owned_without_duplicate_entitlement()
    {
        var store = new MemoryEntitlementStore();
        var resolver = new LocalEntitlementResolver(store, new FixedTimeProvider(Now));
        await resolver.GrantPurchaseAsync(ForgePro(), Now.AddMinutes(-1), TestContext.Current.CancellationToken);

        var result = await resolver.GrantPurchaseAsync(ForgePro(), Now, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(BillingResultStatus.AlreadyOwned);
        store.Entitlements.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RestorePurchasesAsync_merges_restored_entitlements()
    {
        var store = new MemoryEntitlementStore();
        var resolver = new LocalEntitlementResolver(store, new FixedTimeProvider(Now));

        var result = await resolver.RestorePurchasesAsync(
            [new StorePurchaseGrant([ProductCatalogue.ForgeProLifetimeProductId], Now.AddDays(-1))],
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(BillingResultStatus.Succeeded);
        result.Entitlements.Count.ShouldBe(1);
        store.Entitlements.Single().ProductId.ShouldBe(ProductCatalogue.ForgeProLifetimeProductId);
    }

    [Fact]
    public async Task RestorePurchasesAsync_succeeds_without_grant_when_store_account_has_no_purchases()
    {
        var store = new MemoryEntitlementStore();
        var resolver = new LocalEntitlementResolver(store, new FixedTimeProvider(Now));

        var result = await resolver.RestorePurchasesAsync([], TestContext.Current.CancellationToken);

        result.Status.ShouldBe(BillingResultStatus.Succeeded);
        result.Entitlements.ShouldBeEmpty();
        store.Entitlements.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BillingResultStatus.UserCancelled)]
    [InlineData(BillingResultStatus.Pending)]
    [InlineData(BillingResultStatus.PaymentDeclined)]
    [InlineData(BillingResultStatus.StoreUnavailable)]
    [InlineData(BillingResultStatus.ProductUnavailable)]
    [InlineData(BillingResultStatus.Failed)]
    public async Task ApplyPurchaseOutcomeAsync_never_grants_entitlement_for_failure_statuses(BillingResultStatus status)
    {
        var store = new MemoryEntitlementStore();
        var resolver = new LocalEntitlementResolver(store, new FixedTimeProvider(Now));

        var result = await resolver.ApplyPurchaseOutcomeAsync(
            ForgePro(),
            status,
            Now,
            "Not purchased.",
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(status);
        result.Entitlement.ShouldBeNull();
        store.Entitlements.ShouldBeEmpty();
    }

    private static ProductDefinition ForgePro()
        => ProductCatalogue.Find(ProductCatalogue.ForgeProLifetimeProductId)
            ?? throw new InvalidOperationException("Forge Pro product missing.");

    private sealed class MemoryEntitlementStore : IEntitlementStore
    {
        public List<Entitlement> Entitlements { get; private set; } = [];

        public Task<IReadOnlyList<Entitlement>> GetEntitlementsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Entitlement>>(Entitlements);
        }

        public Task SaveEntitlementsAsync(IReadOnlyList<Entitlement> entitlements, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entitlements = [.. entitlements];
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
