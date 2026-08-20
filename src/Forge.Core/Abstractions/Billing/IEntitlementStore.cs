using Forge.Domain.Commerce;

namespace Forge.Core.Abstractions.Billing;

public interface IEntitlementStore
{
    Task<IReadOnlyList<Entitlement>> GetEntitlementsAsync(CancellationToken cancellationToken);

    Task SaveEntitlementsAsync(IReadOnlyList<Entitlement> entitlements, CancellationToken cancellationToken);
}
