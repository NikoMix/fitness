using Forge.App.Composition;
using Forge.Core.Abstractions.Data;
using Forge.Core.Abstractions.Health;
using Forge.Domain.Analytics;
using Forge.Domain.Coaching;
using Forge.Domain.Measurement;
using Forge.Domain.Recovery;
using Forge.Domain.Training;
using Forge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Coaching.Services;

internal sealed class CoachingDataService(ForgeStartupService startup, IDataSessionFactory sessions, IServiceProvider services) : ICoachingDataService
{
    public async Task<NextSessionRecommendation> GetNextSessionRecommendationAsync(CancellationToken cancellationToken)
    {
        await EnsureDatabaseReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var context = CreateContext();
        var sets = await context.Set<SetEntry>().Where(set => !set.IsWarmUp).OrderByDescending(set => set.CompletedUtc).Take(12).ToListAsync(cancellationToken).ConfigureAwait(false);
        var latest = sets.FirstOrDefault();
        if (latest is null)
        {
            return NextSessionRecommender.Recommend(new NextSessionRecommendationRequest(
                Guid.CreateVersion7(),
                "First workout",
                "General",
                [],
                Mass.Zero,
                8,
                10,
                3,
                []));
        }

        var exercise = await context.Set<Exercise>().SingleOrDefaultAsync(item => item.Id == latest.ExerciseId, cancellationToken).ConfigureAwait(false);
        var soreness = await context.Set<SorenessEntry>().ToListAsync(cancellationToken).ConfigureAwait(false);
        var request = new NextSessionRecommendationRequest(
            latest.ExerciseId,
            exercise?.Name ?? "Exercise",
            exercise?.PrimaryMuscle ?? "General",
            exercise?.SecondaryMuscles ?? [],
            latest.Load,
            Math.Max(1, latest.Repetitions - 2),
            Math.Max(1, latest.Repetitions),
            3,
            sets.Where(set => set.ExerciseId == latest.ExerciseId)
                .Select(set => new SessionPerformance(DateOnly.FromDateTime(set.CompletedUtc.LocalDateTime), set.Load, set.Repetitions, set.RepsInReserve, set.IsWarmUp))
                .ToList(),
            Contraindications: [],
            Soreness: soreness);

        return NextSessionRecommender.Recommend(request);
    }

    public async Task<ReadinessScoreResult> GetReadinessAsync(CancellationToken cancellationToken)
    {
        await EnsureDatabaseReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var context = CreateContext();
        var checkIn = await context.Set<MorningCheckIn>().OrderByDescending(entry => entry.Date).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? new MorningCheckIn();
        var soreness = await context.Set<SorenessEntry>().ToListAsync(cancellationToken).ConfigureAwait(false);
        var trainingLoad = await LoadTrainingLoadAsync(context, cancellationToken).ConfigureAwait(false);
        var healthSleep = await TryReadSleepHoursAsync(cancellationToken).ConfigureAwait(false);

        return ReadinessScore.Calculate(new ReadinessInput(checkIn, trainingLoad, soreness, healthSleep));
    }

    public async Task SaveMorningCheckInAsync(MorningCheckIn checkIn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkIn);
        await EnsureDatabaseReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var session = sessions.Create();
        await session.Repository<MorningCheckIn>().AddAsync(checkIn, cancellationToken).ConfigureAwait(false);
        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TrainingLoadRatio?> LoadTrainingLoadAsync(ForgeDbContext context, CancellationToken cancellationToken)
    {
        var sets = await context.Set<SetEntry>().Where(set => !set.IsWarmUp).ToListAsync(cancellationToken).ConfigureAwait(false);
        var points = sets.Select(set => new TrainingLoadPoint(DateOnly.FromDateTime(set.CompletedUtc.LocalDateTime), set.Volume));
        return TrainingLoadCalculator.Calculate(points, DateOnly.FromDateTime(DateTime.Now));
    }

    private async Task<decimal?> TryReadSleepHoursAsync(CancellationToken cancellationToken)
    {
        var health = services.GetService<IHealthDataService>();
        if (health is null)
        {
            return null;
        }

        var availability = await health.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        if (availability is HealthAvailability.NotSupportedOnPlatform or HealthAvailability.PermissionUnknown)
        {
            return null;
        }

        var end = DateTimeOffset.Now;
        var start = end.AddDays(-1);
        var result = await health.ReadAsync([HealthDataType.Sleep], start, end, cancellationToken).ConfigureAwait(false);
        return result.Samples.OfType<SleepHealthSample>().Select(sample => (decimal)sample.Duration.TotalHours).DefaultIfEmpty().Max() is var hours && hours > 0m
            ? decimal.Round(hours, 2)
            : null;
    }

    private async Task EnsureDatabaseReadyAsync(CancellationToken cancellationToken)
    {
        await startup.InitialiseAsync(cancellationToken).ConfigureAwait(false);
        if (!startup.Succeeded)
        {
            throw new InvalidOperationException("Forge database startup did not complete.", startup.Failure);
        }
    }

    private ForgeDbContext CreateContext() => services.GetRequiredService<ForgeDbContext>();
}
