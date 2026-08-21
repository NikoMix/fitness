using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Profile;

/// <summary>
/// Profile names on a shared device. The name is the only thing distinguishing two people before a
/// set is logged against one of them, so it is validated rather than merely stored.
/// </summary>
public sealed class ProfileNameRulesTests
{
    [Fact]
    public void Whitespace_is_trimmed_and_collapsed()
    {
        ProfileNameRules.Normalise("  Avery   Quinn  ").ShouldBe("Avery Quinn");
        ProfileNameRules.Normalise("\tAvery\n").ShouldBe("Avery");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_name_is_refused(string? name)
    {
        var result = ProfileNameRules.Validate(name, []);

        result.IsAccepted.ShouldBeFalse();
        result.Problem.ShouldNotBeNullOrWhiteSpace();
        result.Name.ShouldBeEmpty();
    }

    [Fact]
    public void A_name_longer_than_the_column_is_refused_rather_than_truncated()
    {
        var result = ProfileNameRules.Validate(new string('a', ProfileNameRules.MaximumLength + 1), []);

        result.IsAccepted.ShouldBeFalse();
        result.Problem.ShouldContain(ProfileNameRules.MaximumLength.ToString(System.Globalization.CultureInfo.CurrentCulture));
    }

    [Fact]
    public void A_name_at_the_limit_is_accepted()
    {
        ProfileNameRules.Validate(new string('a', ProfileNameRules.MaximumLength), []).IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public void A_duplicate_name_is_refused_regardless_of_case()
    {
        // Two profiles called "Alex" is not an aesthetic problem: it is how somebody taps the wrong
        // row and ends up with a training history that is not theirs.
        var existing = new[] { new UserProfile { DisplayName = "Alex" } };

        var result = ProfileNameRules.Validate("  alex ", existing);

        result.IsAccepted.ShouldBeFalse();
        result.Problem.ShouldContain("already has a profile");
    }

    [Fact]
    public void A_profile_may_keep_its_own_name_while_being_renamed()
    {
        var existing = new UserProfile { DisplayName = "Alex" };

        ProfileNameRules.Validate("Alex", [existing], existing.Id).IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public void A_deleted_profile_does_not_reserve_its_name()
    {
        var deleted = new UserProfile { DisplayName = "Alex", DeletedUtc = DateTimeOffset.UtcNow };

        ProfileNameRules.Validate("Alex", [deleted]).IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public void An_accepted_name_comes_back_normalised_so_the_stored_value_matches_the_check()
    {
        var result = ProfileNameRules.Validate("  Avery   Quinn ", []);

        result.IsAccepted.ShouldBeTrue();
        result.Name.ShouldBe("Avery Quinn");
        result.Problem.ShouldBeEmpty();
    }

    [Fact]
    public void Validating_against_nothing_throws_rather_than_accepting_a_clash()
    {
        Should.Throw<ArgumentNullException>(() => ProfileNameRules.Validate("Avery", null!));
    }
}
