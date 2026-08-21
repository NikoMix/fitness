using Forge.App.Composition;
using Forge.Core.Abstractions.Data;
using Forge.Domain.Planning;

namespace Forge.App.Features.Plans;

public interface IPlanPersistenceService
{
    Task<IReadOnlyList<TrainingPlan>> ListUserPlansAsync(CancellationToken cancellationToken);

    Task<TrainingPlan?> GetPlanAsync(Guid id, CancellationToken cancellationToken);

    Task SavePlanAsync(TrainingPlan plan, CancellationToken cancellationToken);

    Task<TrainingPlan> AdoptTemplateAsync(TrainingPlan source, CancellationToken cancellationToken);

    Task DeletePlanAsync(Guid id, CancellationToken cancellationToken);
}

internal sealed class PlanPersistenceService(ForgeStartupService startup, IDataSessionFactory sessions) : IPlanPersistenceService
{
    public async Task<IReadOnlyList<TrainingPlan>> ListUserPlansAsync(CancellationToken cancellationToken)
    {
        await EnsureDatabaseReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var session = sessions.Create();
        var plans = session.Repository<TrainingPlan>();

        return (await plans.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(plan => !plan.IsTemplate)
            .OrderByDescending(plan => plan.IsActive)
            .ThenBy(plan => plan.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<TrainingPlan?> GetPlanAsync(Guid id, CancellationToken cancellationToken)
    {
        await EnsureDatabaseReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var session = sessions.Create();
        var plans = session.Repository<TrainingPlan>();
        return await plans.GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task SavePlanAsync(TrainingPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await EnsureDatabaseReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var session = sessions.Create();
        var plans = session.Repository<TrainingPlan>();

        var existingPlans = await plans.ListAsync(cancellationToken).ConfigureAwait(false);
        plan.IsTemplate = false;
        if (plan.IsActive || existingPlans.All(existing => existing.IsTemplate || existing.Id == plan.Id))
        {
            plan.IsActive = true;
            foreach (var existing in existingPlans.Where(existing => !existing.IsTemplate && existing.Id != plan.Id && existing.IsActive))
            {
                existing.IsActive = false;
                await plans.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
            }
        }

        if (existingPlans.Any(existing => existing.Id == plan.Id))
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
        var adoptedPlan = source.CreateEditableCopy();
        await SavePlanAsync(adoptedPlan, cancellationToken).ConfigureAwait(false);
        return adoptedPlan;
    }

    public async Task DeletePlanAsync(Guid id, CancellationToken cancellationToken)
    {
        await EnsureDatabaseReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var session = sessions.Create();
        var plans = session.Repository<TrainingPlan>();
        var days = session.Repository<PlanDay>();
        var exercises = session.Repository<PlannedExercise>();
        var sets = session.Repository<PlannedSet>();

        var plan = await plans.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (plan is null)
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

    private async Task EnsureDatabaseReadyAsync(CancellationToken cancellationToken)
    {
        await startup.InitialiseAsync(cancellationToken).ConfigureAwait(false);
        if (!startup.Succeeded)
        {
            throw new InvalidOperationException("Forge database startup did not complete.", startup.Failure);
        }
    }
}
