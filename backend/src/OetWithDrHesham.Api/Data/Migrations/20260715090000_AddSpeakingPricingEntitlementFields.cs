using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Speaking Module requirements gap audit (2026-07-01) — closes two gaps:
    ///
    /// 1. "Practice Card Access" (ai_self_practice) was not plan-tierable —
    ///    every plan behaved identically, metered only by the AI credit
    ///    wallet. <c>SpeakingPracticeAccessEnabled</c> lets an admin disable
    ///    self-practice for a specific plan. Defaults true so every existing
    ///    plan keeps today's behaviour unchanged.
    ///
    /// 2. "Full Mock Speaking Exam Access" was not distinct from "AI Speaking
    ///    Credits" — both self-practice and the 2-card AI exam drew from the
    ///    same <c>SpeakingOnlyCredits</c> wallet. <c>FundedByMockCredit</c>
    ///    records when an exam was instead funded from the account's
    ///    (pre-existing, previously Speaking-orphaned) <c>MockExamsRemaining</c>
    ///    allowance, so Card B's debit can skip re-charging.
    ///
    /// <para>
    /// Hand-written Postgres-only migration, following the established
    /// pattern (see 20260608000000_AddOet2026CatalogFlags.cs and the WS6
    /// note in docs/speaking/PROGRESS.md): the 25k-line EF model snapshot is
    /// deliberately not hand-edited here. SQLite/InMemory test runs bypass
    /// migrations via EnsureCreatedAsync() and pick up the change directly
    /// from entity attributes.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260715090000_AddSpeakingPricingEntitlementFields")]
    public partial class AddSpeakingPricingEntitlementFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""SpeakingPracticeAccessEnabled"" boolean NOT NULL DEFAULT true;
                ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""SpeakingPracticeAccessEnabled"" boolean NOT NULL DEFAULT true;
                ALTER TABLE ""SpeakingExamSessions"" ADD COLUMN IF NOT EXISTS ""FundedByMockCredit"" boolean NOT NULL DEFAULT false;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""SpeakingExamSessions"" DROP COLUMN IF EXISTS ""FundedByMockCredit"";
                ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""SpeakingPracticeAccessEnabled"";
                ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""SpeakingPracticeAccessEnabled"";
            ");
        }
    }
}
