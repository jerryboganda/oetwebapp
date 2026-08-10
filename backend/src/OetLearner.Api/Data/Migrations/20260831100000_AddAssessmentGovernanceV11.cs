using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Focused v1.1 Listening/Reading governance migration. This migration is
/// intentionally hand-scoped because the repository snapshot contains older,
/// unrelated model drift; it must not replay those unrelated changes here.
/// </summary>
public partial class AddAssessmentGovernanceV11 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "MarkingPolicyVersionId",
            table: "Attempts",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PolicySnapshotJson",
            table: "Attempts",
            type: "character varying(16384)",
            maxLength: 16384,
            nullable: false,
            defaultValue: "{}");
        migrationBuilder.AddColumn<string>(
            name: "MarkingPolicyVersionId",
            table: "ListeningAttempts",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScoreConversionGrade",
            table: "ListeningAttempts",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "ScoreConversionPassed",
            table: "ListeningAttempts",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScoreConversionTableId",
            table: "ListeningAttempts",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScoreConversionTableVersionKey",
            table: "ListeningAttempts",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MarkingPolicyVersionId",
            table: "ReadingAttempts",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScoreConversionGrade",
            table: "ReadingAttempts",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "ScoreConversionPassed",
            table: "ReadingAttempts",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScoreConversionTableId",
            table: "ReadingAttempts",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScoreConversionTableVersionKey",
            table: "ReadingAttempts",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "RawScore",
            table: "Evaluations",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "MaxRawScore",
            table: "Evaluations",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ScaledScore",
            table: "Evaluations",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScoreConversionTableVersionKey",
            table: "Evaluations",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScoreConversionGrade",
            table: "Evaluations",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "ScoreConversionPassed",
            table: "Evaluations",
            type: "boolean",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "AssessmentMarkingPolicyVersions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Assessment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                ScopeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                VersionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PolicyJson = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ApprovedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                HasBeenUsed = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_AssessmentMarkingPolicyVersions", x => x.Id));

        migrationBuilder.CreateTable(
            name: "AssessmentScoreConversionTables",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Assessment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                ScopeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                VersionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ApprovedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                LockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                HasBeenUsed = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_AssessmentScoreConversionTables", x => x.Id));

        migrationBuilder.CreateTable(
            name: "AssessmentReMarkJobs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Assessment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                QuestionRevisionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Reason = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                OriginalKeySnapshotJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                NewKeySnapshotJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                RequestedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ApprovedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                AffectedAttemptIdsJson = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_AssessmentReMarkJobs", x => x.Id));

        migrationBuilder.CreateTable(
            name: "AssessmentRationales",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Assessment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                QuestionRevisionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SourceSentence = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                RationaleText = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                EvidenceCount = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ApprovedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_AssessmentRationales", x => x.Id));

        migrationBuilder.CreateTable(
            name: "AssessmentScoreConversionRows",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TableId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RawScore = table.Column<int>(type: "integer", nullable: false),
                ConvertedScore = table.Column<int>(type: "integer", nullable: false),
                Grade = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                Passed = table.Column<bool>(type: "boolean", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssessmentScoreConversionRows", x => x.Id);
                table.ForeignKey(
                    name: "FK_AssessmentScoreConversionRows_AssessmentScoreConversionTables_TableId",
                    column: x => x.TableId,
                    principalTable: "AssessmentScoreConversionTables",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "UX_AssessmentMarkingPolicy_Assessment_Scope_Version",
            table: "AssessmentMarkingPolicyVersions",
            columns: new[] { "Assessment", "ScopeKey", "VersionKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_AssessmentScoreConversionTable_Assessment_Scope_Version",
            table: "AssessmentScoreConversionTables",
            columns: new[] { "Assessment", "ScopeKey", "VersionKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AssessmentReMarkJob_Assessment_Status_CreatedAt",
            table: "AssessmentReMarkJobs",
            columns: new[] { "Assessment", "Status", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "UX_AssessmentRationale_Assessment_QuestionRevision",
            table: "AssessmentRationales",
            columns: new[] { "Assessment", "QuestionRevisionId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_AssessmentScoreConversionRow_Table_RawScore",
            table: "AssessmentScoreConversionRows",
            columns: new[] { "TableId", "RawScore" },
            unique: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_AssessmentScoreConversionRow_RawScore",
            table: "AssessmentScoreConversionRows",
            sql: "\"RawScore\" BETWEEN 0 AND 42");

        migrationBuilder.AddCheckConstraint(
            name: "CK_AssessmentScoreConversionRow_ConvertedScore",
            table: "AssessmentScoreConversionRows",
            sql: "\"ConvertedScore\" BETWEEN 0 AND 500");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AssessmentScoreConversionRows");
        migrationBuilder.DropTable(name: "AssessmentRationales");
        migrationBuilder.DropTable(name: "AssessmentReMarkJobs");
        migrationBuilder.DropTable(name: "AssessmentScoreConversionTables");
        migrationBuilder.DropTable(name: "AssessmentMarkingPolicyVersions");
        migrationBuilder.DropColumn(name: "PolicySnapshotJson", table: "Attempts");
        migrationBuilder.DropColumn(name: "MarkingPolicyVersionId", table: "Attempts");

        migrationBuilder.DropColumn(name: "MarkingPolicyVersionId", table: "ListeningAttempts");
        migrationBuilder.DropColumn(name: "ScoreConversionGrade", table: "ListeningAttempts");
        migrationBuilder.DropColumn(name: "ScoreConversionPassed", table: "ListeningAttempts");
        migrationBuilder.DropColumn(name: "ScoreConversionTableId", table: "ListeningAttempts");
        migrationBuilder.DropColumn(name: "ScoreConversionTableVersionKey", table: "ListeningAttempts");
        migrationBuilder.DropColumn(name: "MarkingPolicyVersionId", table: "ReadingAttempts");
        migrationBuilder.DropColumn(name: "ScoreConversionGrade", table: "ReadingAttempts");
        migrationBuilder.DropColumn(name: "ScoreConversionPassed", table: "ReadingAttempts");
        migrationBuilder.DropColumn(name: "ScoreConversionTableId", table: "ReadingAttempts");
        migrationBuilder.DropColumn(name: "ScoreConversionTableVersionKey", table: "ReadingAttempts");
        migrationBuilder.DropColumn(name: "RawScore", table: "Evaluations");
        migrationBuilder.DropColumn(name: "MaxRawScore", table: "Evaluations");
        migrationBuilder.DropColumn(name: "ScaledScore", table: "Evaluations");
        migrationBuilder.DropColumn(name: "ScoreConversionTableVersionKey", table: "Evaluations");
        migrationBuilder.DropColumn(name: "ScoreConversionGrade", table: "Evaluations");
        migrationBuilder.DropColumn(name: "ScoreConversionPassed", table: "Evaluations");
    }
}
