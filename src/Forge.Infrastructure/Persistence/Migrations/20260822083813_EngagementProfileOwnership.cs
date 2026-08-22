using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EngagementProfileOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Achievement_Code",
                table: "Achievement");

            migrationBuilder.DropColumn(
                name: "BestDays",
                table: "Streak");

            migrationBuilder.DropColumn(
                name: "CurrentDays",
                table: "Streak");

            migrationBuilder.DropColumn(
                name: "FreezesRemaining",
                table: "Streak");

            migrationBuilder.DropColumn(
                name: "LastCountedDate",
                table: "Streak");

            migrationBuilder.RenameColumn(
                name: "History",
                table: "Streak",
                newName: "ProtectedPeriods");

            // ---------------------------------------------------------------------------------
            // Hand-written. The rename preserves the column contents, and the contents are not
            // compatible.
            //
            // "History" held a JSON array of StreakDay - {"date":...,"kind":...,"streakDaysAfter":...}
            // - and the column now holds ProtectedPeriod - {"start":...,"end":...,"reason":...}.
            // Deserialising the old shape into the new record does NOT throw: ProtectedPeriod has a
            // single parameterised constructor, so every missing member takes its default and each
            // old day becomes ProtectedPeriod(Start: 0001-01-01, End: null, Reason: Deload).
            //
            // End: null means open-ended and Start is year one, so every day of the user's history
            // AND every future day would be silently marked as a deload, suppressing rhythm
            // reminders forever. Nothing throws and no test outside this area would notice.
            //
            // The old value carries no meaning under the new schema - a per-day streak history is
            // not convertible into interruptions the user declared, and inventing interruptions
            // they never declared is exactly the fabrication this feature exists to avoid. So it
            // is reset rather than migrated.
            // ---------------------------------------------------------------------------------
            migrationBuilder.Sql("""UPDATE "Streak" SET "ProtectedPeriods" = '[]';""");

            migrationBuilder.AddColumn<Guid>(
                name: "UserProfileId",
                table: "Achievement",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // ---------------------------------------------------------------------------------
            // Hand-written, and it must run BEFORE the unique index below.
            //
            // Same trap as 20260822024731_ProfileOwnership: EF defaults the new column to
            // Guid.Empty on existing rows, ProfileScope is fail-closed, and a badge owned by
            // nobody is readable by nobody. The user would open an empty cabinet having earned
            // every one of them.
            //
            // Attribution runs only when the device has exactly one profile that is not deleted,
            // matching ProfileOwnership rather than the "oldest profile wins" form proposed in the
            // schema delta. With several profiles, attributing everything to the oldest would hand
            // one person another person's achievements - the precise failure profile separation
            // exists to prevent - and unowned rows stay recoverable where a wrong owner does not.
            // ---------------------------------------------------------------------------------
            migrationBuilder.Sql("""
                UPDATE "Achievement"
                   SET "UserProfileId" = (SELECT "Id" FROM "UserProfile" WHERE "DeletedUtc" IS NULL)
                 WHERE "UserProfileId" = '00000000-0000-0000-0000-000000000000'
                   AND (SELECT COUNT(*) FROM "UserProfile" WHERE "DeletedUtc" IS NULL) = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Achievement_UserProfileId",
                table: "Achievement",
                column: "UserProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Achievement_UserProfileId_Code",
                table: "Achievement",
                columns: new[] { "UserProfileId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Achievement_UserProfileId",
                table: "Achievement");

            migrationBuilder.DropIndex(
                name: "IX_Achievement_UserProfileId_Code",
                table: "Achievement");

            migrationBuilder.DropColumn(
                name: "UserProfileId",
                table: "Achievement");

            migrationBuilder.RenameColumn(
                name: "ProtectedPeriods",
                table: "Streak",
                newName: "History");

            migrationBuilder.AddColumn<int>(
                name: "BestDays",
                table: "Streak",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CurrentDays",
                table: "Streak",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FreezesRemaining",
                table: "Streak",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LastCountedDate",
                table: "Streak",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Achievement_Code",
                table: "Achievement",
                column: "Code",
                unique: true);
        }
    }
}
