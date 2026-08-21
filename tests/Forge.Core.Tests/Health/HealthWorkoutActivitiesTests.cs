using Forge.Core.Abstractions.Health;
using Shouldly;

namespace Forge.Core.Tests.Health;

/// <summary>
/// Pins the canonical workout vocabulary. Both platform mappers switch on these tokens, so a
/// change here silently changes what Apple Health and Health Connect record a Forge session as.
/// </summary>
public sealed class HealthWorkoutActivitiesTests
{
    [Theory]
    [InlineData("strength")]
    [InlineData("Strength Training")]
    [InlineData("strength-training")]
    [InlineData("STRENGTH_TRAINING")]
    [InlineData("weights")]
    [InlineData("Weightlifting")]
    [InlineData("  lifting  ")]
    public void Strength_synonyms_normalise_to_one_token(string input)
    {
        HealthWorkoutActivities.Normalise(input).ShouldBe(HealthWorkoutActivities.StrengthTraining);
    }

    [Theory]
    [InlineData("run", HealthWorkoutActivities.Running)]
    [InlineData("Jogging", HealthWorkoutActivities.Running)]
    [InlineData("treadmill", HealthWorkoutActivities.Running)]
    [InlineData("walk", HealthWorkoutActivities.Walking)]
    [InlineData("Hiking", HealthWorkoutActivities.Walking)]
    [InlineData("bike", HealthWorkoutActivities.Cycling)]
    [InlineData("Spinning", HealthWorkoutActivities.Cycling)]
    [InlineData("erg", HealthWorkoutActivities.Rowing)]
    [InlineData("Swim", HealthWorkoutActivities.Swimming)]
    [InlineData("HIIT", HealthWorkoutActivities.HighIntensityIntervalTraining)]
    [InlineData("circuits", HealthWorkoutActivities.HighIntensityIntervalTraining)]
    [InlineData("metcon", HealthWorkoutActivities.HighIntensityIntervalTraining)]
    [InlineData("Yoga", HealthWorkoutActivities.Yoga)]
    [InlineData("mobility", HealthWorkoutActivities.Stretching)]
    [InlineData("cool down", HealthWorkoutActivities.Stretching)]
    [InlineData("bodyweight", HealthWorkoutActivities.Calisthenics)]
    public void Common_names_map_to_the_expected_token(string input, string expected)
    {
        HealthWorkoutActivities.Normalise(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("underwater basket weaving")]
    public void Unrecognised_input_falls_back_to_other(string? input)
    {
        // A session must always be exportable. Throwing here would mean a user could not send a
        // workout to their health store because they gave it an unusual name.
        HealthWorkoutActivities.Normalise(input).ShouldBe(HealthWorkoutActivities.Other);
    }

    [Fact]
    public void Normalising_a_canonical_token_is_idempotent()
    {
        string[] canonical =
        [
            HealthWorkoutActivities.StrengthTraining,
            HealthWorkoutActivities.Calisthenics,
            HealthWorkoutActivities.Running,
            HealthWorkoutActivities.Walking,
            HealthWorkoutActivities.Cycling,
            HealthWorkoutActivities.Rowing,
            HealthWorkoutActivities.Swimming,
            HealthWorkoutActivities.HighIntensityIntervalTraining,
            HealthWorkoutActivities.Yoga,
            HealthWorkoutActivities.Stretching,
            HealthWorkoutActivities.Other
        ];

        foreach (var token in canonical)
        {
            HealthWorkoutActivities.Normalise(token).ShouldBe(token);
        }
    }

    [Fact]
    public void Punctuation_and_casing_are_irrelevant()
    {
        HealthWorkoutActivities.Normalise("High-Intensity Interval Training")
            .ShouldBe(HealthWorkoutActivities.HighIntensityIntervalTraining);
        HealthWorkoutActivities.Normalise("Rowing Machine").ShouldBe(HealthWorkoutActivities.Other);
    }
}
