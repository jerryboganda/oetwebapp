using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// W6 — Writing submission idempotency, grade-reuse identity, claim owner,
    /// and persist-before-commit provider result.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261203090000_AddWritingSubmissionIdempotency")]
    public partial class AddWritingSubmissionIdempotency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "WritingSubmissions" ADD COLUMN IF NOT EXISTS "IdempotencyKey" character varying(128);
                ALTER TABLE "WritingSubmissions" ADD COLUMN IF NOT EXISTS "ReuseKeyHash" character varying(64);
                ALTER TABLE "WritingSubmissions" ADD COLUMN IF NOT EXISTS "GradeOperationId" character varying(64);
                ALTER TABLE "WritingSubmissions" ADD COLUMN IF NOT EXISTS "ClaimedAt" timestamp with time zone;
                ALTER TABLE "WritingSubmissions" ADD COLUMN IF NOT EXISTS "ClaimOwner" character varying(128);
                ALTER TABLE "WritingSubmissions" ADD COLUMN IF NOT EXISTS "ProviderResultJson" text;

                UPDATE "WritingSubmissions"
                SET "IdempotencyKey" = "Id"::text
                WHERE "IdempotencyKey" IS NULL;

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_WritingSubmissions_User_IdempotencyKey"
                    ON "WritingSubmissions" ("UserId", "IdempotencyKey")
                    WHERE "IdempotencyKey" IS NOT NULL;

                CREATE INDEX IF NOT EXISTS "IX_WritingSubmissions_ReuseKeyHash"
                    ON "WritingSubmissions" ("ReuseKeyHash");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "UX_WritingSubmissions_User_IdempotencyKey";
                DROP INDEX IF EXISTS "IX_WritingSubmissions_ReuseKeyHash";
                ALTER TABLE "WritingSubmissions" DROP COLUMN IF EXISTS "IdempotencyKey";
                ALTER TABLE "WritingSubmissions" DROP COLUMN IF EXISTS "ReuseKeyHash";
                ALTER TABLE "WritingSubmissions" DROP COLUMN IF EXISTS "GradeOperationId";
                ALTER TABLE "WritingSubmissions" DROP COLUMN IF EXISTS "ClaimedAt";
                ALTER TABLE "WritingSubmissions" DROP COLUMN IF EXISTS "ClaimOwner";
                ALTER TABLE "WritingSubmissions" DROP COLUMN IF EXISTS "ProviderResultJson";
                """);
        }
    }
}
