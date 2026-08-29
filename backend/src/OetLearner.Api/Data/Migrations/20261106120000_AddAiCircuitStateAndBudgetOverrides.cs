using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// W3 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
    /// creates the circuit-breaker, budget-override, and budget-alert tables
    /// that back <c>AiCircuitBreakerStore</c>, <c>AiBudgetOverrideService</c>,
    /// and <c>AiBudgetAlertService</c>.
    ///
    /// <para>
    /// Strictly additive: three brand-new tables, zero existing tables touched.
    /// Hand-authored per repo convention: inline <c>[Migration]</c>/
    /// <c>[DbContext]</c>, no Designer file, ModelSnapshot deliberately
    /// untouched. <c>IF NOT EXISTS</c> guards on the table/indexes so this
    /// migration is replay-safe if both blue and green attempt
    /// <c>Database.MigrateAsync()</c> concurrently — matching
    /// <c>20261106090000_AddAiExplanationCache</c>.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261106120000_AddAiCircuitStateAndBudgetOverrides")]
    public partial class AddAiCircuitStateAndBudgetOverrides : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "AiCircuitStates" (
                    "Id" character varying(128) NOT NULL,
                    "Kind" character varying(32) NOT NULL,
                    "Key" character varying(128) NOT NULL,
                    "State" character varying(16) NOT NULL,
                    "FailureCount" integer NOT NULL,
                    "OpenedAt" timestamp with time zone NULL,
                    "OpenUntil" timestamp with time zone NULL,
                    "LastFailureAt" timestamp with time zone NULL,
                    "LastFailureCode" character varying(64) NULL,
                    "ProbeInFlight" boolean NOT NULL DEFAULT false,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_AiCircuitStates" PRIMARY KEY ("Id")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_AiCircuitStates_Kind_Key"
                    ON "AiCircuitStates" ("Kind", "Key");

                CREATE TABLE IF NOT EXISTS "AiBudgetOverrides" (
                    "Id" character varying(64) NOT NULL,
                    "Scope" character varying(64) NOT NULL,
                    "AmountUsd" numeric NOT NULL,
                    "Reason" character varying(512) NOT NULL,
                    "ActorAdminId" character varying(64) NOT NULL,
                    "ExpiresAt" timestamp with time zone NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "RevokedAt" timestamp with time zone NULL,
                    "RevokedByAdminId" character varying(64) NULL,
                    CONSTRAINT "PK_AiBudgetOverrides" PRIMARY KEY ("Id")
                );

                CREATE INDEX IF NOT EXISTS "IX_AiBudgetOverrides_Scope_ExpiresAt"
                    ON "AiBudgetOverrides" ("Scope", "ExpiresAt");

                CREATE TABLE IF NOT EXISTS "AiBudgetAlerts" (
                    "Id" character varying(64) NOT NULL,
                    "Scope" character varying(64) NOT NULL,
                    "PeriodKey" character varying(16) NOT NULL,
                    "ThresholdPct" integer NOT NULL,
                    "FiredAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_AiBudgetAlerts" PRIMARY KEY ("Id")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_AiBudgetAlerts_Scope_PeriodKey_ThresholdPct"
                    ON "AiBudgetAlerts" ("Scope", "PeriodKey", "ThresholdPct");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiBudgetAlerts");
            migrationBuilder.DropTable(name: "AiBudgetOverrides");
            migrationBuilder.DropTable(name: "AiCircuitStates");
        }
    }
}
