using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanWorkoutLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlanDayId",
                table: "WorkoutSession",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanDayName",
                table: "WorkoutSession",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TrainingPlanId",
                table: "WorkoutSession",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSession_UserProfileId_PlanDayId_CompletedUtc",
                table: "WorkoutSession",
                columns: new[] { "UserProfileId", "PlanDayId", "CompletedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkoutSession_UserProfileId_PlanDayId_CompletedUtc",
                table: "WorkoutSession");

            migrationBuilder.DropColumn(
                name: "PlanDayId",
                table: "WorkoutSession");

            migrationBuilder.DropColumn(
                name: "PlanDayName",
                table: "WorkoutSession");

            migrationBuilder.DropColumn(
                name: "TrainingPlanId",
                table: "WorkoutSession");
        }
    }
}
