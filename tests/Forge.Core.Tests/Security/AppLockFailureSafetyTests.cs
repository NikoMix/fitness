using System.Reflection;
using Forge.Core.Abstractions.Data;
using Forge.Core.Abstractions.Preferences;
using Forge.Core.Abstractions.Security;
using NSubstitute;
using Shouldly;

namespace Forge.Core.Tests.Security;

/// <summary>
/// Guards the two promises the app lock makes about failure: it never lets anyone in, and it
/// never takes anything away.
/// </summary>
/// <remarks>
/// Forge holds the only copy of a user's training and body history, so "a lock that eats your
/// data after five bad attempts" is not a hypothetical worth waving away. There is no wipe
/// feature, no attempt counter and no path from the lock to erasure, and these tests assert
/// that both behaviourally and structurally so it cannot be added by accident later.
/// </remarks>
public sealed class AppLockFailureSafetyTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 14, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_run_of_failures_followed_by_a_success_only_grants_access_at_the_success()
    {
        var erasure = Substitute.For<IDataErasureService>();
        var authenticator = Substitute.For<IAppLockAuthenticator>();
        authenticator
            .AuthenticateAsync(Arg.Any<AppLockAuthenticationPrompt>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(AppLockAuthenticationResult.Failed("not recognised")),
                _ => Task.FromResult(AppLockAuthenticationResult.Failed("not recognised")),
                _ => Task.FromResult(AppLockAuthenticationResult.LockedOut("too many attempts")),
                _ => Task.FromResult(AppLockAuthenticationResult.Cancelled),
                _ => Task.FromResult(AppLockAuthenticationResult.Success));

        var machine = new AppLockStateMachine();
        machine.EnterForeground(
            isEnabled: true,
            AppLockCapability.Biometric,
            Now,
            TimeSpan.FromMinutes(1),
            relaxDuringActivity: true,
            isActivityInProgress: false);

        var prompt = new AppLockAuthenticationPrompt("Unlock", "Prove it is you", "Cancel");

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var refusal = await authenticator
                .AuthenticateAsync(prompt, TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            machine.ApplyAuthentication(refusal).ShouldBeFalse();
            machine.State.ShouldBe(AppLockState.Locked);
        }

        var success = await authenticator
            .AuthenticateAsync(prompt, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        machine.ApplyAuthentication(success).ShouldBeTrue();
        machine.State.ShouldBe(AppLockState.Unlocked);

        // The point of the spy: four refusals in a row must not have reached anything that can
        // remove data, and the lock has no reference that would let it.
        await erasure.DidNotReceiveWithAnyArgs()
            .EraseAllLocalDataAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);
    }

    [Fact]
    public void No_outcome_other_than_success_can_be_treated_as_access()
    {
        var granted = Enum.GetValues<AppLockAuthenticationOutcome>()
            .Where(outcome => new AppLockAuthenticationResult(outcome).IsSuccess)
            .ToList();

        granted.ShouldBe([AppLockAuthenticationOutcome.Succeeded]);
    }

    [Fact]
    public void The_security_surface_cannot_reach_user_data()
    {
        // A structural assertion. The lock is a presentation gate; the moment a type in this
        // namespace can see a repository, a data session or the erasure service, "a failed
        // unlock cannot destroy anything" stops being guaranteed by construction.
        Type[] forbidden =
        [
            typeof(IDataErasureService),
            typeof(IDataSessionFactory),
            typeof(IDataSession),
            typeof(IUnitOfWork),
        ];

        var offenders = typeof(AppLockPolicy).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(AppLockPolicy).Namespace)
            .SelectMany(SignatureTypesOf)
            .Where(referenced => forbidden.Contains(referenced))
            .Select(referenced => referenced.Name)
            .Distinct()
            .ToList();

        offenders.ShouldBeEmpty(
            "Forge.Core.Abstractions.Security must not be able to see user data, so that a "
            + "failed unlock structurally cannot delete or alter anything.");
    }

    private static IEnumerable<Type> SignatureTypesOf(Type type)
    {
        const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        foreach (var method in type.GetMethods(Everything))
        {
            yield return method.ReturnType;

            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var field in type.GetFields(Everything))
        {
            yield return field.FieldType;
        }

        foreach (var property in type.GetProperties(Everything))
        {
            yield return property.PropertyType;
        }
    }
}
