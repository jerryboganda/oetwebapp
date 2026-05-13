using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260513140000_AddMockItemAnalysisDiscriminationIndex")]
    public partial class AddMockItemAnalysisDiscriminationIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "MockItemAnalysisSnapshots"
                ADD COLUMN IF NOT EXISTS "DiscriminationIndex" double precision NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "MockItemAnalysisSnapshots"
                ADD COLUMN IF NOT EXISTS "ContentPaperId" character varying(64) NOT NULL DEFAULT '';
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_MockItemAnalysisSnapshots_ContentPaperId"
                ON "MockItemAnalysisSnapshots" ("ContentPaperId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_MockItemAnalysisSnapshots_ContentPaperId";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "MockItemAnalysisSnapshots" DROP COLUMN IF EXISTS "ContentPaperId";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "MockItemAnalysisSnapshots" DROP COLUMN IF EXISTS "DiscriminationIndex";
                """);
        }
    }
}