using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260621090000_AddReadingQuestionBoxExplanations")]
    public partial class AddReadingQuestionBoxExplanations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""ReadingQuestions"" ADD COLUMN IF NOT EXISTS ""BoxExplanationsJson"" character varying(4096) NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""ReadingQuestions"" DROP COLUMN IF EXISTS ""BoxExplanationsJson"";");
        }
    }
}
