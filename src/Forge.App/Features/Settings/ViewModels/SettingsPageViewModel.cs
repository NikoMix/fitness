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
            Create("Preferences", "Units", "Mass, length, volume, energy and calendar week.", "kg lb cm feet inches ml fl oz kcal kj week", ForgeRoutes.UnitsSettings),
            Create("Preferences", "Notifications", "Workout reminders, quiet hours and local nudges.", "reminders quiet hours notifications", ForgeRoutes.NotificationSettings),
            Create("Data", "Data management", "Storage use, backup, export and deletion.", "storage backup restore export delete data", ForgeRoutes.DataManagement),
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
