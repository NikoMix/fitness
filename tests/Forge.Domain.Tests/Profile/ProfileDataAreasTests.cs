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
        // anything reads the children directly. Exercise stands in for the unowned half because the
        // catalogue is shared on purpose and is expected to stay that way.
        var mixed = new ProfileDataArea("Mixed", [typeof(BodyMetric), typeof(Forge.Domain.Training.Exercise)], "…");

        mixed.Separation.ShouldBe(ProfileSeparation.Shared);
    }

    [Fact]
    public void Body_measurements_are_separated_today()
    {
        ProfileDataAreas.Separated().Select(area => area.Name).ShouldContain("Body measurements");
    }

    [Fact]
    public void Training_history_is_separated_today()
    {
        // The migration described in docs/design/multi-profile.md has landed for training. This
        // test is the counterpart of the one it replaced: it fails if workout history is ever
        // moved back to shared, which would make the switcher's wording a lie in the other
        // direction.
        ProfileDataAreas.Separated().Select(area => area.Name).ShouldContain("Workout history");
        ProfileDataAreas.Separated().Select(area => area.Name).ShouldContain("Workout in progress");
        ProfileDataAreas.Separated().Select(area => area.Name).ShouldContain("Training plans");
    }

    [Fact]
    public void The_catalogues_are_still_shared_today()
    {
        // The exercise and food catalogues are shared on purpose: they are shipped reference
        // content, not anybody's record. Phase 4 of docs/design/multi-profile.md covers the
        // per-person state that currently lives on those shared rows - favourites, recently used
        // and user-created flags - which is a join entity, not a filter.
        ProfileDataAreas.Shared().Select(area => area.Name).ShouldContain("Exercise library");
        ProfileDataAreas.Shared().Select(area => area.Name).ShouldContain("Food catalogue");
        ProfileDataAreas.IsFullySeparated.ShouldBeFalse();
    }

    [Fact]
    public void Every_logging_area_a_user_would_expect_to_follow_the_profile_does()
    {
        // Stated as an explicit list rather than as "everything except the catalogues", so that
        // adding an area does not silently join the separated set without anyone deciding.
        string[] mustBeSeparated =
        [
            "Body measurements",
            "Workout history",
            "Workout in progress",
            "Training plans",
            "Food log",
            "Hydration",
            "Recipes",
            "Check-ins and soreness",
        ];

        var separated = ProfileDataAreas.Separated().Select(area => area.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var area in mustBeSeparated)
        {
            separated.ShouldContain(area, $"{area} holds one person's own logging and must not be visible to another profile");
        }
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
        ProfileDataAreas.DeletableEntityTypes().ShouldContain(typeof(Forge.Domain.Training.SetEntry));
    }

    [Fact]
    public void The_shared_catalogues_are_never_offered_to_a_delete()
    {
        // Deleting shared rows during a profile delete would destroy shipped content, and worse,
        // any user-created exercise or food that the remaining profiles still reference.
        ProfileDataAreas.DeletableEntityTypes().ShouldNotContain(typeof(Forge.Domain.Training.Exercise));
        ProfileDataAreas.DeletableEntityTypes().ShouldNotContain(typeof(Forge.Domain.Nutrition.FoodItem));
    }
}
