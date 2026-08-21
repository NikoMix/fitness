using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Profile;

/// <summary>
/// Which profile is active, and why. On a shared device this decides whose training history the
/// app shows and whose record the next logged set lands on, so the rules are pinned rather than
/// left to whatever ordering the database happens to return.
/// </summary>
public sealed class ActiveProfileSelectorTests
{
    [Fact]
    public void No_profiles_means_no_active_profile()
    {
        ActiveProfileSelector.SelectActive([]).ShouldBeNull();
        ActiveProfileSelector.SelectScope([]).ShouldBe(ProfileScope.None);
    }

    [Fact]
    public void The_oldest_profile_wins_before_anyone_has_ever_switched()
    {
        // Reproduces the single-profile behaviour Forge shipped with, so upgrading a device that
        // already has a profile does not silently change whose data is shown.
        var oldest = Profile("Avery", created: Days(-30));
        var newest = Profile("Blake", created: Days(-2));

        ActiveProfileSelector.SelectActive([newest, oldest])!.DisplayName.ShouldBe("Avery");
    }

    [Fact]
    public void The_most_recently_activated_profile_wins()
    {
        var avery = Profile("Avery", created: Days(-30), activated: Days(-5));
        var blake = Profile("Blake", created: Days(-2), activated: Days(-1));

        ActiveProfileSelector.SelectActive([avery, blake])!.DisplayName.ShouldBe("Blake");
    }

    [Fact]
    public void An_explicit_activation_beats_a_profile_that_has_never_been_activated()
    {
        var neverUsed = Profile("Avery", created: Days(-30));
        var used = Profile("Blake", created: Days(-2), activated: Days(-1));

        ActiveProfileSelector.SelectActive([neverUsed, used])!.DisplayName.ShouldBe("Blake");
    }

    [Fact]
    public void A_guest_is_not_chosen_by_default_over_a_personal_profile()
    {
        // A coach who created a demo profile and restarted the app must not find Forge showing the
        // demo. Only an explicit switch puts the device on a guest.
        var guest = Profile("Demo", created: Days(-30), kind: ProfileKind.Guest);
        var personal = Profile("Avery", created: Days(-2));

        ActiveProfileSelector.SelectActive([guest, personal])!.DisplayName.ShouldBe("Avery");
    }

    [Fact]
    public void An_explicitly_activated_guest_stays_active()
    {
        var guest = Profile("Demo", created: Days(-30), activated: Days(-1), kind: ProfileKind.Guest);
        var personal = Profile("Avery", created: Days(-2), activated: Days(-3));

        ActiveProfileSelector.SelectActive([guest, personal])!.DisplayName.ShouldBe("Demo");
    }

    [Fact]
    public void A_soft_deleted_profile_is_never_active()
    {
        var deleted = Profile("Avery", created: Days(-30), activated: Days(-1));
        deleted.DeletedUtc = DateTimeOffset.UtcNow;
        var remaining = Profile("Blake", created: Days(-2));

        ActiveProfileSelector.SelectActive([deleted, remaining])!.DisplayName.ShouldBe("Blake");
    }

    [Fact]
    public void Selection_is_deterministic_when_two_profiles_were_activated_at_the_same_instant()
    {
        // A timestamp collision must not make the active profile depend on list order, because the
        // list order is whatever SQLite returned.
        var instant = Days(-1);
        var first = Profile("Avery", created: Days(-30), activated: instant);
        var second = Profile("Blake", created: Days(-2), activated: instant);

        var forwards = ActiveProfileSelector.SelectActive([first, second]);
        var backwards = ActiveProfileSelector.SelectActive([second, first]);

        forwards!.Id.ShouldBe(backwards!.Id);
        forwards.DisplayName.ShouldBe("Blake", "the more recently created profile breaks the tie");
    }

    [Fact]
    public void Display_order_is_personal_profiles_first_then_oldest_first()
    {
        var guest = Profile("Demo", created: Days(-40), kind: ProfileKind.Guest);
        var blake = Profile("Blake", created: Days(-2));
        var avery = Profile("Avery", created: Days(-30));

        var ordered = ActiveProfileSelector.OrderForDisplay([guest, blake, avery]);

        ordered.Select(profile => profile.DisplayName).ShouldBe(["Avery", "Blake", "Demo"]);
    }

    [Fact]
    public void Display_order_does_not_change_when_the_active_profile_changes()
    {
        // The row position is how somebody finds themselves on a shared device. A list that
        // reorders after a switch is how the next person taps the wrong row.
        var avery = Profile("Avery", created: Days(-30));
        var blake = Profile("Blake", created: Days(-2));

        var before = ActiveProfileSelector.OrderForDisplay([avery, blake]).Select(profile => profile.Id);
        blake.LastActivatedUtc = DateTimeOffset.UtcNow;
        var after = ActiveProfileSelector.OrderForDisplay([avery, blake]).Select(profile => profile.Id);

        after.ShouldBe(before);
    }

    [Fact]
    public void The_device_stops_accepting_profiles_at_the_limit()
    {
        var full = Enumerable.Range(0, ActiveProfileSelector.MaximumProfiles)
            .Select(index => Profile($"Person {index}", created: Days(-index)))
            .ToArray();

        ActiveProfileSelector.CanAdd(full).ShouldBeFalse();
        ActiveProfileSelector.CanAdd(full.Take(ActiveProfileSelector.MaximumProfiles - 1)).ShouldBeTrue();
    }

    [Fact]
    public void The_last_profile_cannot_be_deleted()
    {
        // Deleting it would leave a database full of records Forge can no longer attribute to
        // anyone. Emptying the device is the erasure flow, which also destroys the encryption key.
        var only = Profile("Avery", created: Days(-1));

        ActiveProfileSelector.CanDelete([only], only.Id).ShouldBeFalse();
    }

    [Fact]
    public void A_profile_can_be_deleted_once_another_one_exists()
    {
        var avery = Profile("Avery", created: Days(-30));
        var blake = Profile("Blake", created: Days(-2));

        ActiveProfileSelector.CanDelete([avery, blake], avery.Id).ShouldBeTrue();
        ActiveProfileSelector.CanDelete([avery, blake], Guid.CreateVersion7()).ShouldBeFalse();
    }

    [Fact]
    public void The_successor_is_the_most_recently_used_remaining_profile()
    {
        var avery = Profile("Avery", created: Days(-30), activated: Days(-9));
        var blake = Profile("Blake", created: Days(-20), activated: Days(-2));
        var casey = Profile("Casey", created: Days(-10), activated: Days(-1));

        ActiveProfileSelector.SelectSuccessor([avery, blake, casey], casey.Id)!.DisplayName.ShouldBe("Blake");
    }

    [Fact]
    public void There_is_no_successor_when_the_last_profile_is_removed()
    {
        var only = Profile("Avery", created: Days(-1));

        ActiveProfileSelector.SelectSuccessor([only], only.Id).ShouldBeNull();
    }

    private static DateTimeOffset Days(int offset) => DateTimeOffset.UtcNow.AddDays(offset);

    private static UserProfile Profile(
        string name,
        DateTimeOffset created,
        DateTimeOffset? activated = null,
        ProfileKind kind = ProfileKind.Personal) =>
        new()
        {
            DisplayName = name,
            CreatedUtc = created,
            LastActivatedUtc = activated,
            Kind = kind,
        };
}
