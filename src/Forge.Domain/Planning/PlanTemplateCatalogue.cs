using Forge.Domain.Measurement;
using Forge.Domain.Training;

namespace Forge.Domain.Planning;

/// <summary>Original Forge programme templates for first-run value.</summary>
public static class PlanTemplateCatalogue
{
    /// <summary>Six ready-made plans covering beginner, split, strength, hypertrophy and home use.</summary>
    public static IReadOnlyList<TrainingPlan> Templates { get; } =
    [
        Create("Full-body beginner", "Three steady sessions that teach the main movement patterns without burying a new lifter in choices.", 3,
            Day("Full body A", DayOfWeek.Monday, 0, Ex("Goblet squat", MovementPattern.Squat, "Quadriceps", 0, 3, 8, 10, 60), Ex("Push-up", MovementPattern.Push, "Chest", 1, 3, 6, 10, 60), Ex("One-arm dumbbell row", MovementPattern.Pull, "Back", 2, 3, 8, 10, 75), Ex("Plank", MovementPattern.Core, "Core", 3, 3, 30, 45, 45)),
            Day("Full body B", DayOfWeek.Wednesday, 1, Ex("Romanian deadlift", MovementPattern.Hinge, "Hamstrings", 0, 3, 8, 10, 90), Ex("Dumbbell shoulder press", MovementPattern.Push, "Shoulders", 1, 3, 8, 10, 75), Ex("Lat pulldown", MovementPattern.Pull, "Back", 2, 3, 8, 12, 75), Ex("Dead bug", MovementPattern.Core, "Core", 3, 2, 8, 10, 45)),
            Day("Full body C", DayOfWeek.Friday, 2, Ex("Reverse lunge", MovementPattern.Lunge, "Glutes", 0, 3, 8, 10, 75), Ex("Incline dumbbell press", MovementPattern.Push, "Chest", 1, 3, 8, 10, 75), Ex("Seated cable row", MovementPattern.Pull, "Back", 2, 3, 8, 12, 75), Ex("Farmer carry", MovementPattern.Carry, "Grip", 3, 3, 30, 40, 60))),
        Create("Upper/lower split", "Four focused days divide upper and lower work so you can practice lifts often while keeping sessions manageable.", 4,
            Day("Upper 1", DayOfWeek.Monday, 0, Ex("Bench press", MovementPattern.Push, "Chest", 0, 4, 5, 8, 120), Ex("Chest-supported row", MovementPattern.Pull, "Back", 1, 4, 6, 10, 120), Ex("Dumbbell shoulder press", MovementPattern.Push, "Shoulders", 2, 3, 8, 10, 90), Ex("Face pull", MovementPattern.Pull, "Rear delts", 3, 3, 12, 15, 60)),
            Day("Lower 1", DayOfWeek.Tuesday, 1, Ex("Back squat", MovementPattern.Squat, "Quadriceps", 0, 4, 5, 8, 150), Ex("Romanian deadlift", MovementPattern.Hinge, "Hamstrings", 1, 3, 8, 10, 120), Ex("Walking lunge", MovementPattern.Lunge, "Glutes", 2, 3, 10, 12, 90)),
            Day("Upper 2", DayOfWeek.Thursday, 2, Ex("Pull-up", MovementPattern.Pull, "Back", 0, 4, 5, 8, 120), Ex("Incline dumbbell press", MovementPattern.Push, "Chest", 1, 4, 8, 10, 90), Ex("Cable row", MovementPattern.Pull, "Back", 2, 3, 10, 12, 75), Ex("Lateral raise", MovementPattern.Push, "Shoulders", 3, 3, 12, 15, 60)),
            Day("Lower 2", DayOfWeek.Friday, 3, Ex("Deadlift", MovementPattern.Hinge, "Posterior chain", 0, 3, 3, 5, 180), Ex("Front squat", MovementPattern.Squat, "Quadriceps", 1, 3, 6, 8, 150), Ex("Split squat", MovementPattern.Lunge, "Glutes", 2, 3, 8, 10, 90))),
        Create("Push-pull-legs", "A six-day enthusiast template with repeatable themes, useful when you enjoy frequent training and clear exercise buckets.", 6,
            Day("Push", DayOfWeek.Monday, 0, Ex("Bench press", MovementPattern.Push, "Chest", 0, 4, 6, 8, 120), Ex("Overhead press", MovementPattern.Push, "Shoulders", 1, 3, 6, 8, 120), Ex("Triceps pressdown", MovementPattern.Push, "Triceps", 2, 3, 10, 15, 60)),
            Day("Pull", DayOfWeek.Tuesday, 1, Ex("Barbell row", MovementPattern.Pull, "Back", 0, 4, 6, 8, 120), Ex("Lat pulldown", MovementPattern.Pull, "Back", 1, 3, 8, 12, 90), Ex("Hammer curl", MovementPattern.Pull, "Biceps", 2, 3, 10, 12, 60)),
            Day("Legs", DayOfWeek.Wednesday, 2, Ex("Back squat", MovementPattern.Squat, "Quadriceps", 0, 4, 6, 8, 150), Ex("Hip thrust", MovementPattern.Hinge, "Glutes", 1, 3, 8, 10, 120), Ex("Calf raise", MovementPattern.Lunge, "Calves", 2, 3, 12, 15, 60))),
        Create("Strength 5x5", "Simple heavy practice around five-rep sets, with enough pulling and trunk work to support the barbell lifts.", 3,
            Day("5x5 A", DayOfWeek.Monday, 0, Ex("Back squat", MovementPattern.Squat, "Quadriceps", 0, 5, 5, 5, 180), Ex("Bench press", MovementPattern.Push, "Chest", 1, 5, 5, 5, 180), Ex("Barbell row", MovementPattern.Pull, "Back", 2, 5, 5, 5, 150)),
            Day("5x5 B", DayOfWeek.Wednesday, 1, Ex("Back squat", MovementPattern.Squat, "Quadriceps", 0, 5, 5, 5, 180), Ex("Overhead press", MovementPattern.Push, "Shoulders", 1, 5, 5, 5, 180), Ex("Deadlift", MovementPattern.Hinge, "Posterior chain", 2, 3, 5, 5, 180)),
            Day("5x5 C", DayOfWeek.Friday, 2, Ex("Back squat", MovementPattern.Squat, "Quadriceps", 0, 5, 5, 5, 180), Ex("Bench press", MovementPattern.Push, "Chest", 1, 5, 5, 5, 180), Ex("Pull-up", MovementPattern.Pull, "Back", 2, 4, 5, 8, 120))),
        Create("Hypertrophy", "Moderate loads, controlled rests and repeated muscle exposure for someone training mainly to build size.", 5,
            Day("Chest and back", DayOfWeek.Monday, 0, Ex("Incline dumbbell press", MovementPattern.Push, "Chest", 0, 4, 8, 12, 90), Ex("Seated cable row", MovementPattern.Pull, "Back", 1, 4, 8, 12, 90), Ex("Cable fly", MovementPattern.Push, "Chest", 2, 3, 12, 15, 60), Ex("Straight-arm pulldown", MovementPattern.Pull, "Back", 3, 3, 12, 15, 60)),
            Day("Legs", DayOfWeek.Tuesday, 1, Ex("Leg press", MovementPattern.Squat, "Quadriceps", 0, 4, 10, 15, 120), Ex("Romanian deadlift", MovementPattern.Hinge, "Hamstrings", 1, 4, 8, 12, 120), Ex("Walking lunge", MovementPattern.Lunge, "Glutes", 2, 3, 10, 12, 90)),
            Day("Shoulders and arms", DayOfWeek.Thursday, 2, Ex("Dumbbell shoulder press", MovementPattern.Push, "Shoulders", 0, 3, 8, 12, 90), Ex("Lateral raise", MovementPattern.Push, "Shoulders", 1, 4, 12, 20, 45), Ex("Cable curl", MovementPattern.Pull, "Biceps", 2, 3, 10, 15, 60), Ex("Triceps pressdown", MovementPattern.Push, "Triceps", 3, 3, 10, 15, 60))),
        Create("Home bodyweight", "No-gym training using bodyweight patterns, simple circuits and repeatable progressions for busy weeks.", 3,
            Day("Home A", DayOfWeek.Monday, 0, Ex("Bodyweight squat", MovementPattern.Squat, "Quadriceps", 0, 4, 12, 20, 45), Ex("Push-up", MovementPattern.Push, "Chest", 1, 4, 6, 15, 60), Ex("Towel row", MovementPattern.Pull, "Back", 2, 4, 8, 12, 60), Ex("Side plank", MovementPattern.Core, "Core", 3, 3, 20, 40, 45)),
            Day("Home B", DayOfWeek.Wednesday, 1, Ex("Hip bridge", MovementPattern.Hinge, "Glutes", 0, 4, 12, 20, 45), Ex("Pike push-up", MovementPattern.Push, "Shoulders", 1, 3, 6, 12, 60), Ex("Reverse snow angel", MovementPattern.Pull, "Upper back", 2, 3, 12, 15, 45), Ex("Mountain climber", MovementPattern.Cardio, "Core", 3, 3, 30, 45, 45)),
            Day("Home C", DayOfWeek.Friday, 2, Ex("Split squat", MovementPattern.Lunge, "Glutes", 0, 3, 8, 12, 60), Ex("Close-grip push-up", MovementPattern.Push, "Triceps", 1, 3, 6, 12, 60), Ex("Prone Y raise", MovementPattern.Pull, "Rear delts", 2, 3, 10, 15, 45), Ex("Hollow hold", MovementPattern.Core, "Core", 3, 3, 20, 40, 45)))
    ];

    private static TrainingPlan Create(string name, string description, int sessionsPerWeek, params PlanDay[] days)
    {
        var plan = new TrainingPlan
        {
            Name = name,
            Description = description,
            IsTemplate = true,
            ScheduleMode = PlanScheduleMode.FixedDays,
            TargetSessionsPerWeek = sessionsPerWeek
        };

        foreach (var day in days)
        {
            plan.Days.Add(day);
        }

        return plan;
    }

    private static PlanDay Day(string name, DayOfWeek weekday, int ordinal, params PlannedExercise[] exercises)
    {
        var day = new PlanDay { Name = name, ScheduledDay = weekday, Ordinal = ordinal };
        foreach (var exercise in exercises)
        {
            day.Exercises.Add(exercise);
        }

        return day;
    }

    private static PlannedExercise Ex(string name, MovementPattern pattern, string muscle, int ordinal, int sets, int minReps, int maxReps, int restSeconds)
    {
        var exercise = new PlannedExercise
        {
            ExerciseName = name,
            Pattern = pattern,
            PrimaryMuscle = muscle,
            Ordinal = ordinal,
            BlockType = PlanBlockType.Work
        };

        for (var set = 1; set <= sets; set++)
        {
            exercise.Sets.Add(new PlannedSet
            {
                Ordinal = set,
                TargetRepsMin = minReps,
                TargetRepsMax = maxReps,
                Rest = TimeSpan.FromSeconds(restSeconds),
                TargetRpe = 8m,
                TargetLoad = Mass.Zero
            });
        }

        return exercise;
    }
}
