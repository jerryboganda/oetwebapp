using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// W0 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01).
    ///
    /// Adds the bookkeeping columns the Listening Part A advisory scorer needs to
    /// reach a terminal state instead of being re-selected by the 20 s worker for
    /// ever:
    /// <list type="bullet">
    ///   <item><c>AiSkipReason</c> — terminal reason (<c>skipped_no_evidence</c>,
    ///   <c>credential_quarantined</c>, …). Non-null excludes the row from the
    ///   worker query. <c>AiScoredAt</c> stays null: a skip is not an AI score.</item>
    ///   <item><c>AiAttemptCount</c> — durable count of provider attempts, with
    ///   at most three scheduled per answer.</item>
    ///   <item><c>AiNextAttemptAt</c> — earliest retry instant (Retry-After or
    ///   jittered backoff).</item>
    ///   <item><c>AiIncidentId</c> — operational incident tag applied by
    ///   <c>scripts/ops/close-listening-incident-attempts.sql</c>. Tag only.</item>
    /// </list>
    ///
    /// Strictly additive and non-destructive: no existing column is renamed,
    /// dropped or rewritten, and the deterministic mark (<c>IsCorrect</c> /
    /// <c>PointsEarned</c>) is not referenced. <c>ADD COLUMN … NOT NULL DEFAULT 0</c>
    /// is metadata-only on PostgreSQL 11+, so no table rewrite and no long lock.
    /// Hand-authored per repo convention: inline <c>[Migration]</c>/<c>[DbContext]</c>,
    /// no Designer file, ModelSnapshot deliberately untouched.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261101090000_AddListeningAnswerAiSkipAndRetry")]
    public partial class AddListeningAnswerAiSkipAndRetry : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiSkipReason",
                table: "ListeningAnswers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiAttemptCount",
                table: "ListeningAnswers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AiNextAttemptAt",
                table: "ListeningAnswers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiIncidentId",
                table: "ListeningAnswers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AiSkipReason", table: "ListeningAnswers");
            migrationBuilder.DropColumn(name: "AiAttemptCount", table: "ListeningAnswers");
            migrationBuilder.DropColumn(name: "AiNextAttemptAt", table: "ListeningAnswers");
            migrationBuilder.DropColumn(name: "AiIncidentId", table: "ListeningAnswers");
        }
    }
}
