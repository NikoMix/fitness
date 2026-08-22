using Forge.App.Composition;
using Forge.App.Features.Profile;
using Forge.Core.Abstractions.Data;
using Forge.Domain.Planning;
using Forge.Domain.Profile;

namespace Forge.App.Features.Plans;

public interface IPlanPersistenceService
{
    Task<IReadOnlyList<TrainingPlan>> ListUserPlansAsync(CancellationToken cancellationToken);

    Task<TrainingPlan?> GetPlanAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Creates an empty plan owned by the active profile, without saving it.</summary>
    /// <param name="cancellationToken">Cancels the read that resolves the owner.</param>
    /// <returns>An unsaved plan the editor can populate.</returns>
    /// <remarks>
    /// The editor asks for a draft rather than constructing one itself so that the profile boundary
    /// stays in the persistence layer. A view model that had to know the active profile would end
    /// up resolving it on a different schedule from the service that saves its work.
    /// </remarks>
    Task<TrainingPlan> CreateDraftPlanAsync(CancellationToken cancellationToken);

    Task SavePlanAsync(TrainingPlan plan, CancellationToken cancellationToken);

    Task<TrainingPlan> AdoptTemplateAsync(TrainingPlan source, CancellationToken cancellationToken);

    Task DeletePlanAsync(Guid id, CancellationToken cancellationToken);
}

internal sealed class PlanPersistenceService(ForgeStartupService startup, IDataSessionFactory sessions, ProfileStore profiles) : IPlanPersistenceService
{
    public async Task<IReadOnlyList<TrainingPlan>> ListUserPlansAsync(CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken).ConfigureAwait(false);
        await using var session = sessions.Create();
        var plans = session.Repository<TrainingPlan>();

        return (await plans.ListAsync(cancellationToken).ConfigureAwait(false))
            .OwnedBy(scope)
            .Where(plan => !plan.IsTemplate)
            .OrderByDescending(plan => plan.IsActive)
            .ThenBy(plan => plan.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<TrainingPlan?> GetPlanAsync(Guid id, CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken).ConfigureAwait(false);
        await using var session = sessions.Create();
        var plans = session.Repository<TrainingPlan>();

        // Fetched by identifier and then checked for ownership, rather than trusted because the
        // caller had the identifier. A plan id can outlive a profile switch in a navigation
        // parameter, and opening it afterwards would otherwise show another profile's programme.
        var plan = await plans.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return plan is not null && scope.Owns(plan) ? plan : null;
    }

    public async Task<TrainingPlan> CreateDraftPlanAsync(CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken).ConfigureAwait(false);
        return new TrainingPlan
        {
            UserProfileId = scope.ProfileId,
            Name = "My plan",
            Description = "A custom training plan.",
            IsActive = true
        };
    }

    public async Task SavePlanAsync(TrainingPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var scope = await ResolveScopeAsync(cancellationToken).ConfigureAwait(false);
        await using var session = sessions.Create();
        var plans = session.Repository<TrainingPlan>();

        // Only this profile's plans are considered when deciding which one is active. Without the
        // scope, activating a plan would deactivate everybody else's on a shared device.
        var existingPlans = (await plans.ListAsync(cancellationToken).ConfigureAwait(false)).OwnedBy(scope).ToList();
        plan.IsTemplate = false;
        if (plan.IsActive || existingPlans.TrueForAll(existing => existing.IsTemplate || existing.Id == plan.Id))
        {
            plan.IsActive = true;
            foreach (var existing in existingPlans.Where(existing => !existing.IsTemplate && existing.Id != plan.Id && existing.IsActive))
            {
                existing.IsActive = false;
                await plans.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
            }
        }

        if (existingPlans.Exists(existing => existing.Id == plan.Id))
        {
            await plans.UpdateAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await plans.AddAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrainingPlan> AdoptTemplateAsync(TrainingPlan source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var scope = await ResolveScopeAsync(cancellationToken).ConfigureAwait(false);
        var adoptedPlan = source.CreateEditableCopy(scope.ProfileId);
        await SavePlanAsync(adoptedPlan, cancellationToken).ConfigureAwait(false);
        return adoptedPlan;
    }

    public async Task DeletePlanAsync(Guid id, CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken).ConfigureAwait(false);
        await using var session = sessions.Create();
        var plans = session.Repository<TrainingPlan>();
        var days = session.Repository<PlanDay>();
        var exercises = session.Repository<PlannedExercise>();
        var sets = session.Repository<PlannedSet>();

        var plan = await plans.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (plan is null || !scope.Owns(plan))
        {
            return;
        }

        foreach (var set in plan.Days.SelectMany(day => day.Exercises).SelectMany(exercise => exercise.Sets))
        {
            await sets.SoftDeleteAsync(set.Id, cancellationToken).ConfigureAwait(false);
        }

        foreach (var exercise in plan.Days.SelectMany(day => day.Exercises))
        {
            await exercises.SoftDeleteAsync(exercise.Id, cancellationToken).ConfigureAwait(false);
        }

        foreach (var day in plan.Days)
        {
            await days.SoftDeleteAsync(day.Id, cancellationToken).ConfigureAwait(false);
        }

        await plans.SoftDeleteAsync(plan.Id, cancellationToken).ConfigureAwait(false);
        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProfileScope> ResolveScopeAsync(CancellationToken cancellationToken)
    {
        await EnsureDatabaseReadyAsync(cancellationToken).ConfigureAwait(false);
        return await profiles.GetActiveScopeAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureDatabaseReadyAsync(CancellationToken cancellationToken)
    {
        await startup.InitialiseAsync(cancellationToken).ConfigureAwait(false);
        if (!startup.Succeeded)
        {
            throw new InvalidOperationException("Forge database startup did not complete.", startup.Failure);
        }
    }
}
