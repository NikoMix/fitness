using Forge.App.Composition;
using Forge.App.Features.Profile;
using Forge.Core.Abstractions.Data;
using Forge.Core.Abstractions.Health;
using Forge.Domain.Analytics;
using Forge.Domain.Coaching;
using Forge.Domain.Measurement;
using Forge.Domain.Profile;
using Forge.Domain.Recovery;
using Forge.Domain.Training;
using Forge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Coaching.Services;

internal sealed class CoachingDataService(ForgeStartupService startup, IDataSessionFactory sessions, IServiceProvider services, ProfileStore profiles) : ICoachingDataService
{
    public async Task<NextSessionAdvice> GetNextSessionRecommendationAsync(CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken).ConfigureAwait(false);
        await using var context = CreateContext();

        // What the profile declared, read once and used for both the block and the sentence that
        // says whether the block happened. Reading it twice would let the two disagree.
        var declaration = await LoadDeclaredLimitationsAsync(context, scope, cancellationToken).ConfigureAwait(false);
        var limitationSummary = MovementLimitationCoaching.DescribeUnderstanding(declaration);

        // Ordered client-side: SQLite has no DateTimeOffset type, so ORDER BY over one throws at
        // runtime even though it compiles. See WorkoutPersistenceService.LoadOrStartAsync.
        var workingSets = await context.Set<SetEntry>().OwnedBy(scope).Where(set => !set.IsWarmUp).ToListAsync(cancellationToken).ConfigureAwait(false);
        var sets = workingSets.OrderByDescending(set => set.CompletedUtc).Take(12).ToList();
        var latest = sets.FirstOrDefault();
        if (latest is null)
        {
            return Advise(
                NextSessionRecommender.Recommend(new NextSessionRecommendationRequest(
                    Guid.CreateVersion7(),
                    "First workout",
                    "General",
                    [],
                    Mass.Zero,
                    8,
                    10,
                    3,
                    [])),
                declaration,
                limitationSummary);
        }

        // The exercise catalogue is shared between profiles on purpose and is read unscoped.
        var exercise = await context.Set<Exercise>().SingleOrDefaultAsync(item => item.Id == latest.ExerciseId, cancellationToken).ConfigureAwait(false);
        var soreness = await context.Set<SorenessEntry>().OwnedBy(scope).ToListAsync(cancellationToken).ConfigureAwait(false);
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
            Contraindications: MovementLimitationCoaching.ContraindicationsFor(
                declaration,
                exercise?.PrimaryMuscle,
                exercise?.Pattern ?? MovementPattern.Unspecified),
            Soreness: soreness);

        return Advise(NextSessionRecommender.Recommend(request), declaration, limitationSummary);
    }

    public async Task<ReadinessScoreResult> GetReadinessAsync(CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken).ConfigureAwait(false);
        await using var context = CreateContext();

        // Date is a DateOnly, which SQLite stores as sortable text, so this ORDER BY is safe to
        // run in the database. The DateTimeOffset restriction elsewhere in this file does not
        // apply to it.
        var checkIn = await context.Set<MorningCheckIn>().OwnedBy(scope).OrderByDescending(entry => entry.Date).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? new MorningCheckIn { UserProfileId = scope.ProfileId };
        var soreness = await context.Set<SorenessEntry>().OwnedBy(scope).ToListAsync(cancellationToken).ConfigureAwait(false);
        var trainingLoad = await LoadTrainingLoadAsync(context, scope, cancellationToken).ConfigureAwait(false);
        var healthSleep = await TryReadSleepHoursAsync(cancellationToken).ConfigureAwait(false);

        return ReadinessScore.Calculate(new ReadinessInput(checkIn, trainingLoad, soreness, healthSleep));
    }

    /// <summary>Saves the morning check-in against the profile that is training today.</summary>
    /// <remarks>
    /// The owner is stamped here rather than by the caller. <c>MorningCheckInViewModel</c> composes
    /// the entity from slider values and has no profile scope; giving it one would put the privacy
    /// boundary in a view model, where it drifts out of step with the code that writes the row.
    /// </remarks>
    public async Task SaveMorningCheckInAsync(MorningCheckIn checkIn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkIn);
        var scope = await ResolveScopeAsync(cancellationToken).ConfigureAwait(false);
        checkIn.UserProfileId = scope.ProfileId;
        await using var session = sessions.Create();
        await session.Repository<MorningCheckIn>().AddAsync(checkIn, cancellationToken).ConfigureAwait(false);
        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the active profile's free-text limitation and interprets it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reading is delegated to <see cref="MovementLimitationDeclaration"/> rather than done
    /// here. Coaching is the fourth consumer of the same sentence - the library, the alternatives
    /// screen and onboarding's own echo are the others - and a second interpretation living in a
    /// data service would eventually tell one screen something the other three deny.
    /// </para>
    /// <para>
    /// An unresolved scope reads nothing, so no other profile's declaration can leak in. A profile
    /// that named nothing yields <see cref="MovementLimitationDeclaration.Empty"/>, which
    /// contraindicates nothing and says nothing.
    /// </para>
    /// </remarks>
    private static async Task<MovementLimitationDeclaration> LoadDeclaredLimitationsAsync(
        ForgeDbContext context,
        ProfileScope scope,
        CancellationToken cancellationToken)
    {
        if (!scope.IsResolved)
        {
            return MovementLimitationDeclaration.Empty;
        }

        var profileId = scope.ProfileId;
        var declaration = await context.Set<UserProfile>()
            .Where(profile => profile.Id == profileId)
            .Select(profile => profile.MovementLimitations)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return MovementLimitationDeclaration.FromDeclaration(declaration);
    }

    private static NextSessionAdvice Advise(
        NextSessionRecommendation recommendation,
        MovementLimitationDeclaration declaration,
        string limitationSummary) =>
        new(recommendation, declaration.RecognisedAreas, declaration.UninterpretedPhrases, limitationSummary);

    private static async Task<TrainingLoadRatio?> LoadTrainingLoadAsync(ForgeDbContext context, ProfileScope scope, CancellationToken cancellationToken)
    {
        var sets = await context.Set<SetEntry>().OwnedBy(scope).Where(set => !set.IsWarmUp).ToListAsync(cancellationToken).ConfigureAwait(false);
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

    private ForgeDbContext CreateContext() => services.GetRequiredService<ForgeDbContext>();
}
