using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// W5 — <c>AiResultCaches</c> shared by Listening explanations (this wave)
    /// and Reading (W9). Additive only; snapshot untouched.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261201090000_AddAiResultCache")]
    public partial class AddAiResultCache : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "AiResultCaches" (
                    "Id" character varying(64) NOT NULL,
                    "CacheKey" character varying(64) NOT NULL,
                    "FeatureCode" character varying(64) NOT NULL,
                    "Module" character varying(32) NOT NULL,
                    "PromptVersion" character varying(64),
                    "RulebookVersion" character varying(64),
                    "ResourceVersion" character varying(64),
                    "PayloadJson" text NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "LastServedAt" timestamp with time zone NOT NULL,
                    "ExpiresAt" timestamp with time zone,
                    "ServedCount" integer NOT NULL,
                    CONSTRAINT "PK_AiResultCaches" PRIMARY KEY ("Id")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_AiResultCaches_CacheKey"
                    ON "AiResultCaches" ("CacheKey");

                CREATE INDEX IF NOT EXISTS "IX_AiResultCaches_ExpiresAt"
                    ON "AiResultCaches" ("ExpiresAt");

                CREATE INDEX IF NOT EXISTS "IX_AiResultCaches_Feature_Module"
                    ON "AiResultCaches" ("FeatureCode", "Module");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiResultCaches");
        }
    }
}
