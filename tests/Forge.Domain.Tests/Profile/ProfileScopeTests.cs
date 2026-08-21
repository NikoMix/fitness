using Forge.Domain.Measurement;
using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Profile;

/// <summary>
/// The query-scoping seam. Everything here exists to make one claim safe to rely on: a scoped read
/// cannot return a record belonging to another profile, whatever the caller passes in.
/// </summary>
public sealed class ProfileScopeTests
{
    [Fact]
    public void A_default_scope_resolves_to_nothing()
    {
        default(ProfileScope).IsResolved.ShouldBeFalse();
        ProfileScope.None.IsResolved.ShouldBeFalse();
        new ProfileScope(Guid.Empty).ShouldBe(ProfileScope.None);
    }

    [Fact]
    public void A_scope_built_from_a_profile_names_that_profile()
    {
        var profile = new UserProfile { DisplayName = "Avery" };

        var scope = ProfileScope.For(profile);

        scope.IsResolved.ShouldBeTrue();
        scope.ProfileId.ShouldBe(profile.Id);
    }

    [Fact]
    public void Building_a_scope_from_nothing_throws_rather_than_matching_everything()
    {
        Should.Throw<ArgumentNullException>(() => ProfileScope.For(null!));
    }

    [Fact]
    public void A_scope_owns_only_its_own_records()
    {
        var mine = new ProfileScope(Guid.CreateVersion7());
        var theirs = new ProfileScope(Guid.CreateVersion7());

        var record = Metric(mine.ProfileId);

        mine.Owns(record).ShouldBeTrue();
        theirs.Owns(record).ShouldBeFalse();
    }

    [Fact]
    public void An_unresolved_scope_owns_nothing()
    {
        // Fail-closed. If Forge does not know whose record this is, the answer is "not yours".
        ProfileScope.None.Owns(Metric(Guid.CreateVersion7())).ShouldBeFalse();
        ProfileScope.None.Owns(Metric(Guid.Empty)).ShouldBeFalse();
    }

    [Fact]
    public void Filtering_a_sequence_returns_only_the_scoped_profiles_records()
    {
        var mine = Guid.CreateVersion7();
        var theirs = Guid.CreateVersion7();
        var records = new[] { Metric(mine), Metric(theirs), Metric(mine), Metric(theirs) };

        var scoped = records.OwnedBy(new ProfileScope(mine)).ToArray();

        scoped.Length.ShouldBe(2);
        scoped.ShouldAllBe(record => record.UserProfileId == mine);
    }

    [Fact]
    public void Filtering_a_sequence_with_an_unresolved_scope_returns_nothing()
    {
        // The dangerous alternative is returning everything, which would make an unconfigured
        // screen show every profile's data while looking like it worked.
        var records = new[] { Metric(Guid.CreateVersion7()), Metric(Guid.CreateVersion7()) };

        records.OwnedBy(ProfileScope.None).ShouldBeEmpty();
    }

    [Fact]
    public void Filtering_nothing_throws_rather_than_returning_an_unscoped_result()
    {
        Should.Throw<ArgumentNullException>(() => ((IEnumerable<BodyMetric>)null!).OwnedBy(ProfileScope.None).ToArray());
    }

    [Fact]
    public void A_scoped_sequence_never_leaks_a_record_no_matter_how_many_profiles_share_the_device()
    {
        var profileIds = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToArray();
        var records = profileIds
            .SelectMany(id => Enumerable.Range(0, 4).Select(_ => Metric(id)))
            .ToArray();

        foreach (var profileId in profileIds)
        {
            var scoped = records.OwnedBy(new ProfileScope(profileId)).ToArray();

            scoped.Length.ShouldBe(4);
            scoped.ShouldAllBe(record => record.UserProfileId == profileId);
            scoped.Select(record => record.Id).ShouldBeUnique();
        }
    }

    [Fact]
    public void Every_record_belongs_to_exactly_one_scope()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var records = new[] { Metric(first), Metric(second), Metric(first) };

        var firstScope = records.OwnedBy(new ProfileScope(first)).Select(record => record.Id).ToHashSet();
        var secondScope = records.OwnedBy(new ProfileScope(second)).Select(record => record.Id).ToHashSet();

        firstScope.Overlaps(secondScope).ShouldBeFalse();
        firstScope.Count.ShouldBe(2);
        secondScope.Count.ShouldBe(1);
        (firstScope.Count + secondScope.Count).ShouldBe(records.Length, "no record may be dropped by scoping");
    }

    private static BodyMetric Metric(Guid profileId) => new()
    {
        UserProfileId = profileId,
        Weight = Mass.FromKilograms(70m),
    };
}
