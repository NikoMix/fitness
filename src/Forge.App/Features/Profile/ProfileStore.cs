using Forge.App.Composition;
using Forge.Core.Abstractions.Data;
using Forge.Domain.Measurement;
using Forge.Domain.Profile;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Profile;

// ForgeStartupService is internal and this type is public, so it is resolved from the provider
// rather than injected: a public constructor cannot expose an internal parameter type.
public sealed class ProfileStore(IServiceProvider services, IDataSessionFactory sessions)
{
    public async Task<bool> HasProfileAsync(CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return snapshot is not null;
    }

    public async Task<ProfileSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var profiles = session.Repository<UserProfile>();
        var metrics = session.Repository<BodyMetric>();

        var profile = (await profiles.ListAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(profile => profile.CreatedUtc)
            .FirstOrDefault();

        if (profile is null)
        {
            return null;
        }

        var bodyMetrics = (await metrics.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(metric => metric.UserProfileId == profile.Id)
            .OrderByDescending(metric => metric.RecordedUtc)
            .ToArray();

        return new ProfileSnapshot(profile, bodyMetrics);
    }

    public async Task<UserProfile> EnsureDefaultProfileAsync(CancellationToken cancellationToken)
    {
        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var profiles = session.Repository<UserProfile>();

        var profile = (await profiles.ListAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(profile => profile.CreatedUtc)
            .FirstOrDefault();

        if (profile is not null)
        {
            return profile;
        }

        profile = new UserProfile
        {
            DisplayName = "Me",
            ExperienceLevel = TrainingExperienceLevel.Unspecified,
            Goal = FitnessGoal.Maintain,
            AvailableEquipment = "Bodyweight",
            TrainingDaysPerWeek = 3,
        };

        await profiles.AddAsync(profile, cancellationToken).ConfigureAwait(false);
        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return profile;
    }

    public async Task<GoalSafetyResult> SaveSetupAsync(ProfileSetupDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var profiles = session.Repository<UserProfile>();
        var metrics = session.Repository<BodyMetric>();

        var profile = (await profiles.ListAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(profile => profile.CreatedUtc)
            .FirstOrDefault();

        var isNew = profile is null;
        profile ??= new UserProfile { DisplayName = draft.DisplayName };

        profile.DisplayName = draft.DisplayName;
        profile.DateOfBirth = draft.DateOfBirth;
        profile.BiologicalSex = draft.BiologicalSex;
        profile.Height = draft.Height;
        profile.ExperienceLevel = draft.ExperienceLevel;
        profile.Goal = draft.Goal;
        profile.TargetWeight = draft.TargetWeight;
        profile.GoalTimeframeWeeks = draft.GoalTimeframeWeeks;
        profile.TargetDailyCalories = draft.TargetDailyCalories;
        profile.AvailableEquipment = string.Join(", ", draft.AvailableEquipment);
        profile.MovementLimitations = draft.MovementLimitations;
        profile.TrainingDaysPerWeek = draft.TrainingDaysPerWeek;

        var bodyMetric = new BodyMetric
        {
            UserProfileId = profile.Id,
            RecordedUtc = DateTimeOffset.UtcNow,
            Weight = draft.CurrentWeight,
        };

        var safety = GoalSafetyEvaluator.Evaluate(profile.CreateSafetyProposal(bodyMetric));
        if (!safety.IsAccepted)
        {
            return safety;
        }

        if (isNew)
        {
            await profiles.AddAsync(profile, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await profiles.UpdateAsync(profile, cancellationToken).ConfigureAwait(false);
        }

        await metrics.AddAsync(bodyMetric, cancellationToken).ConfigureAwait(false);
        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return safety;
    }

    private async Task EnsureStartupAsync(CancellationToken cancellationToken)
    {
        var startup = services.GetRequiredService<ForgeStartupService>();
        await startup.InitialiseAsync(cancellationToken).ConfigureAwait(false);

        if (!startup.Succeeded)
        {
            throw new InvalidOperationException("Forge startup did not complete successfully.", startup.Failure);
        }
    }
}

public sealed record ProfileSnapshot(UserProfile Profile, IReadOnlyList<BodyMetric> BodyMetrics);

public sealed record ProfileSetupDraft(
    string DisplayName,
    DateOnly DateOfBirth,
    BiologicalSex BiologicalSex,
    Length Height,
    Mass CurrentWeight,
    FitnessGoal Goal,
    Mass TargetWeight,
    int GoalTimeframeWeeks,
    decimal TargetDailyCalories,
    TrainingExperienceLevel ExperienceLevel,
    IReadOnlyList<string> AvailableEquipment,
    string MovementLimitations,
    int TrainingDaysPerWeek);
