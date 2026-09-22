using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[DbContext(typeof(LearnerDbContext))]
[Migration("20270102090000_AddFreeTierFeaturedContent")]
public partial class AddFreeTierFeaturedContent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FreeWritingScenarioByProfessionJson",
            table: "FreeTierConfigs",
            type: "jsonb",
            nullable: false,
            defaultValue: "{}");

        migrationBuilder.AddColumn<string>(
            name: "FreeSpeakingCardByProfessionJson",
            table: "FreeTierConfigs",
            type: "jsonb",
            nullable: false,
            defaultValue: "{}");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FreeWritingScenarioByProfessionJson",
            table: "FreeTierConfigs");

        migrationBuilder.DropColumn(
            name: "FreeSpeakingCardByProfessionJson",
            table: "FreeTierConfigs");
    }
}
