using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Training;

/// <summary>
/// Covers the bridge between what a person types during onboarding and what the filter knows.
/// </summary>
/// <remarks>
/// The half worth guarding is the failure half. Recognising "knee" is easy; the property that
/// matters is that anything Forge cannot place comes back out again, because a screen that stays
/// silent about it tells someone their injury was accounted for when it was not.
/// </remarks>
public sealed class MovementLimitationDeclarationTests
{
    [Fact]
    public void Nothing_declared_reads_as_nothing()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("   ");

        declaration.IsEmpty.ShouldBeTrue();
        declaration.HasRecognisedAreas.ShouldBeFalse();
        declaration.HasUninterpretedPhrases.ShouldBeFalse();
        declaration.ExcludedMovements.ShouldBeEmpty();
    }

    [Fact]
    public void A_bare_area_name_is_recognised()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("knee");

        declaration.RecognisedAreas.ShouldBe(["knee"]);
        declaration.ExcludedMovements.ShouldContain(MovementPattern.Squat);
        declaration.ExcludedMovements.ShouldContain(MovementPattern.Lunge);
        declaration.HasUninterpretedPhrases.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Left knee", "knee")]
    [InlineData("both knees", "knee")]
    [InlineData("sore shoulders", "shoulder")]
    [InlineData("dodgy WRISTS", "wrist")]
    [InlineData("lower-back pain", "lower back")]
    public void Casing_plurals_hyphens_and_surrounding_words_do_not_hide_an_area(string text, string expected)
        => MovementLimitationDeclaration.FromDeclaration(text).RecognisedAreas.ShouldBe([expected]);

    [Fact]
    public void A_synonym_naming_the_same_region_is_accepted()
    {
        MovementLimitationDeclaration.FromDeclaration("rotator cuff tear").RecognisedAreas.ShouldBe(["shoulder"]);
        MovementLimitationDeclaration.FromDeclaration("lumbar disc").RecognisedAreas.ShouldBe(["lower back"]);
        MovementLimitationDeclaration.FromDeclaration("torn ACL").RecognisedAreas.ShouldBe(["knee"]);
    }

    [Fact]
    public void Lower_back_is_not_also_read_as_the_bare_back()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("lower back");

        declaration.RecognisedAreas.ShouldBe(["lower back"]);
    }

    [Fact]
    public void Several_limitations_can_be_separated_however_the_user_chose_to_separate_them()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("knee, left shoulder and neck");

        declaration.RecognisedAreas.ShouldBe(["knee", "shoulder", "neck"], ignoreOrder: true);
        declaration.HasUninterpretedPhrases.ShouldBeFalse();
    }

    [Fact]
    public void The_same_area_written_twice_is_only_reported_once()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("right knee; left knee");

        declaration.RecognisedAreas.ShouldBe(["knee"]);
    }

    [Fact]
    public void Text_Forge_cannot_place_is_handed_back_rather_than_dropped()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("asthma");

        declaration.HasRecognisedAreas.ShouldBeFalse();
        declaration.ExcludedMovements.ShouldBeEmpty();
        declaration.UninterpretedPhrases.ShouldBe(["asthma"]);
        declaration.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void A_partly_understood_declaration_reports_both_halves()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("bad knee, recovering from pneumonia");

        declaration.RecognisedAreas.ShouldBe(["knee"]);
        declaration.UninterpretedPhrases.ShouldBe(["recovering from pneumonia"]);
        declaration.ExcludedMovements.ShouldContain(MovementPattern.Squat);
    }

    [Fact]
    public void Uninterpreted_text_is_quoted_back_exactly_as_it_was_typed()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("  Dodgy Ticker  ");

        declaration.UninterpretedPhrases.ShouldBe(["Dodgy Ticker"]);
    }

    [Fact]
    public void A_muscle_rather_than_a_region_is_left_uninterpreted_rather_than_guessed_at()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("hamstring strain");

        declaration.HasRecognisedAreas.ShouldBeFalse();
        declaration.UninterpretedPhrases.ShouldBe(["hamstring strain"]);
    }

    [Fact]
    public void The_recognised_vocabulary_is_the_filter_s_own()
    {
        foreach (var area in ExerciseFilter.RecognisedInjuryAreas)
        {
            MovementLimitationDeclaration.FromDeclaration(area)
                .RecognisedAreas
                .ShouldContain(area, $"'{area}' is declared by the filter but cannot be read from free text.");
        }
    }

    [Fact]
    public void What_it_recognised_drives_the_same_exclusions_the_filter_would_apply()
    {
        var declaration = MovementLimitationDeclaration.FromDeclaration("shoulder impingement");
        var filter = ExerciseFilter.For(injuries: declaration.RecognisedAreas);

        filter.ExcludedMovements.ShouldBe(declaration.ExcludedMovements, ignoreOrder: true);
        filter.Matches(TestExercise.Create("Push Up", MovementPattern.Push)).ShouldBeFalse();
        filter.Matches(TestExercise.Create("Goblet Squat", MovementPattern.Squat)).ShouldBeTrue();
    }
}
