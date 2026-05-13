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
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "MockItemAnalysisSnapshots" DROP COLUMN IF EXISTS "DiscriminationIndex";
                """);
        }
    }
}