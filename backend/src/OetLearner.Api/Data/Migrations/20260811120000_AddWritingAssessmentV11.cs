using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[Migration("20260811120000_AddWritingAssessmentV11")]
public partial class AddWritingAssessmentV11 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WritingAssessmentReportsV11",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LetterType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RulePackVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CalibrationSetVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                OriginalLetterHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                OriginalLetterSnapshot = table.Column<string>(type: "text", nullable: false),
                TaskSnapshot = table.Column<string>(type: "text", nullable: false),
                CaseNotesSnapshot = table.Column<string>(type: "text", nullable: false),
                ClassificationJson = table.Column<string>(type: "jsonb", nullable: true),
                FeatureRecordJson = table.Column<string>(type: "jsonb", nullable: true),
                TopPrioritiesJson = table.Column<string>(type: "jsonb", nullable: false),
                StrengthsJson = table.Column<string>(type: "jsonb", nullable: false),
                StudyPlanJson = table.Column<string>(type: "jsonb", nullable: false),
                PurposeScore = table.Column<short>(type: "smallint", nullable: true),
                ContentScore = table.Column<short>(type: "smallint", nullable: true),
                ConcisenessClarityScore = table.Column<short>(type: "smallint", nullable: true),
                GenreStyleScore = table.Column<short>(type: "smallint", nullable: true),
                OrganisationLayoutScore = table.Column<short>(type: "smallint", nullable: true),
                LanguageScore = table.Column<short>(type: "smallint", nullable: true),
                EstimatedPracticeScore = table.Column<int>(type: "integer", nullable: true),
                ScoreRange = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                ConfidenceLabel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                ConfidenceRange = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CandidateNumericScoreEnabled = table.Column<bool>(type: "boolean", nullable: false),
                CandidateReportVisible = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WritingAssessmentReportsV11", x => x.Id);
                table.ForeignKey("FK_WritingAssessmentReportsV11_WritingSubmissions_SubmissionId", x => x.SubmissionId, "WritingSubmissions", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "WritingAssessmentReleaseGates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CalibrationSetVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                CandidateNumericScoreEnabled = table.Column<bool>(type: "boolean", nullable: false),
                MeanAbsoluteError = table.Column<decimal>(type: "numeric", nullable: true),
                ContentConcisenessCorrelation = table.Column<decimal>(type: "numeric", nullable: true),
                LanguageCorrelation = table.Column<decimal>(type: "numeric", nullable: true),
                InventedClaimRate = table.Column<decimal>(type: "numeric", nullable: true),
                OwnerApprovedTolerance = table.Column<decimal>(type: "numeric", nullable: true),
                QualifiedReviewerCount = table.Column<int>(type: "integer", nullable: false),
                HumanRatingsPerBenchmark = table.Column<int>(type: "integer", nullable: false),
                ApprovalEvidenceJson = table.Column<string>(type: "jsonb", nullable: true),
                ApprovedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_WritingAssessmentReleaseGates", x => x.Id));

        migrationBuilder.CreateTable(
            name: "WritingAssessmentPackVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Profession = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LetterType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                VersionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                CandidateFacing = table.Column<bool>(type: "boolean", nullable: false),
                RulesJson = table.Column<string>(type: "jsonb", nullable: false),
                ApprovalEvidenceJson = table.Column<string>(type: "jsonb", nullable: true),
                ApprovedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_WritingAssessmentPackVersions", x => x.Id));

        migrationBuilder.CreateTable(
            name: "WritingAssessmentFacts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                Classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                FactText = table.Column<string>(type: "text", nullable: false),
                SourceReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CandidateStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CandidateExcerpt = table.Column<string>(type: "text", nullable: true),
                Explanation = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WritingAssessmentFacts", x => x.Id);
                table.ForeignKey("FK_WritingAssessmentFacts_WritingAssessmentReportsV11_ReportId", x => x.ReportId, "WritingAssessmentReportsV11", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "WritingAssessmentErrors",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Location = table.Column<string>(type: "text", nullable: true),
                CandidateWording = table.Column<string>(type: "text", nullable: true),
                Correction = table.Column<string>(type: "text", nullable: true),
                RuleSource = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                WhyItMatters = table.Column<string>(type: "text", nullable: true),
                Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Confidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                PrimaryCriterionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                SecondaryCriterionCodesJson = table.Column<string>(type: "jsonb", nullable: false),
                StartOffset = table.Column<int>(type: "integer", nullable: true),
                EndOffset = table.Column<int>(type: "integer", nullable: true),
                IsGroupedDuplicate = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WritingAssessmentErrors", x => x.Id);
                table.ForeignKey("FK_WritingAssessmentErrors_WritingAssessmentReportsV11_ReportId", x => x.ReportId, "WritingAssessmentReportsV11", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "WritingAssessmentCriteria",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                CriterionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Score = table.Column<short>(type: "smallint", nullable: false),
                MaximumScore = table.Column<short>(type: "smallint", nullable: false),
                StrengthObservation = table.Column<string>(type: "text", nullable: false),
                LimitationObservation = table.Column<string>(type: "text", nullable: false),
                EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                ImprovementAction = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WritingAssessmentCriteria", x => x.Id);
                table.ForeignKey("FK_WritingAssessmentCriteria_WritingAssessmentReportsV11_ReportId", x => x.ReportId, "WritingAssessmentReportsV11", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "WritingAssessmentModelAnswers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                IsCandidateVisible = table.Column<bool>(type: "boolean", nullable: false),
                ModelAnswerText = table.Column<string>(type: "text", nullable: true),
                CorrectedCandidateLetter = table.Column<string>(type: "text", nullable: true),
                WhyThisWorksJson = table.Column<string>(type: "jsonb", nullable: true),
                GroundedFactReferencesJson = table.Column<string>(type: "jsonb", nullable: false),
                HoldReason = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WritingAssessmentModelAnswers", x => x.Id);
                table.ForeignKey("FK_WritingAssessmentModelAnswers_WritingAssessmentReportsV11_ReportId", x => x.ReportId, "WritingAssessmentReportsV11", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_WritingAssessmentReportsV11_SubmissionId", table: "WritingAssessmentReportsV11", column: "SubmissionId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_WritingAssessmentReportsV11_Status_CreatedAt", table: "WritingAssessmentReportsV11", columns: new[] { "Status", "CreatedAt" });
        migrationBuilder.CreateIndex(name: "IX_WritingAssessmentReleaseGates_ModelVersion_CalibrationSetVersion", table: "WritingAssessmentReleaseGates", columns: new[] { "ModelVersion", "CalibrationSetVersion" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_WritingAssessmentReleaseGates_Status", table: "WritingAssessmentReleaseGates", column: "Status");
        migrationBuilder.CreateIndex(name: "IX_WritingAssessmentPackVersions_Profession_LetterType_VersionKey", table: "WritingAssessmentPackVersions", columns: new[] { "Profession", "LetterType", "VersionKey" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_WritingAssessmentPackVersions_Profession_LetterType_Status", table: "WritingAssessmentPackVersions", columns: new[] { "Profession", "LetterType", "Status" });
        migrationBuilder.CreateIndex(name: "IX_WritingAssessmentFacts_ReportId_Classification", table: "WritingAssessmentFacts", columns: new[] { "ReportId", "Classification" });
        migrationBuilder.CreateIndex(name: "IX_WritingAssessmentErrors_ReportId_PrimaryCriterionCode", table: "WritingAssessmentErrors", columns: new[] { "ReportId", "PrimaryCriterionCode" });
        migrationBuilder.CreateIndex(name: "IX_WritingAssessmentErrors_ReportId_Severity", table: "WritingAssessmentErrors", columns: new[] { "ReportId", "Severity" });
        migrationBuilder.CreateIndex(name: "IX_WritingAssessmentCriteria_ReportId_CriterionCode", table: "WritingAssessmentCriteria", columns: new[] { "ReportId", "CriterionCode" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_WritingAssessmentModelAnswers_ReportId", table: "WritingAssessmentModelAnswers", column: "ReportId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_WritingAssessmentModelAnswers_Status", table: "WritingAssessmentModelAnswers", column: "Status");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WritingAssessmentErrors");
        migrationBuilder.DropTable(name: "WritingAssessmentFacts");
        migrationBuilder.DropTable(name: "WritingAssessmentCriteria");
        migrationBuilder.DropTable(name: "WritingAssessmentModelAnswers");
        migrationBuilder.DropTable(name: "WritingAssessmentPackVersions");
        migrationBuilder.DropTable(name: "WritingAssessmentReleaseGates");
        migrationBuilder.DropTable(name: "WritingAssessmentReportsV11");
    }
}
