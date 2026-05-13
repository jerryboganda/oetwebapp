using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260512103000_DefaultReadingPaperMode")]
    public partial class DefaultReadingPaperMode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "AllowPaperReadingMode",
                table: "ReadingPolicies",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.Sql("""
                UPDATE "ReadingPolicies"
                SET "AllowPaperReadingMode" = TRUE
                WHERE "Id" = 'global'
                  AND "AllowPaperReadingMode" = FALSE
                  AND "UpdatedByAdminId" IS NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "AllowPaperReadingMode",
                table: "ReadingPolicies",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);
        }
    }
}
