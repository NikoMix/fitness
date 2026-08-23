using Forge.Domain.Coaching;
using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Coaching;

/// <summary>
/// Covers the bridge from a free-text limitation to a coaching block.
/// </summary>
/// <remarks>
/// The defect these pin: <c>CoachingDataService</c> passed <c>Contraindications: []</c>, so
/// somebody who typed "avoid overhead pressing" during onboarding, and saw it echoed back on the
/// review step, was then recommended overhead pressing. The echo is what made it worse than doing
/// nothing, because it told them they had been heard.
/// </remarks>
public sealed class MovementLimitationCoachingTests
{
    [Fact]
    public void A_declared_knee_blocks_a_squat()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("knee");

        var contraindications = MovementLimitationCoaching.ContraindicationsFor(declaration, "Quadriceps", MovementPattern.Squat);

        contraindications.Count.ShouldBe(1);
        contraindications[0].IsInjury.ShouldBeTrue();
        contraindications[0].IsActive.ShouldBeTrue();
        contraindications[0].DeclaredArea.ShouldBe("knee");
    }

    /// <summary>
    /// The match key is the exercise's own muscle, which is what the recommender compares against.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the decision is made on the movement pattern. Matching a declared
    /// area against muscle names finds exactly one of the nine areas across the seeded catalogue -
    /// "lower back" against "Lower back" - so the other eight would silently block nothing.
    /// </remarks>
    [Fact]
    public void The_contraindication_matches_on_the_exercises_own_muscle()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("knee");

        var contraindications = MovementLimitationCoaching.ContraindicationsFor(declaration, "Quadriceps", MovementPattern.Squat);

        contraindications[0].MuscleGroup.ShouldBe("Quadriceps");
    }

    [Fact]
    public void A_declared_knee_does_not_block_an_unrelated_pattern()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("knee");

        MovementLimitationCoaching
            .ContraindicationsFor(declaration, "Chest", MovementPattern.Push)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// A joint that names no muscle in the catalogue still blocks, which is the point.
    /// </summary>
    /// <remarks>
    /// "shoulder" never matches "Shoulders", and singularising it would fix three of nine areas
    /// while leaving five silently inert - a partly-working filter that produces evidence of
    /// working. Deciding on the pattern sidesteps the whole question.
    /// </remarks>
    [Theory]
    [InlineData("shoulder", "Shoulders", MovementPattern.Push)]
    [InlineData("ankle", "Calves", MovementPattern.Lunge)]
    [InlineData("hip", "Hamstrings", MovementPattern.Hinge)]
    [InlineData("wrist", "Grip", MovementPattern.Carry)]
    public void Areas_that_match_no_muscle_name_still_block_their_patterns(
        string declared,
        string primaryMuscle,
        MovementPattern pattern)
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration(declared);

        MovementLimitationCoaching
            .ContraindicationsFor(declaration, primaryMuscle, pattern)
            .ShouldNotBeEmpty();
    }

    /// <summary>The sentence names only the area that caused this block.</summary>
    [Fact]
    public void The_named_area_is_the_one_that_caused_the_block()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("knee, shoulder");

        var pushBlock = MovementLimitationCoaching.ContraindicationsFor(declaration, "Chest", MovementPattern.Push);

        pushBlock.Count.ShouldBe(1);
        pushBlock[0].DeclaredArea.ShouldBe("shoulder");
    }

    [Fact]
    public void Nothing_declared_blocks_nothing()
    {
        MovementLimitationCoaching
            .ContraindicationsFor(MovementLimitationDeclaration.Empty, "Quadriceps", MovementPattern.Squat)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// Text Forge could not read is never acted on.
    /// </summary>
    /// <remarks>
    /// Blocking on a phrase nobody understood would be guessing at a diagnosis. Reporting it is the
    /// honest response, and <see cref="MovementLimitationCoaching.DescribeUnderstanding"/> is where
    /// that happens.
    /// </remarks>
    [Fact]
    public void Uninterpreted_text_blocks_nothing()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("recovering from pneumonia");

        declaration.HasUninterpretedPhrases.ShouldBeTrue();
        declaration.HasRecognisedAreas.ShouldBeFalse();

        MovementLimitationCoaching
            .ContraindicationsFor(declaration, "Quadriceps", MovementPattern.Squat)
            .ShouldBeEmpty();
    }

    [Fact]
    public void An_exercise_with_no_muscle_named_is_not_blocked()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("knee");

        MovementLimitationCoaching
            .ContraindicationsFor(declaration, "  ", MovementPattern.Squat)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Understanding_says_nothing_when_nothing_was_declared()
    {
        MovementLimitationCoaching
            .DescribeUnderstanding(MovementLimitationDeclaration.Empty)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Understanding_names_the_areas_it_read()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("knee");

        MovementLimitationCoaching
            .DescribeUnderstanding(declaration)
            .ShouldBe("Forge is working around your knee.");
    }

    /// <summary>
    /// The user's own words are quoted back, not paraphrased.
    /// </summary>
    /// <remarks>
    /// Paraphrasing would suggest a reading Forge does not have. This wording is shared with the
    /// exercise library so the two screens cannot describe the same failure differently.
    /// </remarks>
    [Fact]
    public void Understanding_quotes_what_it_could_not_read_verbatim()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("recovering from pneumonia");

        var summary = MovementLimitationCoaching.DescribeUnderstanding(declaration);

        summary.ShouldContain("\u201crecovering from pneumonia\u201d");
        summary.ShouldContain("could not interpret");
        summary.ShouldContain("nothing has been left out for that");
    }

    /// <summary>A half-understood declaration says both halves.</summary>
    [Fact]
    public void Understanding_reports_both_what_it_read_and_what_it_did_not()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("knee, recovering from pneumonia");

        var summary = MovementLimitationCoaching.DescribeUnderstanding(declaration);

        summary.ShouldContain("working around your knee");
        summary.ShouldContain("\u201crecovering from pneumonia\u201d");
    }

    /// <summary>
    /// A blocked recommendation names the area the user declared, never a muscle they did not.
    /// </summary>
    /// <remarks>
    /// Without this the screen reads "Forge will not recommend training Quadriceps because the
    /// profile flags Quadriceps as injured", which is a claim nobody made, on the one screen where
    /// honesty is the entire point.
    /// </remarks>
    [Fact]
    public void A_blocked_recommendation_names_the_declared_area()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("knee");
        var contraindications = MovementLimitationCoaching.ContraindicationsFor(declaration, "Quadriceps", MovementPattern.Squat);

        var recommendation = NextSessionRecommender.Recommend(new NextSessionRecommendationRequest(
            Guid.CreateVersion7(),
            "Back squat",
            "Quadriceps",
            [],
            Mass.FromKilograms(100m),
            5,
            8,
            3,
            [],
            Contraindications: contraindications));

        recommendation.Status.ShouldBe(NextSessionRecommendationStatus.BlockedBySafety);
        recommendation.Explanation.ShouldContain("work around your knee");
        recommendation.Explanation.ShouldNotContain("flags Quadriceps as injured");
        recommendation.IsOverridable.ShouldBeTrue();
    }

    /// <summary>
    /// A contraindication with no declared area keeps the original sentence exactly.
    /// </summary>
    /// <remarks>
    /// <c>DeclaredArea</c> defaults to <see langword="null"/> so that every existing caller and its
    /// tests are unaffected by the new branch.
    /// </remarks>
    [Fact]
    public void A_contraindication_without_a_declared_area_keeps_the_original_wording()
    {
        var recommendation = NextSessionRecommender.Recommend(new NextSessionRecommendationRequest(
            Guid.CreateVersion7(),
            "Back squat",
            "Quadriceps",
            [],
            Mass.FromKilograms(100m),
            5,
            8,
            3,
            [],
            Contraindications: [new TrainingContraindication("Quadriceps", "knee pain flare")]));

        recommendation.Status.ShouldBe(NextSessionRecommendationStatus.BlockedBySafety);
        recommendation.Explanation.ShouldContain("the profile flags Quadriceps as injured");
    }
}
