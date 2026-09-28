using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Writing AI subscription provider settings (owner directive 2026-09-29).
    /// Adds the four WritingAi* columns to the RuntimeSettingsRow singleton that
    /// back the admin Writing AI provider control: mode (auto/claude/codex),
    /// warn/failover utilisation percentages, and the Claude quota-exhaustion
    /// marker that drives automatic failover until the weekly reset.
    /// Hand-authored (additive only, idempotent).
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261231093000_AddWritingAiProviderSettings")]
    public partial class AddWritingAiProviderSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "WritingAiProviderMode" character varying(16) NULL;
                ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "WritingAiWarnPct" double precision NULL;
                ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "WritingAiFailoverPct" double precision NULL;
                ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "WritingAiClaudeQuotaExceededUntil" timestamp with time zone NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "WritingAiProviderMode";
                ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "WritingAiWarnPct";
                ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "WritingAiFailoverPct";
                ALTER TABLE "RuntimeSettings" DROP COLUMN IF EXISTS "WritingAiClaudeQuotaExceededUntil";
                """);
        }
    }
}
