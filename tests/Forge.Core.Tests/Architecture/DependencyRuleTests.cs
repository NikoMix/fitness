using System.Reflection;
using Forge.Core.Abstractions;
using Shouldly;

namespace Forge.Core.Tests.Architecture;

/// <summary>
/// Guards the dependency rule at the assembly level.
/// </summary>
/// <remarks>
/// <para>
/// <c>src/Directory.Build.targets</c> already fails the build if a MAUI or DevExpress package
/// is referenced from an inner layer. That check inspects project files, so it catches the
/// direct case. This test inspects the compiled assembly instead, which also catches a
/// reference arriving transitively through another project.
/// </para>
/// <para>
/// The rule matters because it is what keeps the product logic testable without an emulator,
/// and what allows the Windows and Mac Catalyst heads to be added in v1.1 without a rewrite.
/// See docs/adr/0002-platform-scope.md.
/// </para>
/// </remarks>
public sealed class DependencyRuleTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.Maui",
        "DevExpress",
        "Microsoft.UI",
        "Xamarin"
    ];

    [Fact]
    public void Forge_Core_references_no_UI_framework()
    {
        var offenders = ForbiddenReferencesOf(typeof(INavigationService).Assembly);

        offenders.ShouldBeEmpty(
            "Forge.Core must stay free of UI frameworks so it remains unit-testable and " +
            "portable to the desktop heads planned for v1.1.");
    }

    [Fact]
    public void Forge_Domain_references_no_UI_framework()
    {
        var domain = typeof(Domain.Training.Exercise).Assembly;

        var offenders = ForbiddenReferencesOf(domain);

        offenders.ShouldBeEmpty("Forge.Domain must depend on nothing beyond the base class library.");
    }

    [Fact]
    public void Navigation_abstraction_exposes_no_framework_types()
    {
        // One leaked framework type in a signature is enough to drag the whole UI stack into
        // the inner layers, so the public surface is asserted rather than assumed.
        var namespaces = typeof(INavigationService)
            .GetMethods()
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .Select(t => t.Namespace ?? string.Empty)
            .Distinct();

        foreach (var ns in namespaces)
        {
            ForbiddenPrefixes.Any(ns.StartsWith).ShouldBeFalse($"'{ns}' leaked into INavigationService.");
        }
    }

    private static List<string> ForbiddenReferencesOf(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => ForbiddenPrefixes.Any(name.StartsWith))];
}
