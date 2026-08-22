using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExercisePersonalisation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExerciseProfileState",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsFavourite = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastUsedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseProfileState", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseProfileState_ExerciseId",
                table: "ExerciseProfileState",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseProfileState_UserProfileId_ExerciseId",
                table: "ExerciseProfileState",
                columns: new[] { "UserProfileId", "ExerciseId" },
                unique: true);

            // ---------------------------------------------------------------------------------
            // Backfill. Hand-written, and it has to run before the columns below are dropped.
            //
            // The scaffolded migration dropped Exercise.IsFavourite and Exercise.LastUsedUtc
            // first and created this table empty, which would silently discard every favourite
            // and every "recently used" marker on the device. EF warned about data loss; this is
            // what the warning was about.
            //
            // Attribution follows the rule ProfileOwnership established: only when the device has
            // exactly one profile that is not deleted, which is every device today. With none or
            // several, nothing is attributed - the favourites are simply not carried over, which
            // is a visibly empty shortlist rather than somebody else's shortlist.
            //
            // A row is written only for an exercise the user actually expressed something about.
            // Seeding one per exercise would multiply the shipped catalogue by the profile count
            // and store nothing.
            //
            // The identifier is a random 128-bit value rather than a GUID v7. SQLite cannot
            // generate a time-ordered one, and nothing reads ordering off this key - the real
            // invariant is the unique index on (UserProfileId, ExerciseId), which is enforced.
            //
            // CreatedUtc and ModifiedUtc are copied from the exercise rather than computed from
            // 'now'. The exact text encoding EF uses for a DateTimeOffset on SQLite is a provider
            // detail, and writing it wrongly here would not fail the migration - it would throw
            // when the library was next read. Copying a value the provider already wrote cannot
            // be in the wrong format.
            // ---------------------------------------------------------------------------------
            migrationBuilder.Sql("""
                INSERT INTO "ExerciseProfileState"
                    ("Id", "UserProfileId", "ExerciseId", "IsFavourite", "LastUsedUtc", "CreatedUtc", "ModifiedUtc", "DeletedUtc")
                SELECT lower(
                           substr(hex(randomblob(4)), 1, 8) || '-' ||
                           substr(hex(randomblob(2)), 1, 4) || '-' ||
                           substr(hex(randomblob(2)), 1, 4) || '-' ||
                           substr(hex(randomblob(2)), 1, 4) || '-' ||
                           substr(hex(randomblob(6)), 1, 12)),
                       (SELECT "Id" FROM "UserProfile" WHERE "DeletedUtc" IS NULL),
                       "Id",
                       "IsFavourite",
                       "LastUsedUtc",
                       "ModifiedUtc",
                       "ModifiedUtc",
                       NULL
                  FROM "Exercise"
                 WHERE "DeletedUtc" IS NULL
                   AND ("IsFavourite" = 1 OR "LastUsedUtc" IS NOT NULL)
                   AND (SELECT COUNT(*) FROM "UserProfile" WHERE "DeletedUtc" IS NULL) = 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Exercise_IsFavourite",
                table: "Exercise");

            migrationBuilder.DropIndex(
                name: "IX_Exercise_LastUsedUtc",
                table: "Exercise");

            migrationBuilder.DropColumn(
                name: "IsFavourite",
                table: "Exercise");

            migrationBuilder.DropColumn(
                name: "LastUsedUtc",
                table: "Exercise");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFavourite",
                table: "Exercise",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastUsedUtc",
                table: "Exercise",
                type: "TEXT",
                nullable: true);

            // Carry the state back onto the shared row before the table holding it goes away.
            // Going down necessarily collapses several profiles' opinions into one column, so the
            // active profile's state wins and the rest is lost - which is the shape of the bug
            // this migration exists to fix, and is why down is a recovery path and not a supported
            // downgrade.
            migrationBuilder.Sql("""
                UPDATE "Exercise"
                   SET "IsFavourite" = COALESCE((
                           SELECT "IsFavourite" FROM "ExerciseProfileState"
                            WHERE "ExerciseProfileState"."ExerciseId" = "Exercise"."Id"
                              AND "ExerciseProfileState"."DeletedUtc" IS NULL
                            ORDER BY "ExerciseProfileState"."ModifiedUtc" DESC
                            LIMIT 1), 0),
                       "LastUsedUtc" = (
                           SELECT "LastUsedUtc" FROM "ExerciseProfileState"
                            WHERE "ExerciseProfileState"."ExerciseId" = "Exercise"."Id"
                              AND "ExerciseProfileState"."DeletedUtc" IS NULL
                            ORDER BY "ExerciseProfileState"."ModifiedUtc" DESC
                            LIMIT 1);
                """);

            migrationBuilder.DropTable(
                name: "ExerciseProfileState");

            migrationBuilder.CreateIndex(
                name: "IX_Exercise_IsFavourite",
                table: "Exercise",
                column: "IsFavourite");

            migrationBuilder.CreateIndex(
                name: "IX_Exercise_LastUsedUtc",
                table: "Exercise",
                column: "LastUsedUtc");
        }
    }
}
