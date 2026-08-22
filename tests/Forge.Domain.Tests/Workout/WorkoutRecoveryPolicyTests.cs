using Forge.Domain.Training;
using Forge.Domain.Workout;
using Shouldly;

namespace Forge.Domain.Tests.Workout;

public sealed class WorkoutRecoveryPolicyTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();

    [Fact]
    public void Classify_returns_resume_for_recent_unfinished_session()
    {
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var session = new WorkoutSession { UserProfileId = Owner, StartedUtc = now.AddHours(-2), CompletedUtc = null };

        WorkoutRecoveryPolicy.Classify(session, now).ShouldBe(WorkoutRecoveryKind.Resume);
    }

    [Fact]
    public void Classify_returns_stale_for_unfinished_session_older_than_threshold()
    {
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var session = new WorkoutSession { UserProfileId = Owner, StartedUtc = now.AddHours(-13), CompletedUtc = null };

        WorkoutRecoveryPolicy.Classify(session, now).ShouldBe(WorkoutRecoveryKind.Stale);
    }

    [Fact]
    public void Classify_ignores_completed_sessions()
    {
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var session = new WorkoutSession { UserProfileId = Owner, StartedUtc = now.AddHours(-13), CompletedUtc = now.AddHours(-1) };

        WorkoutRecoveryPolicy.Classify(session, now).ShouldBe(WorkoutRecoveryKind.None);
    }
}
