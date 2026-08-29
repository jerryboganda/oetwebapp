using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// W11 — additive <c>AiRawResponses</c> table. Snapshot untouched.
/// Vocabulary unique promotion is owner-run after duplicate merge
/// (<c>scripts/ops/apply-vocabulary-unique-index.sql</c>).
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20261208090000_AddAiRawResponseRetention")]
public partial class AddAiRawResponseRetention : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS "AiRawResponses" (
                "Id" character varying(64) NOT NULL,
                "OperationId" character varying(64),
                "FeatureCode" character varying(64) NOT NULL,
                "ProviderCode" character varying(64),
                "PayloadCiphertext" text,
                "PayloadSha256" character varying(64) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "ExpiresAt" timestamp with time zone NOT NULL,
                "PurgedAt" timestamp with time zone,
                CONSTRAINT "PK_AiRawResponses" PRIMARY KEY ("Id")
            );

            CREATE INDEX IF NOT EXISTS "IX_AiRawResponses_ExpiresAt"
                ON "AiRawResponses" ("ExpiresAt");

            CREATE INDEX IF NOT EXISTS "IX_AiRawResponses_OperationId"
                ON "AiRawResponses" ("OperationId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AiRawResponses");
    }
}
