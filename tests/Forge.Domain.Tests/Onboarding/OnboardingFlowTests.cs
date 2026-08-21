using Forge.Domain.Onboarding;
using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Onboarding;

public sealed class OnboardingFlowTests
{
    [Fact]
    public void Flow_reports_six_ordered_steps_ending_at_review()
    {
        OnboardingFlow.StepCount.ShouldBe(6);
        OnboardingFlow.Steps[0].ShouldBe(OnboardingStep.Goal);
        OnboardingFlow.Steps[^1].ShouldBe(OnboardingStep.Review);
    }

    [Fact]
    public void Progress_runs_from_first_step_to_full_at_review()
    {
        OnboardingFlow.ProgressAt(OnboardingStep.Goal).ShouldBe(1d / 6d, 0.0001d);
        OnboardingFlow.ProgressAt(OnboardingStep.Review).ShouldBe(1d);
    }

    [Fact]
    public void First_step_has_no_previous_and_review_has_no_next()
    {
        OnboardingFlow.Previous(OnboardingStep.Goal).ShouldBeNull();
        OnboardingFlow.Next(OnboardingStep.Review).ShouldBeNull();
        OnboardingFlow.Next(OnboardingStep.Goal).ShouldBe(OnboardingStep.BodyMetrics);
        OnboardingFlow.Previous(OnboardingStep.BodyMetrics).ShouldBe(OnboardingStep.Goal);
    }

    [Fact]
    public void Goal_step_requires_a_goal_to_be_chosen()
    {
        var answers = CompleteAnswers();
        answers.Goal = FitnessGoal.Unspecified;

        var validation = OnboardingFlow.Validate(OnboardingStep.Goal, answers);

        validation.IsValid.ShouldBeFalse();
        validation.Issues.ShouldContain(issue => issue.Field == OnboardingField.Goal);
    }

    [Fact]
    public void Goal_step_requires_a_target_weight_only_for_weight_goals()
    {
        var strength = CompleteAnswers();
        strength.Goal = FitnessGoal.BuildStrength;
        strength.TargetWeightKilograms = 0;
        strength.TimeframeWeeks = 0;

        OnboardingFlow.Validate(OnboardingStep.Goal, strength).IsValid.ShouldBeTrue();

        var loseWeight = CompleteAnswers();
        loseWeight.Goal = FitnessGoal.LoseWeight;
        loseWeight.TargetWeightKilograms = 0;

        OnboardingFlow.Validate(OnboardingStep.Goal, loseWeight)
            .Issues.ShouldContain(issue => issue.Field == OnboardingField.TargetWeight);
    }

    [Fact]
    public void Goal_step_requires_at_least_one_week_so_a_rate_can_be_calculated()
    {
        var answers = CompleteAnswers();
        answers.TimeframeWeeks = 0;

        var validation = OnboardingFlow.Validate(OnboardingStep.Goal, answers);

        validation.Issues.ShouldContain(issue => issue.Field == OnboardingField.Timeframe);
    }

    [Fact]
    public void Body_metrics_step_requires_weight_and_height()
    {
        var answers = CompleteAnswers();
        answers.CurrentWeightKilograms = 0;
        answers.HeightCentimetres = 0;

        var validation = OnboardingFlow.Validate(OnboardingStep.BodyMetrics, answers);

        validation.Issues.ShouldContain(issue => issue.Field == OnboardingField.CurrentWeight);
        validation.Issues.ShouldContain(issue => issue.Field == OnboardingField.Height);
    }

    [Fact]
    public void Height_entered_in_inches_is_explained_as_a_unit_mix_up()
    {
        var answers = CompleteAnswers();
        answers.HeightCentimetres = 70;

        var issue = OnboardingFlow.Validate(OnboardingStep.BodyMetrics, answers)
            .Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe(OnboardingField.Height);
        issue.Message.ShouldContain("inches");
        issue.Message.ShouldContain("cm");
    }

    [Fact]
    public void Future_date_of_birth_is_explained_without_blaming_the_user()
    {
        var answers = CompleteAnswers();
        answers.DateOfBirth = DateOnly.FromDateTime(DateTime.Today).AddDays(1);

        var issue = OnboardingFlow.Validate(OnboardingStep.BodyMetrics, answers)
            .Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe(OnboardingField.DateOfBirth);
        issue.Message.ShouldContain("future");
    }

    [Fact]
    public void Date_of_birth_stays_optional()
    {
        var answers = CompleteAnswers();
        answers.DateOfBirth = null;

        OnboardingFlow.Validate(OnboardingStep.BodyMetrics, answers).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Experience_step_requires_a_level()
    {
        var answers = CompleteAnswers();
        answers.ExperienceLevel = TrainingExperienceLevel.Unspecified;

        OnboardingFlow.Validate(OnboardingStep.Experience, answers)
            .Issues.ShouldContain(issue => issue.Field == OnboardingField.Experience);
    }

    [Fact]
    public void Equipment_step_requires_at_least_one_option()
    {
        var answers = CompleteAnswers();
        answers.AvailableEquipment.Clear();

        OnboardingFlow.Validate(OnboardingStep.Equipment, answers)
            .Issues.ShouldContain(issue => issue.Field == OnboardingField.Equipment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void Availability_step_rejects_impossible_week_shapes(double days)
    {
        var answers = CompleteAnswers();
        answers.TrainingDaysPerWeek = days;

        OnboardingFlow.Validate(OnboardingStep.Availability, answers)
            .Issues.ShouldContain(issue => issue.Field == OnboardingField.TrainingDays);
    }

    [Fact]
    public void Review_step_repeats_every_outstanding_issue()
    {
        var answers = new OnboardingAnswers();

        var validation = OnboardingFlow.Validate(OnboardingStep.Review, answers);

        validation.IsValid.ShouldBeFalse();
        validation.Issues.Select(issue => issue.Field).ShouldContain(OnboardingField.Goal);
        validation.Issues.Select(issue => issue.Field).ShouldContain(OnboardingField.CurrentWeight);
        validation.Issues.Select(issue => issue.Field).ShouldContain(OnboardingField.Experience);
        validation.Summarise().ShouldNotBeEmpty();
    }

    [Fact]
    public void Complete_answers_pass_every_step()
    {
        var answers = CompleteAnswers();

        foreach (var step in OnboardingFlow.Steps)
        {
            OnboardingFlow.Validate(step, answers).IsValid.ShouldBeTrue($"step {step} should be valid");
        }
    }

    [Fact]
    public void Resuming_lands_on_the_first_step_that_still_needs_something()
    {
        var answers = CompleteAnswers();
        answers.ExperienceLevel = TrainingExperienceLevel.Unspecified;

        OnboardingFlow.FirstIncompleteStep(answers).ShouldBe(OnboardingStep.Experience);
    }

    [Fact]
    public void Resuming_a_complete_draft_lands_on_review()
    {
        OnboardingFlow.FirstIncompleteStep(CompleteAnswers()).ShouldBe(OnboardingStep.Review);
    }

    [Fact]
    public void Safety_proposal_is_unavailable_until_height_and_weight_exist()
    {
        var answers = new OnboardingAnswers();

        OnboardingFlow.CreateSafetyProposal(answers).ShouldBeNull();
    }

    [Fact]
    public void Unset_energy_target_is_not_proposed_as_zero_kilocalories()
    {
        var answers = CompleteAnswers();
        answers.TargetDailyCalories = 0;

        var proposal = OnboardingFlow.CreateSafetyProposal(answers).ShouldNotBeNull();

        proposal.TargetDailyCalories.ShouldBeNull();
        GoalSafetyEvaluator.Evaluate(proposal).IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public void Strength_goal_proposes_no_weight_target()
    {
        var answers = CompleteAnswers();
        answers.Goal = FitnessGoal.BuildStrength;
        answers.TargetWeightKilograms = 60;

        var proposal = OnboardingFlow.CreateSafetyProposal(answers).ShouldNotBeNull();

        proposal.TargetWeight.ShouldBeNull();
        proposal.TimeframeWeeks.ShouldBeNull();
    }

    [Fact]
    public void An_energy_floor_refusal_sends_the_user_to_the_body_metrics_step()
    {
        var answers = CompleteAnswers();
        answers.BiologicalSex = BiologicalSex.Male;
        answers.TargetDailyCalories = 900;

        OnboardingFlow.StepForRefusal(answers).ShouldBe(OnboardingStep.BodyMetrics);
    }

    [Fact]
    public void A_pace_refusal_sends_the_user_to_the_goal_step_that_owns_the_target()
    {
        var answers = CompleteAnswers();
        answers.TargetWeightKilograms = 60;
        answers.TimeframeWeeks = 4;

        OnboardingFlow.StepForRefusal(answers).ShouldBe(OnboardingStep.Goal);
    }

    [Fact]
    public void A_refusal_with_no_usable_body_metrics_sends_the_user_to_collect_them()
    {
        OnboardingFlow.StepForRefusal(new OnboardingAnswers()).ShouldBe(OnboardingStep.BodyMetrics);
    }

    [Fact]
    public void Validation_messages_explain_rather_than_scold()
    {
        var copy = string.Join(
            ' ',
            OnboardingFlow.Validate(OnboardingStep.Review, new OnboardingAnswers()).Issues.Select(issue => issue.Message));

        copy.ShouldNotContain("invalid", Case.Insensitive);
        copy.ShouldNotContain("error", Case.Insensitive);
        copy.ShouldNotContain("must ", Case.Insensitive);
        copy.ShouldNotContain("failed", Case.Insensitive);
        copy.ShouldNotContain("required", Case.Insensitive);
    }

    [Fact]
    public void Every_step_carries_a_title_and_an_explanation()
    {
        foreach (var step in OnboardingFlow.Steps)
        {
            OnboardingFlow.TitleOf(step).ShouldNotBeNullOrWhiteSpace();
            OnboardingFlow.DescriptionOf(step).ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Cloning_a_draft_does_not_share_the_equipment_list()
    {
        var answers = CompleteAnswers();
        var clone = answers.Clone();

        clone.AvailableEquipment.Add("Bands");

        answers.AvailableEquipment.ShouldNotContain("Bands");
        clone.DisplayName.ShouldBe(answers.DisplayName);
    }

    private static OnboardingAnswers CompleteAnswers() => new()
    {
        DisplayName = "Sam",
        Goal = FitnessGoal.LoseWeight,
        TargetWeightKilograms = 78,
        TimeframeWeeks = 12,
        CurrentWeightKilograms = 82,
        HeightCentimetres = 178,
        TargetDailyCalories = 2000,
        DateOfBirth = new DateOnly(1990, 5, 4),
        BiologicalSex = BiologicalSex.PreferNotToSay,
        ExperienceLevel = TrainingExperienceLevel.Beginner,
        AvailableEquipment = { "Bodyweight" },
        TrainingDaysPerWeek = 3,
    };
}
