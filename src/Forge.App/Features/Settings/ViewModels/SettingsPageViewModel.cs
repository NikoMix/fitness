using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;

namespace Forge.App.Features.Settings.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly List<SettingsItemViewModel> allItems;

    public SettingsPageViewModel()
    {
        allItems =
        [
            Create("Preferences", "Preferences", "Units, theme, video downloads, haptics, rest timer and calendar week.", "kg lb cm feet inches ml fl oz theme dark light video quality haptics rest timer week", ForgeRoutes.UnitsSettings),
            Create("Preferences", "Notifications", "Workout reminders, quiet hours and local nudges.", "reminders quiet hours notifications", ForgeRoutes.NotificationSettings),
            Create("Preferences", "Language", "The language Forge speaks.", "language english german deutsch sprache locale translation", ForgeRoutes.LanguageSettings),
            Create("Health", "Health connections", "What Forge reads from Health Connect or Apple Health, and what it writes back.", "health connect apple healthkit steps sleep permissions samsung", ForgeRoutes.HealthConnections),
            Create("Privacy", "App lock", "Require your fingerprint, face or passcode before Forge opens.", "lock biometric fingerprint face id passcode privacy security", ForgeRoutes.AppLockSettings),
            Create("Data", "Data management", "Storage use, backup, export and deletion.", "storage backup restore export delete data", ForgeRoutes.DataManagement),
            // Same destination as the row above, on purpose. The diagnostic log lives on the Data
            // management screen because it is user data on the device like everything else there,
            // but nobody looking for it searches "data" - they search "crash", "log" or "problem",
            // usually just after the app has closed on them. A second row costs one line and is
            // the difference between the feature being findable and not.
            Create("Data", "Diagnostic log", "What Forge recorded when something went wrong, and how to send it.", "log logs diagnostic diagnostics crash report problem bug support error closed", ForgeRoutes.DataManagement),
            Create("Shop", "Shop", "Forge Pro, exercise video packs and anything else for sale.", "shop store buy purchase pro premium upgrade video packs", ForgeRoutes.Shop),
            Create("Legal", "Privacy policy", "How Forge protects local health data.", "privacy health data local gdpr", ForgeRoutes.PrivacyPolicy),
            Create("Legal", "Terms of service", "The rules for using Forge.", "terms service conditions", ForgeRoutes.TermsOfService),
            Create("Legal", "Medical disclaimer", "Important safety guidance before training or changing nutrition.", "medical safety pain pregnancy cardiac injury", ForgeRoutes.MedicalDisclaimer),
            Create("Legal", "Licences", "Third-party and open-source acknowledgements.", "licenses licences open source devexpress toolkit ef sqlite", ForgeRoutes.Licences),
        ];

        ApplyFilter();
    }

    public ObservableCollection<SettingsItemViewModel> FilteredItems { get; } = [];

    [ObservableProperty]
    private string searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private static SettingsItemViewModel Create(string group, string title, string description, string keywords, string route)
    {
        return new SettingsItemViewModel(
            group,
            title,
            description,
            keywords,
            new AsyncRelayCommand(() => Microsoft.Maui.Controls.Shell.Current.GoToAsync(route)));
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var items = string.IsNullOrWhiteSpace(query)
            ? allItems
            : allItems.Where(item =>
                item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Group.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase));

        FilteredItems.Clear();
        foreach (var item in items)
        {
            FilteredItems.Add(item);
        }
    }
}
