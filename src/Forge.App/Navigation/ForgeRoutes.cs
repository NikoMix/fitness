namespace Forge.App.Navigation;

/// <summary>
/// Every navigable destination in Forge.
/// </summary>
/// <remarks>
/// <para>
/// Routes are constants rather than inline strings so that a typo becomes a compile error
/// instead of a silent navigation failure discovered on a device, and so that renaming a
/// destination is a single edit.
/// </para>
/// <para>
/// The full v1 route table is declared here up front, including destinations not yet built.
/// That is deliberate: it lets features being developed in parallel link to one another
/// without editing this shared file, which would otherwise be a constant source of conflicts.
/// A feature registers its own route with <c>Routing.RegisterRoute</c> inside its own
/// <c>Add&lt;Name&gt;Feature</c> method.
/// </para>
/// </remarks>
public static class ForgeRoutes
{
    // ---- Primary tab destinations. Declared in AppShell.xaml, not registered dynamically. ----

    /// <summary>Landing surface showing today's plan, rings and next action.</summary>
    public const string Today = "today";

    /// <summary>Training hub: plans, programmes and history.</summary>
    public const string Train = "train";

    /// <summary>Nutrition hub: food log, macros and hydration.</summary>
    public const string Nutrition = "nutrition";

    /// <summary>Progress hub: charts, personal records and trends.</summary>
    public const string Progress = "progress";

    /// <summary>Profile hub: goals, body metrics and settings entry point.</summary>
    public const string Profile = "profile";

    // ---- Onboarding and identity ----

    /// <summary>First-run welcome and value introduction.</summary>
    public const string Welcome = "welcome";

    /// <summary>Goal-setting wizard shown during onboarding.</summary>
    public const string GoalWizard = "goal-wizard";

    /// <summary>Biometric or PIN app-lock screen.</summary>
    public const string AppLock = "app-lock";

    /// <summary>Local profile selection when more than one profile exists on the device.</summary>
    public const string ProfileSwitcher = "profile-switcher";

    // ---- Exercise library ----

    /// <summary>Browsable, searchable exercise catalogue.</summary>
    public const string ExerciseLibrary = "exercises";

    /// <summary>Detail for a single exercise, including form guidance.</summary>
    public const string ExerciseDetail = "exercise-detail";

    /// <summary>Suggested alternatives for an exercise, filtered by available equipment.</summary>
    public const string ExerciseAlternatives = "exercise-alternatives";

    // ---- Planning and execution ----

    /// <summary>Training plan list.</summary>
    public const string PlanList = "plans";

    /// <summary>Plan builder and editor.</summary>
    public const string PlanEditor = "plan-editor";

    /// <summary>Ready-made programme templates for common goals.</summary>
    public const string PlanTemplates = "plan-templates";

    /// <summary>Weekly schedule view for the active plan.</summary>
    public const string PlanSchedule = "plan-schedule";

    /// <summary>
    /// The active workout screen.
    /// </summary>
    /// <remarks>
    /// Presented modally over the shell so the tab bar is hidden. A workout is a focused mode:
    /// exposing navigation mid-set invites accidental exits and lost sessions.
    /// </remarks>
    public const string ActiveWorkout = "active-workout";

    /// <summary>Post-session summary with volume, records and comparison to last time.</summary>
    public const string WorkoutSummary = "workout-summary";

    /// <summary>Full-screen exercise demonstration video.</summary>
    public const string ExerciseVideo = "exercise-video";

    // ---- Insights ----

    /// <summary>Analytics hub: trends, records and training load.</summary>
    public const string Insights = "insights";

    /// <summary>Strength progression over time for one exercise.</summary>
    public const string ExerciseProgress = "exercise-progress";

    /// <summary>Personal record history across every tracked exercise.</summary>
    public const string PersonalRecords = "personal-records";

    /// <summary>Bodyweight and measurement trends, smoothed by a moving average.</summary>
    public const string BodyMetrics = "body-metrics";

    // ---- Coaching and recovery ----

    /// <summary>Adaptive recommendation for the next training session.</summary>
    public const string Coaching = "coaching";

    /// <summary>Readiness score breakdown, showing how each input contributed.</summary>
    public const string Readiness = "readiness";

    /// <summary>Daily subjective check-in that feeds the readiness score.</summary>
    public const string MorningCheckIn = "morning-check-in";

    // ---- Engagement ----

    /// <summary>Achievement and badge cabinet.</summary>
    public const string Achievements = "achievements";

    /// <summary>Streak detail and streak-protection controls.</summary>
    public const string Streaks = "streaks";

    // ---- Backup and portability ----

    /// <summary>Create and restore an encrypted local backup.</summary>
    public const string BackupRestore = "backup-restore";

    /// <summary>Export data to open formats for portability under GDPR Article 20.</summary>
    public const string ExportData = "export-data";

    /// <summary>Import from a competitor export to lower switching cost.</summary>
    public const string ImportData = "import-data";

    // ---- Nutrition and hydration ----

    /// <summary>Food search and logging.</summary>
    public const string FoodLog = "food-log";

    /// <summary>Barcode scanner for packaged foods.</summary>
    public const string BarcodeScanner = "barcode-scanner";

    /// <summary>Recipe list and detail.</summary>
    public const string Recipes = "recipes";

    /// <summary>Hydration logging and history.</summary>
    public const string Hydration = "hydration";

    // ---- Commerce ----

    /// <summary>Shop and paywall.</summary>
    public const string Shop = "shop";

    /// <summary>Restore previously purchased entitlements. Mandatory under Apple 3.1.1.</summary>
    public const string RestorePurchases = "restore-purchases";

    // ---- Settings, legal and compliance ----

    /// <summary>Settings root.</summary>
    public const string Settings = "settings";

    /// <summary>Units, formatting and locale preferences.</summary>
    public const string UnitsSettings = "settings-units";

    /// <summary>Notification preferences and quiet hours.</summary>
    public const string NotificationSettings = "settings-notifications";

    /// <summary>Health platform connection and per-data-type consent.</summary>
    public const string HealthConnections = "settings-health";

    /// <summary>Backup, restore and export.</summary>
    public const string DataManagement = "settings-data";

    /// <summary>Privacy policy, also published at a public URL.</summary>
    public const string PrivacyPolicy = "privacy-policy";

    /// <summary>Terms of service.</summary>
    public const string TermsOfService = "terms-of-service";

    /// <summary>
    /// Medical disclaimer.
    /// </summary>
    /// <remarks>
    /// Forge gives exercise and nutrition guidance, so this is a substantive safety surface
    /// rather than boilerplate.
    /// </remarks>
    public const string MedicalDisclaimer = "medical-disclaimer";

    /// <summary>Third-party and open-source licence attribution.</summary>
    public const string Licences = "licences";

    /// <summary>
    /// Irreversible deletion of all local data.
    /// </summary>
    /// <remarks>
    /// Required by GDPR Article 17 and by Apple's account-deletion policy. Must be reachable
    /// without contacting support.
    /// </remarks>
    public const string DeleteMyData = "delete-my-data";
}
