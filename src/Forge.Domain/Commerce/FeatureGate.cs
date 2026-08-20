namespace Forge.Domain.Commerce;

/// <summary>
/// Decides which capabilities remain free and which require an active entitlement.
/// </summary>
public static class FeatureGate
{
    public static FeatureAccess Evaluate(
        ForgeFeature feature,
        IEnumerable<Entitlement> entitlements,
        DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(entitlements);

        return feature switch
        {
            ForgeFeature.BasicWorkoutLogging
                or ForgeFeature.BasicExerciseLibrary
                or ForgeFeature.BasicNutritionLogging
                or ForgeFeature.LocalBackupExport
                or ForgeFeature.HealthPlatformImport => FeatureAccess.AllowedFree,

            ForgeFeature.AdvancedTrainingAnalytics
                or ForgeFeature.CustomPlanTemplates
                or ForgeFeature.PersonalRecordDeepDive
                or ForgeFeature.ExtraPersonalisation => HasActive(entitlements, EntitlementKind.ForgePro, atUtc)
                    ? FeatureAccess.AllowedPaid
                    : FeatureAccess.RequiresForgePro,

            ForgeFeature.FutureTemplatePacks => HasActive(entitlements, EntitlementKind.FutureContent, atUtc)
                || HasActive(entitlements, EntitlementKind.ForgePro, atUtc)
                    ? FeatureAccess.AllowedPaid
                    : FeatureAccess.RequiresFutureContent,

            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown Forge feature.")
        };
    }

    public static bool IsAllowed(ForgeFeature feature, IEnumerable<Entitlement> entitlements, DateTimeOffset atUtc)
    {
        var access = Evaluate(feature, entitlements, atUtc);
        return access is FeatureAccess.AllowedFree or FeatureAccess.AllowedPaid;
    }

    private static bool HasActive(IEnumerable<Entitlement> entitlements, EntitlementKind kind, DateTimeOffset atUtc)
    {
        return entitlements.Any(entitlement => entitlement.Kind == kind && entitlement.IsActive(atUtc));
    }
}

public enum ForgeFeature
{
    BasicWorkoutLogging,
    BasicExerciseLibrary,
    BasicNutritionLogging,
    LocalBackupExport,
    HealthPlatformImport,
    AdvancedTrainingAnalytics,
    CustomPlanTemplates,
    PersonalRecordDeepDive,
    ExtraPersonalisation,
    FutureTemplatePacks
}

public enum FeatureAccess
{
    AllowedFree,
    AllowedPaid,
    RequiresForgePro,
    RequiresFutureContent
}
