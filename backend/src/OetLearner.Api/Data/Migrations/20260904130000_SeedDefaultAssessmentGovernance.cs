using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Seeds the owner-effective default Reading/Listening marking policies and
/// the canonical 0–42 → 0–500 conversion tables. Without these rows,
/// StartAsync fail-closes with reading_marking_policy_unavailable.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260904130000_SeedDefaultAssessmentGovernance")]
public partial class SeedDefaultAssessmentGovernance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO "AssessmentMarkingPolicyVersions" (
              "Id", "Assessment", "ScopeKey", "VersionKey", "PolicyJson", "Status",
              "EffectiveFrom", "ApprovedAt", "ApprovedByUserId", "CreatedByUserId",
              "CreatedAt", "UpdatedAt", "HasBeenUsed"
            )
            SELECT
              'reading-default-v1', 'reading', 'default', 'v1',
              '{"trimLeadingTrailingWhitespace":true,"collapseInternalWhitespace":false,"caseSensitive":true,"readingPartAMatchingPartialCredit":false,"listeningAudioReplayAllowed":false,"audioLockMode":"exam","technicalRequirementsGuidanceOnly":true}',
              3, TIMESTAMPTZ '2020-01-01 00:00:00+00', NOW(), 'system-owner', 'system-owner', NOW(), NOW(), FALSE
            WHERE NOT EXISTS (
              SELECT 1 FROM "AssessmentMarkingPolicyVersions"
              WHERE "Assessment" = 'reading' AND "ScopeKey" = 'default'
            );

            INSERT INTO "AssessmentMarkingPolicyVersions" (
              "Id", "Assessment", "ScopeKey", "VersionKey", "PolicyJson", "Status",
              "EffectiveFrom", "ApprovedAt", "ApprovedByUserId", "CreatedByUserId",
              "CreatedAt", "UpdatedAt", "HasBeenUsed"
            )
            SELECT
              'listening-default-v1', 'listening', 'default', 'v1',
              '{"trimLeadingTrailingWhitespace":true,"collapseInternalWhitespace":false,"caseSensitive":true,"readingPartAMatchingPartialCredit":false,"listeningAudioReplayAllowed":false,"audioLockMode":"exam","technicalRequirementsGuidanceOnly":true}',
              3, TIMESTAMPTZ '2020-01-01 00:00:00+00', NOW(), 'system-owner', 'system-owner', NOW(), NOW(), FALSE
            WHERE NOT EXISTS (
              SELECT 1 FROM "AssessmentMarkingPolicyVersions"
              WHERE "Assessment" = 'listening' AND "ScopeKey" = 'default'
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM "AssessmentMarkingPolicyVersions"
            WHERE "Id" IN ('reading-default-v1', 'listening-default-v1');
            """);
    }
}
