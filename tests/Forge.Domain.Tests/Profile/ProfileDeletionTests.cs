using Forge.Domain.Measurement;
using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Profile;

/// <summary>
/// Deleting a profile is the only operation in Forge that destroys data belonging to somebody
/// other than the person tapping the button, and there is no backend copy to restore from. These
/// tests are about the rows that must survive, not the rows that go.
/// </summary>
public sealed class ProfileDeletionTests
{
    [Fact]
    public void Only_the_deleted_profiles_records_are_selected()
    {
        var doomed = Guid.CreateVersion7();
        var survivor = Guid.CreateVersion7();
        var records = new[] { Metric(doomed), Metric(survivor), Metric(doomed), Metric(survivor) };

        var partition = ProfileDeletion.Partition(records, new ProfileScope(doomed));

        partition.ToDelete.Count.ShouldBe(2);
        partition.ToKeep.Count.ShouldBe(2);
        records.Where(record => partition.ToDelete.Contains(record.Id))
               .ShouldAllBe(record => record.UserProfileId == doomed);
    }

    [Fact]
    public void The_partition_is_total_and_disjoint()
    {
        // Any record that appears in neither half would silently survive a delete; any record in
        // both halves means the delete list was built from a different read than the keep list.
        var doomed = Guid.CreateVersion7();
        var records = Enumerable.Range(0, 20)
            .Select(index => Metric(index % 3 == 0 ? doomed : Guid.CreateVersion7()))
            .ToArray();

        var partition = ProfileDeletion.Partition(records, new ProfileScope(doomed));

        partition.ToDelete.Intersect(partition.ToKeep).ShouldBeEmpty();
        partition.ToDelete.Concat(partition.ToKeep).Order().ShouldBe(records.Select(record => record.Id).Order());
    }

    [Fact]
    public void Every_other_profiles_record_is_kept()
    {
        var profileIds = Enumerable.Range(0, 4).Select(_ => Guid.CreateVersion7()).ToArray();
        var records = profileIds
            .SelectMany(id => Enumerable.Range(0, 3).Select(_ => Metric(id)))
            .ToArray();

        foreach (var doomed in profileIds)
        {
            var partition = ProfileDeletion.Partition(records, new ProfileScope(doomed));

            var kept = records.Where(record => partition.ToKeep.Contains(record.Id)).ToArray();

            kept.Length.ShouldBe(9);
            kept.ShouldAllBe(record => record.UserProfileId != doomed);

            foreach (var other in profileIds.Where(id => id != doomed))
            {
                kept.Count(record => record.UserProfileId == other)
                    .ShouldBe(3, "no other profile may lose a record when one profile is deleted");
            }
        }
    }

    [Fact]
    public void An_unresolved_scope_deletes_nothing()
    {
        // If the active profile could not be resolved, a delete that "matched everything" would
        // empty the device. Every record must land in the surviving half.
        var records = new[] { Metric(Guid.CreateVersion7()), Metric(Guid.Empty) };

        var partition = ProfileDeletion.Partition(records, ProfileScope.None);

        partition.ToDelete.ShouldBeEmpty();
        partition.ToKeep.Count.ShouldBe(2);
    }

    [Fact]
    public void Partitioning_nothing_throws_rather_than_deleting_nothing_quietly()
    {
        Should.Throw<ArgumentNullException>(() => ProfileDeletion.Partition<BodyMetric>(null!, ProfileScope.None));
    }

    [Fact]
    public void Owned_entity_types_track_the_seam()
    {
        ProfileDeletion.OwnedEntityTypes().ShouldContain(typeof(BodyMetric));
        ProfileDeletion.OwnedEntityTypes().ShouldAllBe(type => typeof(IProfileOwned).IsAssignableFrom(type));
    }

    [Fact]
    public void The_plan_counts_exactly_what_will_be_removed()
    {
        var profile = new UserProfile { DisplayName = "Avery" };
        var counts = new Dictionary<Type, int> { [typeof(BodyMetric)] = 12 };

        var plan = ProfileDeletionPlan.Create(profile, counts, [typeof(BodyMetric)], successor: null);

        plan.RemovedRecordCount.ShouldBe(12);
        plan.Describe().ShouldContain("12 records");
        plan.Headline.ShouldBe("Delete \"Avery\"?");
    }

    [Fact]
    public void The_plan_lists_shared_data_as_kept_rather_than_pretending_it_is_erased()
    {
        var profile = new UserProfile { DisplayName = "Avery" };

        var plan = ProfileDeletionPlan.Create(profile, new Dictionary<Type, int>(), [typeof(BodyMetric)], successor: null);

        plan.Retained.ShouldNotBeEmpty();
        plan.Retained.Select(line => line.Name).ShouldContain("Workout history");
        plan.Describe().ShouldContain("Kept");
    }

    [Fact]
    public void An_owned_area_the_delete_cannot_remove_is_reported_as_kept()
    {
        // This is the self-correcting part. If a feature adopts the seam but the delete is not
        // extended, the dialog must not claim to have erased data it never touched.
        var profile = new UserProfile { DisplayName = "Avery" };
        var counts = new Dictionary<Type, int> { [typeof(BodyMetric)] = 4 };

        var plan = ProfileDeletionPlan.Create(profile, counts, [], successor: null);

        plan.Removed.ShouldBeEmpty();
        plan.RemovedRecordCount.ShouldBe(0);
        plan.Retained.Select(line => line.Name).ShouldContain("Body measurements");
    }

    [Fact]
    public void The_plan_names_the_profile_that_takes_over()
    {
        var profile = new UserProfile { DisplayName = "Avery" };
        var successor = new UserProfile { DisplayName = "Blake" };

        var plan = ProfileDeletionPlan.Create(profile, new Dictionary<Type, int>(), [typeof(BodyMetric)], successor);

        plan.SuccessorName.ShouldBe("Blake");
        plan.Describe().ShouldContain("\"Blake\" becomes the active profile.");
    }

    [Fact]
    public void A_refused_plan_carries_the_reason_and_is_not_permitted()
    {
        var profile = new UserProfile { DisplayName = "Avery" };

        var plan = ProfileDeletionPlan.Create(
            profile,
            new Dictionary<Type, int>(),
            [typeof(BodyMetric)],
            successor: null,
            refusal: "This is the only profile on this device.");

        plan.IsPermitted.ShouldBeFalse();
        plan.Refusal.ShouldBe("This is the only profile on this device.");
    }

    [Fact]
    public void The_plan_always_states_that_the_delete_cannot_be_undone()
    {
        var profile = new UserProfile { DisplayName = "Avery" };

        var description = ProfileDeletionPlan
            .Create(profile, new Dictionary<Type, int>(), [typeof(BodyMetric)], successor: null)
            .Describe();

        description.ShouldContain("cannot be undone");
        description.ShouldContain("no cloud backup");
    }

    private static BodyMetric Metric(Guid profileId) => new()
    {
        UserProfileId = profileId,
        Weight = Mass.FromKilograms(70m),
    };
}
