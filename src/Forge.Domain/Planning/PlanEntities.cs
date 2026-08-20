using Forge.Domain.Common;
using Forge.Domain.Measurement;
using Forge.Domain.Training;

namespace Forge.Domain.Planning;

/// <summary>A reusable or user-authored training programme.</summary>
public sealed class TrainingPlan : Entity
{
    /// <summary>Display name.</summary>
    public required string Name { get; set; }

    /// <summary>Short explanation of who the plan serves and how to use it.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Whether this row is shipped template data rather than a user's own plan.</summary>
    public bool IsTemplate { get; set; }

    /// <summary>Whether this is the user's active programme for Today and workout start.</summary>
    public bool IsActive { get; set; }

    /// <summary>Fixed weekdays or flexible frequency scheduling.</summary>
    public PlanScheduleMode ScheduleMode { get; set; } = PlanScheduleMode.Flexible;

    /// <summary>Target sessions per seven-day week for flexible scheduling.</summary>
    public int TargetSessionsPerWeek { get; set; } = 3;

    /// <summary>Days contained by the plan, in intended order.</summary>
    public ICollection<PlanDay> Days { get; } = [];

    /// <summary>Creates an editable user-owned copy of this plan.</summary>
    public TrainingPlan CreateEditableCopy(string? name = null, bool isActive = true)
    {
        var copy = new TrainingPlan
        {
            Name = string.IsNullOrWhiteSpace(name) ? Name : name,
            Description = Description,
            IsTemplate = false,
            IsActive = isActive,
            ScheduleMode = ScheduleMode,
            TargetSessionsPerWeek = TargetSessionsPerWeek
        };

        foreach (var day in Days.OrderBy(day => day.Ordinal))
        {
            var dayCopy = new PlanDay
            {
                Name = day.Name,
                Ordinal = day.Ordinal,
                ScheduledDay = day.ScheduledDay
            };

            foreach (var exercise in day.Exercises.OrderBy(exercise => exercise.Ordinal))
            {
                var exerciseCopy = new PlannedExercise
                {
                    ExerciseId = exercise.ExerciseId,
                    ExerciseName = exercise.ExerciseName,
                    Pattern = exercise.Pattern,
                    PrimaryMuscle = exercise.PrimaryMuscle,
                    SecondaryMuscles = [.. exercise.SecondaryMuscles],
                    BlockType = exercise.BlockType,
                    GroupKey = exercise.GroupKey,
                    Ordinal = exercise.Ordinal
                };

                foreach (var set in exercise.Sets.OrderBy(set => set.Ordinal))
                {
                    exerciseCopy.Sets.Add(new PlannedSet
                    {
                        Ordinal = set.Ordinal,
                        TargetRepsMin = set.TargetRepsMin,
                        TargetRepsMax = set.TargetRepsMax,
                        TargetLoad = set.TargetLoad,
                        TargetRpe = set.TargetRpe,
                        Rest = set.Rest,
                        IsWarmUp = set.IsWarmUp
                    });
                }

                dayCopy.Exercises.Add(exerciseCopy);
            }

            copy.Days.Add(dayCopy);
        }

        return copy;
    }
}

/// <summary>One planned training day inside a programme.</summary>
public sealed class PlanDay : Entity
{
    /// <summary>Parent plan identifier.</summary>
    public Guid TrainingPlanId { get; init; }

    /// <summary>Display name, for example "Upper A".</summary>
    public required string Name { get; set; }

    /// <summary>Zero-based order inside the plan.</summary>
    public int Ordinal { get; set; }

    /// <summary>Weekday for fixed-day scheduling; null means the day floats.</summary>
    public DayOfWeek? ScheduledDay { get; set; }

    /// <summary>Planned exercise blocks in order.</summary>
    public ICollection<PlannedExercise> Exercises { get; } = [];
}

/// <summary>One exercise prescription, optionally grouped into supersets or circuits.</summary>
public sealed class PlannedExercise : Entity
{
    /// <summary>Parent plan-day identifier.</summary>
    public Guid PlanDayId { get; init; }

    /// <summary>Catalogue exercise identifier when known.</summary>
    public Guid? ExerciseId { get; set; }

    /// <summary>Snapshot name so templates remain readable without catalogue joins.</summary>
    public required string ExerciseName { get; set; }

    /// <summary>Movement pattern used for balance analysis.</summary>
    public MovementPattern Pattern { get; set; } = MovementPattern.Unspecified;

    /// <summary>Primary muscle group used for volume balance.</summary>
    public string PrimaryMuscle { get; set; } = "General";

    /// <summary>Secondary muscles that materially contribute.</summary>
    public List<string> SecondaryMuscles { get; set; } = [];

    /// <summary>Training block classification.</summary>
    public PlanBlockType BlockType { get; set; } = PlanBlockType.Work;

    /// <summary>Shared key for exercises paired as a superset or circuit, for example "A1".</summary>
    public string? GroupKey { get; set; }

    /// <summary>Zero-based order inside the day.</summary>
    public int Ordinal { get; set; }

    /// <summary>Set prescriptions.</summary>
    public ICollection<PlannedSet> Sets { get; } = [];

    /// <summary>Working sets, excluding warm-up and cool-down prescriptions.</summary>
    public int WorkingSetCount => Sets.Count(set => !set.IsWarmUp && BlockType == PlanBlockType.Work);
}

/// <summary>One planned set prescription.</summary>
public sealed class PlannedSet : Entity
{
    /// <summary>Parent exercise identifier.</summary>
    public Guid PlannedExerciseId { get; init; }

    /// <summary>One-based order under the exercise.</summary>
    public int Ordinal { get; set; }

    /// <summary>Low end of the target repetition range.</summary>
    public int TargetRepsMin { get; set; }

    /// <summary>High end of the target repetition range.</summary>
    public int TargetRepsMax { get; set; }

    /// <summary>Target load. Null means bodyweight, timed or technique work.</summary>
    public Mass? TargetLoad { get; set; }

    /// <summary>Target rate of perceived exertion on a 1-10 scale.</summary>
    public decimal? TargetRpe { get; set; }

    /// <summary>Rest after the set.</summary>
    public TimeSpan Rest { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Whether this set is preparation and excluded from working volume.</summary>
    public bool IsWarmUp { get; set; }
}

/// <summary>Where an exercise sits within a session.</summary>
public enum PlanBlockType
{
    /// <summary>Preparation block.</summary>
    WarmUp = 0,
    /// <summary>Main training work.</summary>
    Work = 1,
    /// <summary>Finisher or repeated-round conditioning block.</summary>
    Circuit = 2,
    /// <summary>Cooldown or mobility block.</summary>
    CoolDown = 3
}

/// <summary>Scheduling style for a plan.</summary>
public enum PlanScheduleMode
{
    /// <summary>Sessions are tied to named weekdays.</summary>
    FixedDays = 0,
    /// <summary>Only the number of weekly sessions is fixed.</summary>
    Flexible = 1
}
