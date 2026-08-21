namespace Forge.App.Features.Legal;

public static class LegalContent
{
    public static IReadOnlyList<LegalSection> PrivacyPolicy { get; } =
    [
        new("Local-first privacy",
            "Forge is designed without a backend. Your workouts, body metrics, nutrition logs, goals, preferences and health-platform imports are stored on your device. Forge does not operate a cloud account server or cloud database for v1."),
        new("Health data stays on device",
            "Health and fitness data can reveal sensitive information about your body and daily routine. Forge does not sell it, does not use it for advertising and does not send it to Forge servers. If you export a backup or share diagnostics, you decide where that file goes."),
        new("Storage and encryption",
            "Forge stores app data in an encrypted local SQLite database. The database encryption key is held in platform secure storage. Preferences such as units and notification choices are stored locally on the device."),
        new("Permissions",
            "When Forge requests Health Connect, HealthKit, notification, camera or file access, the permission is used only for the feature you chose. You can revoke platform permissions in system settings."),
        new("Deletion",
            "Delete my data is available in-app without contacting support. It is designed to erase the local database, encryption key, cached media, preferences and temporary export files. Because Forge has no cloud backup, deletion is irreversible unless you exported your own backup first."),
        new("Purchases",
            "If you buy Forge Pro, Apple or Google processes the payment. Forge stores only a local entitlement receipt on this device so paid features can remain unlocked without a Forge account server."),
        new("Contact",
            "Before store publication, the public privacy policy URL must include the current legal contact address for NikoMix and must match this in-app policy."),
    ];

    public static IReadOnlyList<LegalSection> TermsOfService { get; } =
    [
        new("Using Forge",
            "Forge is a local-first fitness app for personal training, nutrition tracking and progress review. You are responsible for the information you enter and for keeping your device and backups secure."),
        new("No account recovery",
            "Forge v1 has no Forge-operated account system or cloud database. If you delete the app, erase your data or lose your device without an exported backup, Forge cannot recover your local data."),
        new("Purchases",
            "Forge Pro is planned as a one-off, non-consumable app-store purchase. The store shows the final local price before purchase, handles taxes and payment, and provides the account used by Restore purchases."),
        new("Entitlements",
            "Forge stores purchase entitlements locally. Secure storage and signing make casual edits evident, but without a Forge backend a determined device owner may still tamper with local state. Forge does not promise server-grade DRM."),
        new("Acceptable use",
            "Do not use Forge in a way that violates law, infringes rights, attempts to bypass app-store purchase systems or endangers another person."),
        new("Changes",
            "These terms may be updated with app releases. Continued use after an update means you accept the updated terms."),
    ];

    public static IReadOnlyList<LegalSection> MedicalDisclaimer { get; } =
    [
        new("Not medical advice",
            "Forge provides exercise, nutrition and habit guidance for general fitness. It is not medical advice, diagnosis, treatment or a substitute for a qualified healthcare professional."),
        new("Before starting",
            "Consult a physician, physiotherapist, dietitian or other qualified professional before starting a new programme, increasing training intensity, changing diet materially or using Forge after a period of inactivity."),
        new("Pain or warning symptoms",
            "Stop exercising immediately if you experience chest pain, faintness, severe shortness of breath, unusual heart rhythm, sharp joint pain, sudden weakness, dizziness or any symptom that feels unsafe. Seek urgent care when symptoms are severe or persistent."),
        new("Pregnancy",
            "If you are pregnant, recently gave birth or trying to conceive, get professional guidance before following exercise or nutrition recommendations. Training intensity, hydration, calorie targets and movement selection may need individual adjustment."),
        new("Cardiac conditions",
            "If you have a heart condition, high blood pressure, a history of cardiac events, implanted cardiac devices or have been advised to limit exertion, use Forge only with professional clearance and follow your clinician's limits over any app suggestion."),
        new("Injury and rehabilitation",
            "If you are injured, recovering from surgery or managing chronic pain, avoid movements that aggravate symptoms and follow your clinician or therapist's rehabilitation plan. Forge cannot assess tissue healing or diagnose safe loading."),
    ];

    public static IReadOnlyList<LegalSection> Licences { get; } =
    [
        new("DevExpress .NET MAUI",
            "Forge uses DevExpress MAUI controls for mobile UI surfaces. See DevExpress licence terms for the applicable package version."),
        new("CommunityToolkit",
            "Forge uses CommunityToolkit.Mvvm and CommunityToolkit.Maui components under their open-source licence terms."),
        new("Entity Framework Core",
            "Forge uses Microsoft Entity Framework Core for local persistence access under Microsoft's open-source licence terms."),
        new("SQLite",
            "Forge stores local data in SQLite, with encryption supplied by the configured SQLCipher bundle in the persistence layer."),
        new("Attribution",
            "Before store submission, verify exact package names, versions and licence texts from the dependency lock files and include any required notices."),
    ];
}
