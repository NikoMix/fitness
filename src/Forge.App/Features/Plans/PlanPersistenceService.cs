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

    /// <summary>Returns the profile's active programme, or <see langword="null"/> when it has none.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The active plan, or <see langword="null"/>.</returns>
    Task<TrainingPlan?> GetActivePlanAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finds one plan day, confined to the profile that owns it.
    /// </summary>
    /// <remarks>
    /// Looked up through the owning plan rather than by day identifier alone. A day identifier
    /// travels in a navigation parameter and can outlive a profile switch, and starting a workout
    /// from another profile's plan day would attribute their programme to somebody else.
    /// </remarks>
    /// <param name="planDayId">The day to find.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The day and its plan, or <see langword="null"/> when the profile does not own it.</returns>
    Task<PlanDayLookup?> GetPlanDayAsync(Guid planDayId, CancellationToken cancellationToken);

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

/// <summary>One plan day together with the plan it belongs to.</summary>
/// <param name="Plan">The owning plan.</param>
/// <param name="Day">The day to execute.</param>
public sealed record PlanDayLookup(TrainingPlan Plan, PlanDay Day);

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

    public async Task<TrainingPlan?> GetActivePlanAsync(CancellationToken cancellationToken)
    {
        var plans = await ListUserPlansAsync(cancellationToken).ConfigureAwait(false);

        // The list is already ordered active-first, so the second lookup is only reached when the
        // profile has plans but has activated none of them. Offering one is better than offering
        // nothing: a user with a single unactivated plan still wants to train from it.
        return plans.FirstOrDefault(plan => plan.IsActive) ?? (plans.Count > 0 ? plans[0] : null);
    }

    public async Task<PlanDayLookup?> GetPlanDayAsync(Guid planDayId, CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken).ConfigureAwait(false);
        await using var session = sessions.Create();
        var plans = session.Repository<TrainingPlan>();

        var owned = (await plans.ListAsync(cancellationToken).ConfigureAwait(false)).OwnedBy(scope).ToList();
        foreach (var plan in owned)
        {
            // The day is matched against days reached through an owned plan, and its own owner is
            // checked as well. Either check alone would be enough today; together they survive a
            // plan whose days were stamped before the profile boundary existed.
            var day = plan.Days.FirstOrDefault(candidate => candidate.Id == planDayId && scope.Owns(candidate));
            if (day is not null)
            {
                return new PlanDayLookup(plan, day);
            }
        }

        return null;
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
