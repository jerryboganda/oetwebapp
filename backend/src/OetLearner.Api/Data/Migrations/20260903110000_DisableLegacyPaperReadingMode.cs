using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[DbContext(typeof(LearnerDbContext))]
[Migration("20260903110000_DisableLegacyPaperReadingMode")]
public partial class DisableLegacyPaperReadingMode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE \"ReadingPolicies\" SET \"AllowPaperReadingMode\" = FALSE;");
        migrationBuilder.AlterColumn<bool>(
            name: "AllowPaperReadingMode",
            table: "ReadingPolicies",
            type: "boolean",
            nullable: false,
            defaultValue: false,
            oldClrType: typeof(bool),
            oldType: "boolean",
            oldDefaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<bool>(
            name: "AllowPaperReadingMode",
            table: "ReadingPolicies",
            type: "boolean",
            nullable: false,
            defaultValue: true,
            oldClrType: typeof(bool),
            oldType: "boolean",
            oldDefaultValue: false);
    }
}
