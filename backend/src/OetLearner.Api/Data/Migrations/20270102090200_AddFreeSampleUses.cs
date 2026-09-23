using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    // HAND-AUTHORED (repo convention): contains ONLY the new objects. Raw
    // idempotent SQL so a dev database created with EnsureCreated does not
    // fail with "already exists". The ModelSnapshot IS updated.
    // Free sample retry addendum (owner 23 Sep 2026): each learner gets TWO
    // successful AI-graded results per subtest on the SAME pinned item. One
    // FreeSampleUses row per attempt at the sample (ResourceId UNIQUE); the
    // claim gains a Version concurrency token so two racing binds for the last
    // slot resolve to exactly one winner. Every existing claim that was bound
    // to an attempt/submission is backfilled as that claim's first use.
    // +0200: 20270102090100 is taken twice already (free-sample claims and
    // PersistLiveVoiceContentProvenance).
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270102090200_AddFreeSampleUses")]
    public partial class AddFreeSampleUses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "FreeSampleClaims" ADD COLUMN IF NOT EXISTS "Version" integer NOT NULL DEFAULT 0;

                CREATE TABLE IF NOT EXISTS "FreeSampleUses" (
                    "Id" character varying(64) NOT NULL,
                    "ClaimId" character varying(64) NOT NULL,
                    "UserId" character varying(64) NOT NULL,
                    "Subtest" character varying(16) NOT NULL,
                    "ResourceKind" character varying(32) NOT NULL,
                    "ResourceId" character varying(64) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_FreeSampleUses" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_FreeSampleUses_FreeSampleClaims_ClaimId" FOREIGN KEY ("ClaimId")
                        REFERENCES "FreeSampleClaims" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS "IX_FreeSampleUses_ClaimId"
                    ON "FreeSampleUses" ("ClaimId");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_FreeSampleUses_ResourceId"
                    ON "FreeSampleUses" ("ResourceId");

                INSERT INTO "FreeSampleUses" ("Id", "ClaimId", "UserId", "Subtest", "ResourceKind", "ResourceId", "CreatedAt")
                SELECT 'fsu-' || md5(c."Id"),
                       c."Id",
                       c."UserId",
                       c."Subtest",
                       CASE WHEN c."Subtest" = 'writing' THEN 'writing_submission' ELSE 'legacy_attempt' END,
                       c."AttemptId",
                       c."UpdatedAt"
                FROM "FreeSampleClaims" c
                WHERE c."AttemptId" IS NOT NULL AND btrim(c."AttemptId") <> ''
                ON CONFLICT DO NOTHING;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FreeSampleUses");
            migrationBuilder.DropColumn(name: "Version", table: "FreeSampleClaims");
        }
    }
}
