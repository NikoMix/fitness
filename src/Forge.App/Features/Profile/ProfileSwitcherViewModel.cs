using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;
using Forge.Domain.Profile;

namespace Forge.App.Features.Profile;

/// <summary>
/// Choosing which of several local profiles this device is currently being used by.
/// </summary>
/// <remarks>
/// <para>
/// The screen states what a switch actually does. Forge stores profiles locally with no account
/// behind them, and at the time this screen was built only the profile record and its body
/// measurements were separated: training history, plans, food and hydration are still shared by
/// every profile on the device. A switcher that hid that would be actively harmful, because a user
/// would reasonably read "switch" as "this is now my data" and then train against, or log into,
/// somebody else's record.
/// </para>
/// <para>
/// The wording is not hard-coded. <see cref="ProfileDataAreas"/> derives each claim from whether
/// the underlying entity implements <see cref="IProfileOwned"/>, so as features adopt the seam this
/// screen becomes less apologetic on its own and can never overstate what it separates.
/// </para>
/// </remarks>
public sealed partial class ProfileSwitcherViewModel : ObservableObject
{
    private readonly ProfileStore? profileStore;

    /// <summary>Initialises an instance with no persistence, used by the XAML designer.</summary>
    public ProfileSwitcherViewModel()
    {
    }

    /// <summary>Initialises the view model.</summary>
    /// <param name="profileStore">Reads and writes the local profiles.</param>
    /// <exception cref="ArgumentNullException"><paramref name="profileStore"/> is <see langword="null"/>.</exception>
    public ProfileSwitcherViewModel(ProfileStore profileStore)
    {
        this.profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
    }

    /// <summary>Every profile on this device, in a stable display order.</summary>
    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = [];

    /// <summary>Kinds of data a switch genuinely keeps apart.</summary>
    public ObservableCollection<ProfileDataAreaViewModel> SeparatedAreas { get; } = [];

    /// <summary>Kinds of data every profile on this device still shares.</summary>
    public ObservableCollection<ProfileDataAreaViewModel> SharedAreas { get; } = [];

    /// <summary>One sentence on what switching profile does and does not change.</summary>
    [ObservableProperty]
    private string separationSummary = ProfileDataAreas.SummariseSeparation();

    /// <summary>Whether some data is still shared between profiles.</summary>
    [ObservableProperty]
    private bool hasSharedData = !ProfileDataAreas.IsFullySeparated;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private bool hasProfiles;

    /// <summary>Whether another profile may be added to this device.</summary>
    [ObservableProperty]
    private bool canAddProfile = true;

    /// <summary>The name typed into the add-profile field.</summary>
    [ObservableProperty]
    private string newProfileName = string.Empty;

    /// <summary>Why the typed name was refused, empty when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewProfileProblem))]
    private string newProfileProblem = string.Empty;

    /// <summary>Whether the add-profile field has a problem to show.</summary>
    public bool HasNewProfileProblem => !string.IsNullOrEmpty(NewProfileProblem);

    /// <summary>What just happened, shown after a switch, rename or delete.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string status = string.Empty;

    /// <summary>Whether there is a status message to show.</summary>
    public bool HasStatus => !string.IsNullOrEmpty(Status);

    /// <summary>How many profiles this device may hold.</summary>
    public static int MaximumProfiles => ActiveProfileSelector.MaximumProfiles;

    /// <summary>Reads every profile and rebuilds the screen.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes once the screen has been rebuilt.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        BuildDataAreas();

        if (profileStore is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var roster = await profileStore.LoadRosterAsync(cancellationToken).ConfigureAwait(true);

            Profiles.Clear();
            foreach (var profile in roster.Profiles)
            {
                Profiles.Add(new ProfileRowViewModel(
                    this,
                    profile,
                    isActive: profile.Id == roster.ActiveProfileId,
                    canDelete: roster.Profiles.Count > 1,
                    ownedRecordCount: roster.OwnedRecordCounts.TryGetValue(profile.Id, out var count) ? count : 0));
            }

            HasProfiles = Profiles.Count > 0;
            IsEmpty = Profiles.Count == 0;
            CanAddProfile = roster.CanAddProfile;
        }
        catch (InvalidOperationException)
        {
            // Startup has not finished resolving the encrypted database. Showing a stale roster
            // would be worse than showing none: the user could tap a profile that is not there.
            Profiles.Clear();
            HasProfiles = false;
            IsEmpty = true;
            Status = "Local storage is still starting. Try again in a moment.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Makes a profile the active one.</summary>
    /// <param name="row">The profile row that was tapped.</param>
    /// <returns>A task that completes once the switch has been applied.</returns>
    internal async Task SwitchToAsync(ProfileRowViewModel row)
    {
        if (profileStore is null || row.IsActive)
        {
            return;
        }

        if (await profileStore.SwitchToAsync(row.ProfileId, CancellationToken.None).ConfigureAwait(true))
        {
            Status = ProfileDataAreas.IsFullySeparated
                ? $"Switched to \"{row.Name}\"."
                : $"Switched to \"{row.Name}\". Shared data listed below is unchanged and still belongs to everyone on this device.";
        }

        await LoadAsync(CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>Applies a pending rename.</summary>
    /// <param name="row">The row being renamed.</param>
    /// <returns>A task that completes once the rename has been attempted.</returns>
    internal async Task ConfirmRenameAsync(ProfileRowViewModel row)
    {
        if (profileStore is null)
        {
            return;
        }

        var result = await profileStore.RenameProfileAsync(row.ProfileId, row.RenameText, CancellationToken.None).ConfigureAwait(true);
        if (!result.IsAccepted)
        {
            row.RenameProblem = result.Problem;
            return;
        }

        Status = $"Renamed to \"{result.Name}\".";
        await LoadAsync(CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>Confirms and performs a delete, stating exactly what it removes first.</summary>
    /// <param name="row">The profile the user asked to delete.</param>
    /// <returns>A task that completes once the delete has been attempted.</returns>
    internal async Task DeleteAsync(ProfileRowViewModel row)
    {
        if (profileStore is null)
        {
            return;
        }

        var shell = Shell.Current;
        var plan = await profileStore.PrepareDeletionAsync(row.ProfileId, CancellationToken.None).ConfigureAwait(true);

        if (plan is null)
        {
            await LoadAsync(CancellationToken.None).ConfigureAwait(true);
            return;
        }

        if (!plan.IsPermitted)
        {
            if (shell is not null)
            {
                await shell.DisplayAlertAsync("Cannot delete this profile", plan.Refusal, "OK").ConfigureAwait(true);
            }

            return;
        }

        // The plan is shown before anything is written, and it lists what survives as well as what
        // goes. Somebody deleting a profile for privacy reasons has to know that data Forge cannot
        // attribute to them stays on the device.
        var confirmed = shell is not null
            && await shell.DisplayAlertAsync(plan.Headline, plan.Describe(), "Delete profile", "Cancel").ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        if (await profileStore.DeleteProfileAsync(row.ProfileId, CancellationToken.None).ConfigureAwait(true))
        {
            Status = plan.RemovedRecordCount == 0
                ? $"Deleted \"{plan.ProfileName}\"."
                : $"Deleted \"{plan.ProfileName}\" and {plan.RemovedRecordCount} of its records.";
        }

        await LoadAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task AddProfileAsync() => AddAsync(ProfileKind.Personal);

    [RelayCommand]
    private Task AddGuestProfileAsync() => AddAsync(ProfileKind.Guest);

    [RelayCommand]
    private static Task FinishSetupAsync() => Shell.Current.GoToAsync(ForgeRoutes.GoalWizard);

    private async Task AddAsync(ProfileKind kind)
    {
        if (profileStore is null)
        {
            return;
        }

        var result = await profileStore.CreateProfileAsync(NewProfileName, kind, CancellationToken.None).ConfigureAwait(true);
        if (!result.IsAccepted)
        {
            NewProfileProblem = result.Problem;
            return;
        }

        NewProfileProblem = string.Empty;
        NewProfileName = string.Empty;
        Status = kind == ProfileKind.Guest
            ? $"Created guest profile \"{result.Name}\" and switched to it. Delete it when you are finished demonstrating."
            : $"Created \"{result.Name}\" and switched to it. Finish setup to personalise training.";

        await LoadAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private void BuildDataAreas()
    {
        if (SeparatedAreas.Count > 0 || SharedAreas.Count > 0)
        {
            return;
        }

        foreach (var area in ProfileDataAreas.Separated())
        {
            SeparatedAreas.Add(new ProfileDataAreaViewModel(area.Name, area.Detail));
        }

        foreach (var area in ProfileDataAreas.Shared())
        {
            SharedAreas.Add(new ProfileDataAreaViewModel(area.Name, area.Detail));
        }

        SeparationSummary = ProfileDataAreas.SummariseSeparation();
        HasSharedData = !ProfileDataAreas.IsFullySeparated;
    }
}

/// <summary>One profile in the switcher list.</summary>
/// <remarks>
/// The row owns its commands rather than reaching back to the page's binding context through a
/// relative-source binding. Commands bound across a data template boundary are the classic way a
/// list ends up rendering correctly and doing nothing when tapped, and this list's taps switch
/// whose data the app shows.
/// </remarks>
public sealed partial class ProfileRowViewModel : ObservableObject
{
    private readonly ProfileSwitcherViewModel owner;

    /// <summary>Initialises a row.</summary>
    /// <param name="owner">The screen this row belongs to.</param>
    /// <param name="profile">The profile the row represents.</param>
    /// <param name="isActive">Whether this profile is the active one.</param>
    /// <param name="canDelete">Whether the device holds more than one profile.</param>
    /// <param name="ownedRecordCount">How many records belong only to this profile.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ProfileRowViewModel(ProfileSwitcherViewModel owner, UserProfile profile, bool isActive, bool canDelete, int ownedRecordCount)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(profile);

        this.owner = owner;
        ProfileId = profile.Id;
        Name = profile.DisplayName;
        IsActive = isActive;
        IsGuest = profile.Kind == ProfileKind.Guest;
        CanDelete = canDelete;
        CanSwitch = !isActive;
        renameText = profile.DisplayName;

        KindLabel = IsGuest ? "Guest" : "Personal";
        StateLabel = isActive ? "Active" : "Tap to switch";
        Detail = BuildDetail(profile, ownedRecordCount);
        SwitchDescription = isActive ? $"{Name}, already active" : $"Switch to {Name}";
    }

    /// <summary>The profile identifier.</summary>
    public Guid ProfileId { get; }

    /// <summary>The profile display name.</summary>
    public string Name { get; }

    /// <summary>Whether this profile is the active one.</summary>
    public bool IsActive { get; }

    /// <summary>Whether tapping the row would change the active profile.</summary>
    public bool CanSwitch { get; }

    /// <summary>Whether this is a guest profile.</summary>
    public bool IsGuest { get; }

    /// <summary>Whether this profile may be deleted.</summary>
    public bool CanDelete { get; }

    /// <summary>"Personal" or "Guest".</summary>
    public string KindLabel { get; }

    /// <summary>"Active" or the invitation to switch.</summary>
    public string StateLabel { get; }

    /// <summary>What this profile has stored, in the user's terms.</summary>
    public string Detail { get; }

    /// <summary>Accessibility description for the switch action.</summary>
    public string SwitchDescription { get; }

    /// <summary>Accessibility description for the rename action.</summary>
    public string RenameDescription => $"Rename {Name}";

    /// <summary>Accessibility description for the delete action.</summary>
    public string DeleteDescription => $"Delete {Name}";

    /// <summary>Whether the row is currently showing its rename field.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRenaming))]
    private bool isRenaming;

    /// <summary>Whether the row is showing its normal actions.</summary>
    public bool IsNotRenaming => !IsRenaming;

    /// <summary>The name being typed into the rename field.</summary>
    [ObservableProperty]
    private string renameText;

    /// <summary>Why the typed name was refused, empty when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRenameProblem))]
    private string renameProblem = string.Empty;

    /// <summary>Whether the rename field has a problem to show.</summary>
    public bool HasRenameProblem => !string.IsNullOrEmpty(RenameProblem);

    [RelayCommand]
    private Task SwitchAsync() => owner.SwitchToAsync(this);

    [RelayCommand]
    private void BeginRename()
    {
        RenameText = Name;
        RenameProblem = string.Empty;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CancelRename()
    {
        RenameProblem = string.Empty;
        IsRenaming = false;
    }

    [RelayCommand]
    private Task ConfirmRenameAsync() => owner.ConfirmRenameAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => owner.DeleteAsync(this);

    private static string BuildDetail(UserProfile profile, int ownedRecordCount)
    {
        var measurements = ownedRecordCount switch
        {
            0 => "No measurements yet",
            1 => "1 measurement",
            _ => string.Create(CultureInfo.CurrentCulture, $"{ownedRecordCount} measurements"),
        };

        var lastUsed = profile.LastActivatedUtc is { } activated
            ? string.Create(CultureInfo.CurrentCulture, $"last used {activated.LocalDateTime:d MMM yyyy}")
            : "never used";

        return string.Create(CultureInfo.CurrentCulture, $"{measurements} \u00b7 {lastUsed}");
    }
}

/// <summary>One kind of data, and what a profile switch does to it.</summary>
/// <param name="Name">What a user would call the data.</param>
/// <param name="Detail">Plainly what happens on this device today.</param>
public sealed record ProfileDataAreaViewModel(string Name, string Detail);
