using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Training;

public sealed class ExerciseSearchIndexTests
{
    private static ExerciseSearchIndex BuildIndex() => new(BuildCatalogue());

    private static List<Exercise> BuildCatalogue() =>
    [
        TestExercise.Create("Squat", MovementPattern.Squat, "Quadriceps", ["Glutes"]),
        TestExercise.Create("Goblet Squat", MovementPattern.Squat, "Quadriceps", ["Glutes"], "Dumbbell"),
        TestExercise.Create("Squat Jump", MovementPattern.Squat, "Quadriceps", ["Calves"]),
        TestExercise.Create("Dumbbell Bench Press", MovementPattern.Push, "Chest", ["Triceps"], "Dumbbell"),
        TestExercise.Create("Seated Dumbbell Shoulder Press", MovementPattern.Push, "Shoulders", ["Triceps"], "Dumbbell"),
        TestExercise.Create("Push Up", MovementPattern.Push, "Chest", ["Triceps"]),
        TestExercise.Create("Kettlebell Swing", MovementPattern.Hinge, "Glutes", ["Hamstrings"], "Kettlebell"),
        TestExercise.Create("Glute Bridge", MovementPattern.Hinge, "Glutes", ["Hamstrings"]),
        TestExercise.Create("Hip Thrust", MovementPattern.Hinge, "Glutes", ["Hamstrings"], "Barbell")
    ];

    [Fact]
    public void An_exact_name_beats_a_prefix_which_beats_a_word_inside_the_name()
    {
        var results = BuildIndex().Search("squat");

        results.Select(result => result.Exercise.Name)
            .ShouldBe(["Squat", "Squat Jump", "Goblet Squat"]);
        results[0].Score.ShouldBeGreaterThan(results[1].Score);
        results[1].Score.ShouldBeGreaterThan(results[2].Score);
    }

    [Fact]
    public void A_name_outranks_a_primary_muscle_which_outranks_a_secondary_muscle()
    {
        var results = BuildIndex().Search("glute");

        results[0].Exercise.Name.ShouldBe("Glute Bridge");
        results[0].BestField.ShouldBe(ExerciseSearchField.Name);
        results[1].BestField.ShouldBe(ExerciseSearchField.PrimaryMuscle);
        results[^1].BestField.ShouldBe(ExerciseSearchField.SecondaryMuscle);
        results[0].Score.ShouldBeGreaterThan(results[1].Score);
        results[1].Score.ShouldBeGreaterThan(results[^1].Score);
    }

    [Fact]
    public void Every_word_typed_must_match_something()
    {
        var results = BuildIndex().Search("dumbbell press");

        results.Select(result => result.Exercise.Name)
            .ShouldBe(["Dumbbell Bench Press", "Seated Dumbbell Shoulder Press"]);
    }

    [Fact]
    public void A_word_that_matches_nothing_returns_nothing_rather_than_everything()
        => BuildIndex().Search("dumbbell kayak").ShouldBeEmpty();

    [Fact]
    public void A_muscle_hit_explains_itself_so_the_result_does_not_look_like_a_bug()
    {
        var results = BuildIndex().Search("glutes");

        var best = results[0];
        best.Exercise.PrimaryMuscle.ShouldBe("Glutes");
        best.BestField.ShouldBe(ExerciseSearchField.PrimaryMuscle);
        best.MatchExplanation.ShouldBe("Trains the Glutes");

        var secondary = results.First(result => result.Exercise.Name == "Squat");
        secondary.BestField.ShouldBe(ExerciseSearchField.SecondaryMuscle);
        secondary.Score.ShouldBeLessThan(best.Score);
    }

    [Fact]
    public void Equipment_and_pattern_are_searchable_too()
    {
        var equipmentHit = BuildIndex().Search("barbell").Single();
        equipmentHit.Exercise.Name.ShouldBe("Hip Thrust");
        equipmentHit.BestField.ShouldBe(ExerciseSearchField.Equipment);
        equipmentHit.MatchExplanation.ShouldBe("Uses Barbell");

        var patternHits = BuildIndex().Search("hinge");
        patternHits.Select(result => result.Exercise.Name)
            .ShouldBe(["Hip Thrust", "Glute Bridge", "Kettlebell Swing"]);
        patternHits.ShouldAllBe(result => result.BestField == ExerciseSearchField.Pattern);
    }

    [Fact]
    public void A_blank_query_browses_favourites_then_recents_then_everything_alphabetically()
    {
        var catalogue = BuildCatalogue();
        catalogue.Single(exercise => exercise.Name == "Push Up").Favourite();
        catalogue.Single(exercise => exercise.Name == "Squat Jump").UsedAt(DateTimeOffset.UnixEpoch);
        catalogue.Single(exercise => exercise.Name == "Hip Thrust").UsedAt(DateTimeOffset.UnixEpoch.AddDays(1));

        var results = new ExerciseSearchIndex(catalogue).Search(null);

        results.Select(result => result.Exercise.Name).Take(3)
            .ShouldBe(["Push Up", "Hip Thrust", "Squat Jump"]);
        results[3].Exercise.Name.ShouldBe("Dumbbell Bench Press");
        results.Count.ShouldBe(catalogue.Count);
    }

    [Fact]
    public void Favourites_break_ties_but_never_outrank_what_the_user_typed()
    {
        var catalogue = BuildCatalogue();
        catalogue.Single(exercise => exercise.Name == "Goblet Squat").Favourite();

        var results = new ExerciseSearchIndex(catalogue).Search("squat");

        // The favourite is still last, because "Squat" and "Squat Jump" match the typed text
        // more strongly. Pinning something should not quietly reorder a search.
        results[^1].Exercise.Name.ShouldBe("Goblet Squat");
    }

    [Fact]
    public void Filters_are_applied_before_ranking()
    {
        var results = BuildIndex().Search("squat", ExerciseFilter.For(equipment: ["Dumbbell"]));

        results.Select(result => result.Exercise.Name).ShouldBe(["Goblet Squat"]);
    }

    [Fact]
    public void Browsing_respects_the_filter_as_well()
    {
        var results = BuildIndex().Search(string.Empty, ExerciseFilter.For(patterns: [MovementPattern.Hinge]));

        results.Select(result => result.Exercise.Name)
            .ShouldBe(["Glute Bridge", "Hip Thrust", "Kettlebell Swing"]);
    }

    [Fact]
    public void Results_can_be_limited()
        => BuildIndex().Search("squat", limit: 2).Count.ShouldBe(2);

    [Fact]
    public void The_index_exposes_the_facets_a_filter_bar_needs()
    {
        var index = BuildIndex();

        index.Count.ShouldBe(9);
        index.Equipment.ShouldBe(["Barbell", "Bodyweight", "Dumbbell", "Kettlebell"]);
        index.Patterns.ShouldBe([MovementPattern.Squat, MovementPattern.Hinge, MovementPattern.Push]);
        index.Muscles.ShouldContain("Quadriceps");
        index.Muscles.ShouldContain("Triceps");
        index.Muscles.ShouldBe(index.Muscles.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Searching_is_case_and_whitespace_insensitive()
    {
        var index = BuildIndex();

        index.Search("  GOBLET  ").Single().Exercise.Name.ShouldBe("Goblet Squat");
        index.Search("goblet").Single().Exercise.Name.ShouldBe("Goblet Squat");
    }
}
