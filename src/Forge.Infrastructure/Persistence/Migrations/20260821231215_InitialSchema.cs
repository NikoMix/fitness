using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Achievement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    EncouragingDescription = table.Column<string>(type: "TEXT", maxLength: 280, nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    UnlockedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ActiveWorkoutState",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkoutSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CurrentExerciseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CurrentExerciseName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ExerciseQueue = table.Column<string>(type: "TEXT", nullable: false),
                    CompletedSets = table.Column<string>(type: "TEXT", nullable: false),
                    ActiveRestTimer_PlannedDuration = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    ActiveRestTimer_StartedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ActiveRestTimer_TargetEndUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ActiveRestTimer_NotificationId = table.Column<int>(type: "INTEGER", nullable: true),
                    ActiveRestTimer_EndedEarly = table.Column<bool>(type: "INTEGER", nullable: true),
                    ActiveRestTimer_EndedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveWorkoutState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BodyMetric",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    WeightKilograms = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    BodyFatPercentage = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: true),
                    WaistCentimetres = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: true),
                    HipCentimetres = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: true),
                    ChestCentimetres = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: true),
                    ThighCentimetres = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BodyMetric", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Exercise",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Pattern = table.Column<int>(type: "INTEGER", nullable: false),
                    PrimaryMuscle = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SecondaryMuscles = table.Column<string>(type: "TEXT", nullable: false),
                    Equipment = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    ForceType = table.Column<int>(type: "INTEGER", nullable: false),
                    ExecutionSteps = table.Column<string>(type: "TEXT", nullable: false),
                    CommonMistakes = table.Column<string>(type: "TEXT", nullable: false),
                    CoachingCues = table.Column<string>(type: "TEXT", nullable: false),
                    SafetyNotes = table.Column<string>(type: "TEXT", nullable: false),
                    IsUnilateral = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsUserCreated = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsFavourite = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastUsedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exercise", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FoodItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    EnergyKilocaloriesPer100g = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    ProteinGramsPer100g = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    CarbohydrateGramsPer100g = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    FatGramsPer100g = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    FibreGramsPer100g = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    SugarGramsPer100g = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    SodiumMilligramsPer100g = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    IsUserCreated = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodItem", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HydrationEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VolumeMillilitres = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    BeverageType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CaffeineMilligrams = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    ConsumedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HydrationEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MorningCheckIn",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Energy = table.Column<int>(type: "INTEGER", nullable: false),
                    Soreness = table.Column<int>(type: "INTEGER", nullable: false),
                    Motivation = table.Column<int>(type: "INTEGER", nullable: false),
                    Stress = table.Column<int>(type: "INTEGER", nullable: false),
                    SleepHours = table.Column<decimal>(type: "TEXT", precision: 4, scale: 2, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MorningCheckIn", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recipe",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    BaseServings = table.Column<int>(type: "INTEGER", nullable: false),
                    PrepTime = table.Column<double>(type: "REAL", nullable: false),
                    CookTime = table.Column<double>(type: "REAL", nullable: false),
                    Provenance = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recipe", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeedContentImport",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogueName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeedContentImport", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SorenessEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MuscleGroup = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    RecordedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SorenessEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Streak",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurrentDays = table.Column<int>(type: "INTEGER", nullable: false),
                    BestDays = table.Column<int>(type: "INTEGER", nullable: false),
                    FreezesRemaining = table.Column<int>(type: "INTEGER", nullable: false),
                    GamificationEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastCountedDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    History = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Streak", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainingPlan",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    IsTemplate = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScheduleMode = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetSessionsPerWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingPlan", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    LastActivatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    BiologicalSex = table.Column<int>(type: "INTEGER", nullable: false),
                    HeightCentimetres = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: false),
                    ExperienceLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    Goal = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetWeightKilograms = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: true),
                    GoalTimeframeWeeks = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetDailyCalories = table.Column<decimal>(type: "TEXT", precision: 8, scale: 0, nullable: true),
                    AvailableEquipment = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    MovementLimitations = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    TrainingDaysPerWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutSession",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SessionRpe = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutSession", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FoodBarcode",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Gtin14 = table.Column<string>(type: "TEXT", maxLength: 14, nullable: false),
                    ScannedValue = table.Column<string>(type: "TEXT", maxLength: 14, nullable: false),
                    Symbology = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FoodItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provenance = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastScannedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    TimesScanned = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodBarcode", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FoodBarcode_FoodItem_FoodItemId",
                        column: x => x.FoodItemId,
                        principalTable: "FoodItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FoodItemServingDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MassKilograms = table.Column<decimal>(type: "TEXT", precision: 10, scale: 6, nullable: false),
                    VolumeMillilitres = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: true),
                    FoodItemId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodItemServingDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FoodItemServingDefinitions_FoodItem_FoodItemId",
                        column: x => x.FoodItemId,
                        principalTable: "FoodItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FoodLogEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FoodItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Serving_ServingName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Serving_Quantity = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    Serving_GramsPerServing = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    MealSlot = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConsumedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodLogEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FoodLogEntry_FoodItem_FoodItemId",
                        column: x => x.FoodItemId,
                        principalTable: "FoodItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecipeIngredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EdibleMassKilograms = table.Column<decimal>(type: "TEXT", precision: 10, scale: 6, nullable: false),
                    VolumeMillilitres = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: true),
                    EnergyKilocaloriesPer100g = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    ProteinGramsPer100g = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    CarbohydrateGramsPer100g = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    FatGramsPer100g = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    FibreGramsPer100g = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    SugarGramsPer100g = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    SodiumMilligramsPer100g = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    PreparationNote = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RecipeId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeIngredients_Recipe_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Instruction = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RecipeId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeSteps_Recipe_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Tag = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RecipeId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeTags_Recipe_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanDay",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrainingPlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledDay = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanDay", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanDay_TrainingPlan_TrainingPlanId",
                        column: x => x.TrainingPlanId,
                        principalTable: "TrainingPlan",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SetEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkoutSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    LoadKilograms = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: false),
                    Repetitions = table.Column<int>(type: "INTEGER", nullable: false),
                    RepsInReserve = table.Column<int>(type: "INTEGER", nullable: true),
                    ToFailure = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsWarmUp = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    DistanceMetres = table.Column<double>(type: "REAL", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SetEntry_WorkoutSession_WorkoutSessionId",
                        column: x => x.WorkoutSessionId,
                        principalTable: "WorkoutSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlannedExercise",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanDayId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExerciseName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Pattern = table.Column<int>(type: "INTEGER", nullable: false),
                    PrimaryMuscle = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SecondaryMuscles = table.Column<string>(type: "TEXT", nullable: false),
                    BlockType = table.Column<int>(type: "INTEGER", nullable: false),
                    GroupKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedExercise", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannedExercise_PlanDay_PlanDayId",
                        column: x => x.PlanDayId,
                        principalTable: "PlanDay",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlannedSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlannedExerciseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetRepsMin = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetRepsMax = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetLoadKilograms = table.Column<decimal>(type: "TEXT", precision: 10, scale: 3, nullable: true),
                    TargetRpe = table.Column<decimal>(type: "TEXT", precision: 4, scale: 1, nullable: true),
                    Rest = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    IsWarmUp = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedSet", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannedSet_PlannedExercise_PlannedExerciseId",
                        column: x => x.PlannedExerciseId,
                        principalTable: "PlannedExercise",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Achievement_Category",
                table: "Achievement",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Achievement_Code",
                table: "Achievement",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActiveWorkoutState_CompletedUtc",
                table: "ActiveWorkoutState",
                column: "CompletedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ActiveWorkoutState_WorkoutSessionId",
                table: "ActiveWorkoutState",
                column: "WorkoutSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BodyMetric_UserProfileId_RecordedUtc",
                table: "BodyMetric",
                columns: new[] { "UserProfileId", "RecordedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Exercise_Equipment",
                table: "Exercise",
                column: "Equipment");

            migrationBuilder.CreateIndex(
                name: "IX_Exercise_IsFavourite",
                table: "Exercise",
                column: "IsFavourite");

            migrationBuilder.CreateIndex(
                name: "IX_Exercise_LastUsedUtc",
                table: "Exercise",
                column: "LastUsedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Exercise_Name",
                table: "Exercise",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Exercise_Pattern",
                table: "Exercise",
                column: "Pattern");

            migrationBuilder.CreateIndex(
                name: "IX_FoodBarcode_FoodItemId",
                table: "FoodBarcode",
                column: "FoodItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FoodBarcode_Gtin14",
                table: "FoodBarcode",
                column: "Gtin14",
                unique: true,
                filter: "\"DeletedUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FoodItem_Brand",
                table: "FoodItem",
                column: "Brand");

            migrationBuilder.CreateIndex(
                name: "IX_FoodItem_Name",
                table: "FoodItem",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_FoodItemServingDefinitions_FoodItemId",
                table: "FoodItemServingDefinitions",
                column: "FoodItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FoodLogEntry_ConsumedUtc",
                table: "FoodLogEntry",
                column: "ConsumedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FoodLogEntry_FoodItemId",
                table: "FoodLogEntry",
                column: "FoodItemId");

            migrationBuilder.CreateIndex(
                name: "IX_HydrationEntry_ConsumedUtc",
                table: "HydrationEntry",
                column: "ConsumedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MorningCheckIn_Date",
                table: "MorningCheckIn",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanDay_ScheduledDay",
                table: "PlanDay",
                column: "ScheduledDay");

            migrationBuilder.CreateIndex(
                name: "IX_PlanDay_TrainingPlanId_Ordinal",
                table: "PlanDay",
                columns: new[] { "TrainingPlanId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_PlannedExercise_Pattern",
                table: "PlannedExercise",
                column: "Pattern");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedExercise_PlanDayId_Ordinal",
                table: "PlannedExercise",
                columns: new[] { "PlanDayId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_PlannedSet_PlannedExerciseId_Ordinal",
                table: "PlannedSet",
                columns: new[] { "PlannedExerciseId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_Recipe_Name",
                table: "Recipe",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredients_RecipeId",
                table: "RecipeIngredients",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeSteps_RecipeId",
                table: "RecipeSteps",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeTags_RecipeId",
                table: "RecipeTags",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_SeedContentImport_CatalogueName",
                table: "SeedContentImport",
                column: "CatalogueName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SetEntry_ExerciseId_CompletedUtc",
                table: "SetEntry",
                columns: new[] { "ExerciseId", "CompletedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SetEntry_WorkoutSessionId",
                table: "SetEntry",
                column: "WorkoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SorenessEntry_MuscleGroup_RecordedOn",
                table: "SorenessEntry",
                columns: new[] { "MuscleGroup", "RecordedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Streak_UserProfileId",
                table: "Streak",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingPlan_IsActive",
                table: "TrainingPlan",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingPlan_IsTemplate",
                table: "TrainingPlan",
                column: "IsTemplate");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfile_DisplayName",
                table: "UserProfile",
                column: "DisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSession_CompletedUtc",
                table: "WorkoutSession",
                column: "CompletedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSession_StartedUtc",
                table: "WorkoutSession",
                column: "StartedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Achievement");

            migrationBuilder.DropTable(
                name: "ActiveWorkoutState");

            migrationBuilder.DropTable(
                name: "BodyMetric");

            migrationBuilder.DropTable(
                name: "Exercise");

            migrationBuilder.DropTable(
                name: "FoodBarcode");

            migrationBuilder.DropTable(
                name: "FoodItemServingDefinitions");

            migrationBuilder.DropTable(
                name: "FoodLogEntry");

            migrationBuilder.DropTable(
                name: "HydrationEntry");

            migrationBuilder.DropTable(
                name: "MorningCheckIn");

            migrationBuilder.DropTable(
                name: "PlannedSet");

            migrationBuilder.DropTable(
                name: "RecipeIngredients");

            migrationBuilder.DropTable(
                name: "RecipeSteps");

            migrationBuilder.DropTable(
                name: "RecipeTags");

            migrationBuilder.DropTable(
                name: "SeedContentImport");

            migrationBuilder.DropTable(
                name: "SetEntry");

            migrationBuilder.DropTable(
                name: "SorenessEntry");

            migrationBuilder.DropTable(
                name: "Streak");

            migrationBuilder.DropTable(
                name: "UserProfile");

            migrationBuilder.DropTable(
                name: "FoodItem");

            migrationBuilder.DropTable(
                name: "PlannedExercise");

            migrationBuilder.DropTable(
                name: "Recipe");

            migrationBuilder.DropTable(
                name: "WorkoutSession");

            migrationBuilder.DropTable(
                name: "PlanDay");

            migrationBuilder.DropTable(
                name: "TrainingPlan");
        }
    }
}
