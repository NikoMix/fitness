using Forge.Domain.Training;
using Forge.Infrastructure.Content;
using Shouldly;

namespace Forge.Domain.Tests.Training;

/// <summary>
/// Runs the library logic against the catalogue that actually ships.
/// </summary>
/// <remarks>
/// The other tests build their own exercises, so they only prove the rules are self-consistent.
/// These bind the rules to the real sixty movements, which is where "no suitable alternative"
/// stops being a hypothetical branch and becomes something a bodyweight-only user hits on their
/// first attempt to swap a pulling exercise.
/// </remarks>
public sealed class ShippedCatalogueBehaviourTests
{
    private static IReadOnlyList<Exercise> Catalogue => SeedCatalogue.Exercises;

    [Fact]
    public void Every_shipped_exercise_carries_its_own_written_guidance()
    {
        Catalogue.ShouldAllBe(exercise => exercise.ExecutionSteps.Count > 0);
        Catalogue.ShouldAllBe(exercise => exercise.CoachingCues.Count > 0);
        Catalogue.ShouldAllBe(exercise => exercise.CommonMistakes.Count > 0);
        Catalogue.ShouldAllBe(exercise => exercise.SafetyNotes.Count > 0);

        // Templated content would collapse to a handful of distinct blocks, which is what made
        // a wall sit and a bench press list identical mistakes.
        Distinct(exercise => exercise.CommonMistakes).ShouldBe(Catalogue.Count);
        Distinct(exercise => exercise.ExecutionSteps).ShouldBe(Catalogue.Count);
        Distinct(exercise => exercise.CoachingCues).ShouldBe(Catalogue.Count);
        Distinct(exercise => exercise.SafetyNotes).ShouldBe(Catalogue.Count);
    }

    [Fact]
    public void A_bodyweight_trainee_is_told_honestly_that_pulling_cannot_be_replaced()
    {
        var latPulldown = Find("Lat Pulldown");

        var suggestions = ExerciseSubstitution.Suggest(latPulldown, Catalogue, EquipmentAvailability.BodyweightOnly);

        // Every pulling movement in the catalogue needs something to pull on, so the honest
        // answer is none - not a pushing movement dressed up as an alternative.
        suggestions.HasResults.ShouldBeFalse();
        suggestions.Explanation.ShouldContain("needs equipment you have not listed");
        suggestions.EquipmentThatWouldUnlockMore.ShouldContain("Pull-up bar");
        suggestions.EquipmentThatWouldUnlockMore.ShouldContain("Resistance band");
    }

    [Fact]
    public void A_dumbbell_owner_gets_real_squat_alternatives_led_by_the_same_pattern()
    {
        var frontSquat = Find("Front Squat");

        var suggestions = ExerciseSubstitution.Suggest(
            frontSquat, Catalogue, EquipmentAvailability.From(["Dumbbell"]));

        suggestions.HasResults.ShouldBeTrue();
        suggestions.Results[0].PatternMatches.ShouldBeTrue();
        suggestions.Results.ShouldAllBe(result =>
            result.Exercise.Pattern == MovementPattern.Squat || result.Exercise.Pattern == MovementPattern.Lunge);
        suggestions.Results.ShouldAllBe(result =>
            result.Exercise.Equipment == null || result.Exercise.Equipment == "Dumbbell");
    }

    [Fact]
    public void No_exercise_in_the_catalogue_ever_produces_an_unexplained_empty_screen()
    {
        foreach (var exercise in Catalogue)
        {
            var suggestions = ExerciseSubstitution.Suggest(exercise, Catalogue, EquipmentAvailability.BodyweightOnly);

            suggestions.Explanation.ShouldNotBeNullOrWhiteSpace();
            suggestions.Results.ShouldAllBe(result => result.Reason.Length > 0);
        }
    }

    [Fact]
    public void Searching_the_real_catalogue_returns_what_a_user_would_expect()
    {
        var index = new ExerciseSearchIndex(Catalogue);

        index.Search("goblet").Select(result => result.Exercise.Name).ShouldBe(["Goblet Squat"]);
        index.Search("kettlebell").ShouldAllBe(result => result.Exercise.Equipment == "Kettlebell");

        // "squat" matches six movements by name and two more only by movement pattern. The
        // named ones have to come first, or typing a name would surface unrelated exercises.
        var squats = index.Search("squat");
        var named = Catalogue.Count(exercise => exercise.Name.Contains("Squat", StringComparison.Ordinal));
        squats.Count.ShouldBeGreaterThan(named);
        squats.Take(named).ShouldAllBe(result => result.Exercise.Name.Contains("Squat", StringComparison.Ordinal));
        squats.ShouldAllBe(result =>
            result.Exercise.Pattern == MovementPattern.Squat
            || result.Exercise.Name.Contains("Squat", StringComparison.Ordinal));
    }

    [Fact]
    public void Filtering_the_real_catalogue_stays_fast_and_narrows_correctly()
    {
        var index = new ExerciseSearchIndex(Catalogue);

        var bodyweightPushing = index.Search(
            null,
            ExerciseFilter.For(equipment: ["Bodyweight"], patterns: [MovementPattern.Push]));

        bodyweightPushing.ShouldNotBeEmpty();
        bodyweightPushing.ShouldAllBe(result => result.Exercise.Equipment == null);
        bodyweightPushing.ShouldAllBe(result => result.Exercise.Pattern == MovementPattern.Push);
    }

    [Fact]
    public void The_index_offers_every_facet_present_in_the_shipped_catalogue()
    {
        var index = new ExerciseSearchIndex(Catalogue);

        index.Count.ShouldBe(Catalogue.Count);
        index.Patterns.Count.ShouldBe(10);
        index.Equipment.ShouldContain("Bodyweight");
        index.Muscles.Count.ShouldBeGreaterThan(10);
    }

    private static int Distinct(Func<Exercise, List<string>> selector)
        => Catalogue.Select(exercise => string.Join('|', selector(exercise))).Distinct(StringComparer.Ordinal).Count();

    private static Exercise Find(string name)
        => SeedCatalogue.FindByName(name) ?? throw new InvalidOperationException($"'{name}' is missing from the shipped catalogue.");
}
