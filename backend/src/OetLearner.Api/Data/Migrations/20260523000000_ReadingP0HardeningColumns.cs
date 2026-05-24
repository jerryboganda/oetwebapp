using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260523000000_ReadingP0HardeningColumns")]
    /// <summary>
    /// P0 hardening - 2026-05-23. Adds the columns and column-size limits
    /// the entity model already declares but the prior ReadingP0Hardening
    /// migration did not emit (the snapshot was regenerated after the entity
    /// changes landed, so the diff was clipped).
    ///
    /// Up:
    ///  - <c>ReadingAttempts.RowVersion</c> (NOT NULL, default 0) for
    ///    optimistic-concurrency on concurrent submit/sweep grading.
    ///  - <c>ReadingAnswers.CreatedAt / UpdatedAt</c> (NOT NULL, default
    ///    NOW()) for audit + retention queries.
    ///  - Tighten unbounded TEXT columns to bounded VARCHAR(N) on
    ///    ReadingTexts, ReadingQuestions, ReadingAttempts, ReadingAnswers
    ///    so payload-inflation DoS is rejected at the database boundary.
    ///
    /// Down: reverses each operation.
    /// </summary>
    public partial class ReadingP0HardeningColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── ReadingAttempts.RowVersion (concurrency token) ────────────
            migrationBuilder.AddColumn<int>(
                name: "RowVersion",
                table: "ReadingAttempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // ── ReadingAnswers audit timestamps ───────────────────────────
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ReadingAnswers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "ReadingAnswers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            // ── ReadingTexts.BodyHtml: text → varchar(65536) ──────────────
            migrationBuilder.AlterColumn<string>(
                name: "BodyHtml",
                table: "ReadingTexts",
                type: "character varying(65536)",
                maxLength: 65536,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            // ── ReadingQuestions JSON columns ─────────────────────────────
            migrationBuilder.AlterColumn<string>(
                name: "OptionsJson",
                table: "ReadingQuestions",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CorrectAnswerJson",
                table: "ReadingQuestions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "AcceptedSynonymsJson",
                table: "ReadingQuestions",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OptionDistractorsJson",
                table: "ReadingQuestions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            // ── ReadingAttempts JSON columns ──────────────────────────────
            migrationBuilder.AlterColumn<string>(
                name: "PolicySnapshotJson",
                table: "ReadingAttempts",
                type: "character varying(16384)",
                maxLength: 16384,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeJson",
                table: "ReadingAttempts",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            // ── ReadingAnswers.UserAnswerJson ─────────────────────────────
            migrationBuilder.AlterColumn<string>(
                name: "UserAnswerJson",
                table: "ReadingAnswers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse VARCHAR(N) → text
            migrationBuilder.AlterColumn<string>(
                name: "UserAnswerJson",
                table: "ReadingAnswers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeJson",
                table: "ReadingAttempts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8192)",
                oldMaxLength: 8192,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PolicySnapshotJson",
                table: "ReadingAttempts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16384)",
                oldMaxLength: 16384);

            migrationBuilder.AlterColumn<string>(
                name: "OptionDistractorsJson",
                table: "ReadingQuestions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AcceptedSynonymsJson",
                table: "ReadingQuestions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CorrectAnswerJson",
                table: "ReadingQuestions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<string>(
                name: "OptionsJson",
                table: "ReadingQuestions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096);

            migrationBuilder.AlterColumn<string>(
                name: "BodyHtml",
                table: "ReadingTexts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(65536)",
                oldMaxLength: 65536);

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ReadingAnswers");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "ReadingAnswers");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ReadingAttempts");
        }
    }
}
