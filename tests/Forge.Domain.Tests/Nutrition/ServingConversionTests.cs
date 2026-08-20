using Forge.Domain.Measurement;
using Forge.Domain.Nutrition;
using Shouldly;

namespace Forge.Domain.Tests.Nutrition;

public sealed class ServingConversionTests
{
    private static ServingConversion CreateConverter() => new(
    [
        new ServingDefinition { Name = "100 g", Mass = Mass.FromKilograms(0.100m) },
        new ServingDefinition { Name = "1 cup", Mass = Mass.FromKilograms(0.240m), Volume = Volume.FromMillilitres(240m) },
        new ServingDefinition { Name = "1 serving", Mass = Mass.FromKilograms(0.050m) },
        new ServingDefinition { Name = "2 servings", Mass = Mass.FromKilograms(0.100m) },
    ]);

    [Fact]
    public void Cup_to_grams_to_servings_round_trips_through_canonical_grams()
    {
        var converter = CreateConverter();

        var servings = converter.Convert(1m, "1 cup", "1 serving");
        var grams = converter.ToGrams("1 serving", servings);
        var cups = converter.FromGrams(grams, "1 cup");

        servings.ShouldBe(4.8m);
        grams.ShouldBe(240m);
        cups.ShouldBe(1m, tolerance: 0.000001m);
    }

    [Fact]
    public void Hundred_grams_to_two_servings_and_back_is_stable()
    {
        var converter = CreateConverter();

        var twoServingCount = converter.Convert(1m, "100 g", "2 servings");
        var grams = converter.ToGrams("2 servings", twoServingCount);

        twoServingCount.ShouldBe(1m);
        grams.ShouldBe(100m);
    }

    [Fact]
    public void Unknown_serving_is_rejected_instead_of_guessing()
    {
        Should.Throw<KeyNotFoundException>(() => CreateConverter().ToGrams("bowl", 1m));
    }

    [Fact]
    public void Negative_quantities_are_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CreateConverter().ToGrams("1 cup", -1m));
        Should.Throw<ArgumentOutOfRangeException>(() => CreateConverter().FromGrams(-1m, "1 cup"));
    }
}
