using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds the Part A AI-marking columns to <c>ListeningAnswers</c>: a per-gap
    /// AI verdict + one-line rationale (Claude Sonnet 4.6), the scored-at
    /// timestamp (also the idempotency guard — the scorer only processes rows
    /// where it is null), and the model id. Advisory only; the deterministic
    /// grade (<c>IsCorrect</c>/<c>PointsEarned</c>) stays authoritative. Additive,
    /// non-destructive, backfills as null.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260710090000_AddListeningAnswerAiVerdict")]
    public partial class AddListeningAnswerAiVerdict : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiVerdict",
                table: "ListeningAnswers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiRationale",
                table: "ListeningAnswers",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AiScoredAt",
                table: "ListeningAnswers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiModel",
                table: "ListeningAnswers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AiVerdict", table: "ListeningAnswers");
            migrationBuilder.DropColumn(name: "AiRationale", table: "ListeningAnswers");
            migrationBuilder.DropColumn(name: "AiScoredAt", table: "ListeningAnswers");
            migrationBuilder.DropColumn(name: "AiModel", table: "ListeningAnswers");
        }
    }
}
