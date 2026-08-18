using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Captures the owner-approved score-conversion table selection at attempt
/// start. A null table id plus an error code is intentional: the attempt may
/// still produce an auditable raw-only result, but it must not adopt a table
/// that becomes effective later.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260902090000_AddAssessmentScoreConversionAttemptSnapshots")]
public partial class AddAssessmentScoreConversionAttemptSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ScoreConversionSnapshotJson",
            table: "Attempts",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScoreConversionSnapshotJson",
            table: "ListeningAttempts",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScoreConversionSnapshotJson",
            table: "ReadingAttempts",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ScoreConversionSnapshotJson",
            table: "Attempts");

        migrationBuilder.DropColumn(
            name: "ScoreConversionSnapshotJson",
            table: "ListeningAttempts");

        migrationBuilder.DropColumn(
            name: "ScoreConversionSnapshotJson",
            table: "ReadingAttempts");
    }
}
