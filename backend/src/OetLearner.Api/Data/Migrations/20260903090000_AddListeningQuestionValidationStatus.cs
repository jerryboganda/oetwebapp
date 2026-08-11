using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Adds the Section 12 per-question validation status and reviewer note to the
/// relational Listening projection. Existing rows intentionally start as
/// draft; an owner-authorised content workflow must explicitly promote them
/// before a paper can be published or republished.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260903090000_AddListeningQuestionValidationStatus")]
public partial class AddListeningQuestionValidationStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ValidationStatus",
            table: "ListeningQuestions",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "draft");

        migrationBuilder.AddColumn<string>(
            name: "ValidationNote",
            table: "ListeningQuestions",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ValidationStatus",
            table: "ListeningQuestions");

        migrationBuilder.DropColumn(
            name: "ValidationNote",
            table: "ListeningQuestions");
    }
}
