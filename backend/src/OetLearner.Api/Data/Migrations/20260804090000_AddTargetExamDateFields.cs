using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    // Mandatory exam-date plan (2026-07-22), Task A1 — schema foundation.
    //
    // Adds LearnerGoal.TargetExamDateSetByUser (distinguishes a real
    // candidate/admin-supplied exam date from the lazy "+3 months" placeholder
    // CreateDefaultGoal stamps on first touch) and
    // LearnerRegistrationProfile.TargetExamDate (collected at registration /
    // admin Add-User, read once when the learner's LearnerGoal row is lazily
    // created).
    //
    // HAND-AUTHORED (repo convention): `dotnet ef migrations add` diffs against
    // the intentionally-stale LearnerDbContextModelSnapshot and would re-emit
    // unrelated already-shipped schema. This migration contains ONLY the two
    // new columns. The model snapshot is left as-is; the runtime model comes
    // from the entity classes.
    //
    // NOTE: the task brief for this step assumed today's date (2026-07-22) as
    // the migration id, but the repo's migration history already reaches
    // 20260803090000_AddUserVideoAccess — using 20260722090000 would collide in
    // spirit with the same-day 20260722090000_AddListeningExtractContextIntro
    // migration and sort BEFORE several already-shipped migrations. Bumped the
    // id to 20260804090000 to keep it future-dated/ordered after the latest
    // existing migration, per this repo's hand-authored-migration convention.
    //
    // SAFETY: purely additive — two new nullable/defaulted columns. No
    // backfill, no destructive change; forward-only but Down() is provided.
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260804090000_AddTargetExamDateFields")]
    public partial class AddTargetExamDateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TargetExamDateSetByUser",
                table: "Goals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TargetExamDate",
                table: "LearnerRegistrationProfiles",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetExamDate",
                table: "LearnerRegistrationProfiles");

            migrationBuilder.DropColumn(
                name: "TargetExamDateSetByUser",
                table: "Goals");
        }
    }
}
