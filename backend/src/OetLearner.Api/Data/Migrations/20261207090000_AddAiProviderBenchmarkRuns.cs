using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// W10 — additive <c>AiProviderBenchmarkRuns</c> table. Snapshot untouched.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20261207090000_AddAiProviderBenchmarkRuns")]
public partial class AddAiProviderBenchmarkRuns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS "AiProviderBenchmarkRuns" (
                "Id" character varying(64) NOT NULL,
                "FeatureCode" character varying(64) NOT NULL,
                "ProviderCode" character varying(64) NOT NULL,
                "Model" character varying(128) NOT NULL,
                "CorpusVersion" character varying(64) NOT NULL,
                "Class" integer NOT NULL,
                "SchemaValidityPct" numeric NOT NULL,
                "CitationCompliancePct" numeric NOT NULL,
                "ScoringGovernanceViolations" integer NOT NULL,
                "PassFailFlips" integer NOT NULL,
                "CriterionWithinOnePct" numeric NOT NULL,
                "MeanScaledAbsError" numeric NOT NULL,
                "EvidenceGroundingPct" numeric NOT NULL,
                "FabricatedSourceClaims" integer NOT NULL,
                "CostReductionPct" numeric NOT NULL,
                "Passed" boolean NOT NULL,
                "RollbackTargetRouteId" character varying(64),
                "RollbackProviderCode" character varying(64),
                "RollbackModel" character varying(128),
                "RecordedAt" timestamp with time zone NOT NULL,
                "ReportJson" text NOT NULL,
                CONSTRAINT "PK_AiProviderBenchmarkRuns" PRIMARY KEY ("Id")
            );

            CREATE INDEX IF NOT EXISTS "IX_AiProviderBenchmarkRuns_Feature_Provider_Recorded"
                ON "AiProviderBenchmarkRuns" ("FeatureCode", "ProviderCode", "RecordedAt");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AiProviderBenchmarkRuns");
    }
}
