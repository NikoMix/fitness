using Forge.Core.Abstractions.Data;
using Forge.Infrastructure.Persistence;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

public sealed class DatabaseKeyProviderTests
{
    [Fact]
    public async Task Key_is_generated_once_and_reused_from_secure_storage()
    {
        var storage = new InMemorySecureStorage();
        var provider = new SecureStorageDatabaseKeyProvider(storage);

        var first = await provider.GetOrCreateKeyAsync(TestContext.Current.CancellationToken);
        var second = await provider.GetOrCreateKeyAsync(TestContext.Current.CancellationToken);

        first.ShouldNotBeNullOrWhiteSpace();
        Convert.FromBase64String(first).Length.ShouldBe(32);
        second.ShouldBe(first);
        storage.StoredValue.ShouldBe(first);
    }

    private sealed class InMemorySecureStorage : ISecureStorage
    {
        public string? StoredValue { get; private set; }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(StoredValue);
        }

        public Task SetAsync(string key, string value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoredValue = value;
            return Task.CompletedTask;
        }
    }
}
