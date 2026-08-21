using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Training;

public sealed class EquipmentAvailabilityTests
{
    [Fact]
    public void Bodyweight_is_always_available()
    {
        var availability = EquipmentAvailability.From(["Dumbbell"]);

        availability.Allows("Bodyweight").ShouldBeTrue();
        availability.Allows((string?)null).ShouldBeTrue();
        availability.Allows(TestExercise.Create("Push Up", equipment: null)).ShouldBeTrue();
    }

    [Fact]
    public void Matching_tolerates_casing_and_surrounding_whitespace()
    {
        var availability = EquipmentAvailability.From(["  resistance BAND "]);

        availability.Allows("Resistance band").ShouldBeTrue();
        availability.Allows("Cable").ShouldBeFalse();
    }

    [Fact]
    public void A_declaration_is_split_on_every_separator_a_user_might_type()
    {
        var availability = EquipmentAvailability.FromDeclaration("Dumbbell, Kettlebell; Pull-up bar | Bench");

        availability.Allows("Dumbbell").ShouldBeTrue();
        availability.Allows("Kettlebell").ShouldBeTrue();
        availability.Allows("Pull-up bar").ShouldBeTrue();
        availability.Allows("Bench").ShouldBeTrue();
        availability.HasEquipment.ShouldBeTrue();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("No equipment")]
    [InlineData("body weight")]
    [InlineData("Nothing")]
    public void Free_text_ways_of_saying_no_equipment_do_not_become_imaginary_equipment(string declaration)
    {
        var availability = EquipmentAvailability.FromDeclaration(declaration);

        availability.HasEquipment.ShouldBeFalse();
        availability.Items.ShouldBe(["Bodyweight"]);
    }

    [Fact]
    public void An_empty_declaration_falls_back_to_bodyweight_only()
    {
        EquipmentAvailability.FromDeclaration(null).HasEquipment.ShouldBeFalse();
        EquipmentAvailability.FromDeclaration("   ").Allows("Barbell").ShouldBeFalse();
    }

    [Fact]
    public void Adding_equipment_leaves_the_original_set_untouched()
    {
        var original = EquipmentAvailability.BodyweightOnly;

        var extended = original.With("Cable");

        extended.Allows("Cable").ShouldBeTrue();
        original.Allows("Cable").ShouldBeFalse();
    }
}
