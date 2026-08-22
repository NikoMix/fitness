using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfileOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MorningCheckIn_Date",
                table: "MorningCheckIn");

            migrationBuilder.AddColumn<Guid>(
                name: "UserProfileId",
                table: "WorkoutSession",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserProfileId",
                table: "TrainingPlan",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserProfileId",
                table: "SorenessEntry",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserProfileId",
                table: "SetEntry",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserProfileId",
                table: "Recipe",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserProfileId",
                table: "PlannedSet",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserProfileId",
                table: "PlannedExercise",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserProfileId",
                table: "PlanDay",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserProfileId",
                table: "MorningCheckIn",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserProfileId",
                table: "HydrationEntry",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserProfileId",
                table: "FoodLogEntry",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserProfileId",
                table: "ActiveWorkoutState",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // ---------------------------------------------------------------------------------
            // Backfill. Hand-written; everything above and below this block is scaffolded.
            //
            // Every column above defaults to Guid.Empty on existing rows, and ProfileScope is
            // deliberately fail-closed - it matches nothing when the owner is unresolved. So
            // without this, the schema would be correct, every test would pass, and a user who
            // updated the app would open it to find every workout, meal and plan gone while the
            // rows still sat in the database. They would not file a bug; they would uninstall,
            // and there is no backend copy to restore from.
            //
            // Attribution runs only when the device has exactly one profile that is not deleted.
            // That is every device in existence today, because multi-profile support has not
            // shipped - so it is not a guess, it is the only attribution that can be right.
            //
            // With no profile, nothing is attributed: first-run setup creates one and the rows
            // stay recoverable. With more than one, nothing is attributed either. That deviates
            // from the schema-delta note, which proposed failing loudly, and the reason is that
            // the two failure modes are not equal. Rows left unowned are hidden and can be
            // attributed later; an aborted migration is an app that will not start at all. Both
            // are bad, only one is recoverable, and neither risks handing one person another
            // person's health data - which is the failure this whole change exists to prevent.
            // ---------------------------------------------------------------------------------
            foreach (var table in new[]
                     {
                         "WorkoutSession", "SetEntry", "ActiveWorkoutState", "PlanDay",
                         "PlannedExercise", "PlannedSet", "FoodLogEntry", "HydrationEntry",
                         "MorningCheckIn", "SorenessEntry"
                     })
            {
                migrationBuilder.Sql($"""
                    UPDATE "{table}"
                       SET "UserProfileId" = (SELECT "Id" FROM "UserProfile" WHERE "DeletedUtc" IS NULL)
                     WHERE "UserProfileId" = '00000000-0000-0000-0000-000000000000'
                       AND (SELECT COUNT(*) FROM "UserProfile" WHERE "DeletedUtc" IS NULL) = 1;
                    """);
            }

            // Shipped catalogue rows stay at Guid.Empty on purpose: that is what makes them
            // visible to every profile. RecipeCatalogueService unions "shipped" with "owned by
            // this profile", so a shipped recipe stamped with the first user's identifier would
            // disappear for everybody else on the device.
            migrationBuilder.Sql("""
                UPDATE "Recipe"
                   SET "UserProfileId" = (SELECT "Id" FROM "UserProfile" WHERE "DeletedUtc" IS NULL)
                 WHERE "UserProfileId" = '00000000-0000-0000-0000-000000000000'
                   AND ("Provenance" IS NULL OR "Provenance" = '')
                   AND (SELECT COUNT(*) FROM "UserProfile" WHERE "DeletedUtc" IS NULL) = 1;
                """);

            // Same shape for plan templates. None are persisted today, but a future release that
            // seeds them must not end up with a template only one person can see.
            migrationBuilder.Sql("""
                UPDATE "TrainingPlan"
                   SET "UserProfileId" = (SELECT "Id" FROM "UserProfile" WHERE "DeletedUtc" IS NULL)
                 WHERE "UserProfileId" = '00000000-0000-0000-0000-000000000000'
                   AND "IsTemplate" = 0
                   AND (SELECT COUNT(*) FROM "UserProfile" WHERE "DeletedUtc" IS NULL) = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSession_UserProfileId_CompletedUtc",
                table: "WorkoutSession",
                columns: new[] { "UserProfileId", "CompletedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSession_UserProfileId_StartedUtc",
                table: "WorkoutSession",
                columns: new[] { "UserProfileId", "StartedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingPlan_UserProfileId_IsActive",
                table: "TrainingPlan",
                columns: new[] { "UserProfileId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SorenessEntry_UserProfileId_RecordedOn",
                table: "SorenessEntry",
                columns: new[] { "UserProfileId", "RecordedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_SetEntry_UserProfileId",
                table: "SetEntry",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_SetEntry_UserProfileId_ExerciseId_CompletedUtc",
                table: "SetEntry",
                columns: new[] { "UserProfileId", "ExerciseId", "CompletedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Recipe_UserProfileId",
                table: "Recipe",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedSet_UserProfileId",
                table: "PlannedSet",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedExercise_UserProfileId",
                table: "PlannedExercise",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanDay_UserProfileId",
                table: "PlanDay",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MorningCheckIn_UserProfileId_Date",
                table: "MorningCheckIn",
                columns: new[] { "UserProfileId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HydrationEntry_UserProfileId_ConsumedUtc",
                table: "HydrationEntry",
                columns: new[] { "UserProfileId", "ConsumedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FoodLogEntry_UserProfileId_ConsumedUtc",
                table: "FoodLogEntry",
                columns: new[] { "UserProfileId", "ConsumedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActiveWorkoutState_UserProfileId",
                table: "ActiveWorkoutState",
                column: "UserProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkoutSession_UserProfileId_CompletedUtc",
                table: "WorkoutSession");

            migrationBuilder.DropIndex(
                name: "IX_WorkoutSession_UserProfileId_StartedUtc",
                table: "WorkoutSession");

            migrationBuilder.DropIndex(
                name: "IX_TrainingPlan_UserProfileId_IsActive",
                table: "TrainingPlan");

            migrationBuilder.DropIndex(
                name: "IX_SorenessEntry_UserProfileId_RecordedOn",
                table: "SorenessEntry");

            migrationBuilder.DropIndex(
                name: "IX_SetEntry_UserProfileId",
                table: "SetEntry");

            migrationBuilder.DropIndex(
                name: "IX_SetEntry_UserProfileId_ExerciseId_CompletedUtc",
                table: "SetEntry");

            migrationBuilder.DropIndex(
                name: "IX_Recipe_UserProfileId",
                table: "Recipe");

            migrationBuilder.DropIndex(
                name: "IX_PlannedSet_UserProfileId",
                table: "PlannedSet");

            migrationBuilder.DropIndex(
                name: "IX_PlannedExercise_UserProfileId",
                table: "PlannedExercise");

            migrationBuilder.DropIndex(
                name: "IX_PlanDay_UserProfileId",
                table: "PlanDay");

            migrationBuilder.DropIndex(
                name: "IX_MorningCheckIn_UserProfileId_Date",
                table: "MorningCheckIn");

            migrationBuilder.DropIndex(
                name: "IX_HydrationEntry_UserProfileId_ConsumedUtc",
                table: "HydrationEntry");

            migrationBuilder.DropIndex(
                name: "IX_FoodLogEntry_UserProfileId_ConsumedUtc",
                table: "FoodLogEntry");

            migrationBuilder.DropIndex(
                name: "IX_ActiveWorkoutState_UserProfileId",
                table: "ActiveWorkoutState");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "WorkoutSession");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "TrainingPlan");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "SorenessEntry");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "SetEntry");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "Recipe");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "PlannedSet");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "PlannedExercise");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "PlanDay");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "MorningCheckIn");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "HydrationEntry");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "FoodLogEntry");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "ActiveWorkoutState");

            migrationBuilder.CreateIndex(
                name: "IX_MorningCheckIn_Date",
                table: "MorningCheckIn",
                column: "Date",
                unique: true);
        }
    }
}
