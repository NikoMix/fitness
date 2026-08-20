using Forge.Domain.Commerce;
using Shouldly;

namespace Forge.Domain.Tests.Commerce;

public sealed class FeatureGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ForgeFeature.BasicWorkoutLogging)]
    [InlineData(ForgeFeature.BasicExerciseLibrary)]
    [InlineData(ForgeFeature.BasicNutritionLogging)]
    [InlineData(ForgeFeature.LocalBackupExport)]
    [InlineData(ForgeFeature.HealthPlatformImport)]
    public void Core_training_capabilities_remain_free(ForgeFeature feature)
    {
        FeatureGate.Evaluate(feature, [], Now).ShouldBe(FeatureAccess.AllowedFree);
    }

    [Theory]
    [InlineData(ForgeFeature.AdvancedTrainingAnalytics)]
    [InlineData(ForgeFeature.CustomPlanTemplates)]
    [InlineData(ForgeFeature.PersonalRecordDeepDive)]
    [InlineData(ForgeFeature.ExtraPersonalisation)]
    public void Additive_pro_capabilities_require_forge_pro(ForgeFeature feature)
    {
        FeatureGate.Evaluate(feature, [], Now).ShouldBe(FeatureAccess.RequiresForgePro);
    }

    [Fact]
    public void Active_forge_pro_unlocks_additive_pro_capabilities()
    {
        var entitlements = new[]
        {
            new Entitlement(EntitlementKind.ForgePro, ProductCatalogue.ForgeProLifetimeProductId, Now.AddDays(-1))
        };

        FeatureGate.Evaluate(ForgeFeature.AdvancedTrainingAnalytics, entitlements, Now)
            .ShouldBe(FeatureAccess.AllowedPaid);
    }

    [Fact]
    public void Future_content_requires_a_matching_subscription_or_pro_unlock()
    {
        FeatureGate.Evaluate(ForgeFeature.FutureTemplatePacks, [], Now)
            .ShouldBe(FeatureAccess.RequiresFutureContent);

        var entitlements = new[]
        {
            new Entitlement(EntitlementKind.FutureContent, ProductCatalogue.FutureContentMonthlyProductId, Now.AddDays(-1), Now.AddDays(1))
        };

        FeatureGate.Evaluate(ForgeFeature.FutureTemplatePacks, entitlements, Now)
            .ShouldBe(FeatureAccess.AllowedPaid);
    }
}
