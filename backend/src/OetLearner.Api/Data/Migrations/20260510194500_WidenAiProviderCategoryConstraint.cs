using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260510194500_WidenAiProviderCategoryConstraint")]
    public partial class WidenAiProviderCategoryConstraint : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"AiProviders\" DROP CONSTRAINT IF EXISTS \"CK_AiProviders_Category\";");

            migrationBuilder.Sql(
                "ALTER TABLE \"AiProviders\" " +
                "ADD CONSTRAINT \"CK_AiProviders_Category\" " +
                "CHECK (\"Category\" IN (0, 1, 2, 3, 4, 5));");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"AiProviders\" DROP CONSTRAINT IF EXISTS \"CK_AiProviders_Category\";");

            migrationBuilder.Sql(
                "ALTER TABLE \"AiProviders\" " +
                "ADD CONSTRAINT \"CK_AiProviders_Category\" " +
                "CHECK (\"Category\" IN (0, 1, 2, 3));");
        }
    }
}
