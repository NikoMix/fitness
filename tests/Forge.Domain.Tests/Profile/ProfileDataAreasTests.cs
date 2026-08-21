using Forge.Domain.Common;
using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Profile;

/// <summary>
/// The catalogue the profile switcher reads to tell the user what a switch actually does.
/// </summary>
/// <remarks>
/// The point of these tests is that the screen cannot become a lie by omission. A contributor who
/// adds a persisted entity, or who adopts the scoping seam without revisiting the wording, is
/// caught here rather than by a user discovering somebody else's data behind their own name.
/// </remarks>
public sealed class ProfileDataAreasTests
{
    [Fact]
    public void Every_persisted_domain_entity_is_accounted_for()
    {
        var persisted = typeof(UserProfile).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(Entity).IsAssignableFrom(type))
            .ToArray();

        var accounted = ProfileDataAreas.CoveredEntityTypes()
            .Append(ProfileDataAreas.ProfileEntityType)
            .ToHashSet();

        var missing = persisted.Where(type => !accounted.Contains(type)).Select(type => type.Name).ToArray();

        missing.ShouldBeEmpty(
            $"a persisted entity that the switcher does not describe is data the user is never told about: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Separation_is_derived_from_the_seam_rather_than_declared()
    {
        foreach (var area in ProfileDataAreas.Describe())
        {
            var owned = area.EntityTypes.All(type => typeof(IProfileOwned).IsAssignableFrom(type));

            area.Separation.ShouldBe(
                owned ? ProfileSeparation.Separated : ProfileSeparation.Shared,
                $"{area.Name} must report what its entity types actually implement");
        }
    }

    [Fact]
    public void An_area_is_only_separated_when_every_one_of_its_types_is_owned()
    {
        // A plan whose root carries an owner but whose days do not is still a leak the moment
        // anything reads the children directly.
        var mixed = new ProfileDataArea("Mixed", [typeof(BodyMetric), typeof(Forge.Domain.Training.WorkoutSession)], "…");

        mixed.Separation.ShouldBe(ProfileSeparation.Shared);
    }

    [Fact]
    public void Body_measurements_are_separated_today()
    {
        ProfileDataAreas.Separated().Select(area => area.Name).ShouldContain("Body measurements");
    }

    [Fact]
    public void Training_history_is_still_shared_today()
    {
        // If this ever fails, the migration described in docs/design/multi-profile.md has landed
        // and both this test and that document should be updated together.
        ProfileDataAreas.Shared().Select(area => area.Name).ShouldContain("Workout history");
        ProfileDataAreas.IsFullySeparated.ShouldBeFalse();
    }

    [Fact]
    public void Separated_and_shared_together_are_the_whole_catalogue()
    {
        var describe = ProfileDataAreas.Describe();

        (ProfileDataAreas.Separated().Count + ProfileDataAreas.Shared().Count).ShouldBe(describe.Count);
        ProfileDataAreas.Separated().Intersect(ProfileDataAreas.Shared()).ShouldBeEmpty();
    }

    [Fact]
    public void Separated_areas_are_listed_first()
    {
        var separationOrder = ProfileDataAreas.Describe().Select(area => area.Separation).ToArray();
        var firstShared = Array.IndexOf(separationOrder, ProfileSeparation.Shared);

        if (firstShared >= 0)
        {
            separationOrder[firstShared..].ShouldAllBe(separation => separation == ProfileSeparation.Shared);
        }
    }

    [Fact]
    public void Every_area_explains_itself_in_the_users_terms()
    {
        foreach (var area in ProfileDataAreas.Describe())
        {
            area.Name.ShouldNotBeNullOrWhiteSpace();
            area.Detail.ShouldNotBeNullOrWhiteSpace();
            area.EntityTypes.ShouldNotBeEmpty();
        }
    }

    [Fact]
    public void The_summary_does_not_overstate_what_is_separated()
    {
        var summary = ProfileDataAreas.SummariseSeparation();

        summary.ShouldNotBeNullOrWhiteSpace();
        summary.ShouldContain("shared", Case.Insensitive);
    }

    [Fact]
    public void Only_owned_types_are_offered_to_a_delete()
    {
        ProfileDataAreas.DeletableEntityTypes()
            .ShouldAllBe(type => typeof(IProfileOwned).IsAssignableFrom(type));

        ProfileDataAreas.DeletableEntityTypes().ShouldContain(typeof(BodyMetric));
    }
}
