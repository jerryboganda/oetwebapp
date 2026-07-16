using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Access &amp; payment spec 2026-07-15 §1 + §2/§5/§6.6 — schema for the universal
    /// proof-of-payment record and per-plan delivery method.
    ///
    ///   * ManualPaymentRequests gains a Kind discriminator (learner_upload |
    ///     gateway_receipt) plus the gateway-receipt fields, the buyer's profession,
    ///     and the admin proof-waiver trio. ProofUrl becomes nullable — a gateway
    ///     receipt has no file. The entity/table keep their "manual" names on purpose:
    ///     prod is blue/green and a rename breaks the outgoing container mid-rollover.
    ///   * BillingPlans / BillingPlanVersions gain DeliveryMethod + the Telegram and
    ///     manual hand-over fields + ContentOverridesJson. The subtest axis of the
    ///     content model reuses the EXISTING IncludedSubtestsJson column.
    ///   * Subscriptions gains FulfilmentStatus, orthogonal to Status.
    ///
    /// HAND-AUTHORED (repo convention, see 20260726090000 / 20260727090000): `dotnet ef
    /// migrations add` diffs against the intentionally-stale LearnerDbContextModelSnapshot
    /// and would re-emit unrelated already-shipped schema. The snapshot is left as-is;
    /// the runtime model comes from the entity classes.
    ///
    /// SAFETY: additive only — new columns with defaults, plus one NOT NULL relaxation.
    /// Old containers keep working during blue/green rollover: they never read the new
    /// columns, and they always write a non-null ProofUrl. Backfills below are
    /// belt-and-braces (ADD COLUMN … DEFAULT already backfills on PG11+) and cover the
    /// case where a column already exists from a prior partial run.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260729090000_AddPaymentProofAndDeliveryMethod")]
    public partial class AddPaymentProofAndDeliveryMethod : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── §1 universal proof of payment ──
            migrationBuilder.Sql(@"
ALTER TABLE ""ManualPaymentRequests"" ADD COLUMN IF NOT EXISTS ""Kind"" character varying(24) NOT NULL DEFAULT 'learner_upload';
ALTER TABLE ""ManualPaymentRequests"" ADD COLUMN IF NOT EXISTS ""Gateway"" character varying(32) NULL;
ALTER TABLE ""ManualPaymentRequests"" ADD COLUMN IF NOT EXISTS ""PaymentTransactionId"" uuid NULL;
ALTER TABLE ""ManualPaymentRequests"" ADD COLUMN IF NOT EXISTS ""ProfessionId"" character varying(32) NULL;
ALTER TABLE ""ManualPaymentRequests"" ADD COLUMN IF NOT EXISTS ""ProofWaivedByAdminId"" character varying(64) NULL;
ALTER TABLE ""ManualPaymentRequests"" ADD COLUMN IF NOT EXISTS ""ProofWaivedAt"" timestamp with time zone NULL;
ALTER TABLE ""ManualPaymentRequests"" ADD COLUMN IF NOT EXISTS ""ProofWaiverReason"" character varying(512) NULL;
");

            // Gateway receipts have no uploaded file. The DEFAULT '' stays so any old
            // container that inserts without naming the column still succeeds.
            migrationBuilder.Sql(@"ALTER TABLE ""ManualPaymentRequests"" ALTER COLUMN ""ProofUrl"" DROP NOT NULL;");

            // Every pre-existing row is a learner upload.
            migrationBuilder.Sql(@"UPDATE ""ManualPaymentRequests"" SET ""Kind"" = 'learner_upload' WHERE ""Kind"" IS NULL OR ""Kind"" = '';");

            // Idempotency lookup for the gateway-receipt writer + the admin Kind/Status filter.
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_ManualPaymentRequests_PaymentTransactionId"" ON ""ManualPaymentRequests"" (""PaymentTransactionId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_ManualPaymentRequests_Kind_Status"" ON ""ManualPaymentRequests"" (""Kind"", ""Status"");");

            // ── §2/§5/§6.6 delivery method ──
            migrationBuilder.Sql(@"
ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""DeliveryMethod"" character varying(32) NOT NULL DEFAULT 'automatic_web';
ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""TelegramInviteUrl"" character varying(512) NULL;
ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""DeliveryInstructions"" character varying(2000) NULL;
ALTER TABLE ""BillingPlans"" ADD COLUMN IF NOT EXISTS ""ContentOverridesJson"" text NULL;

ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""DeliveryMethod"" character varying(32) NOT NULL DEFAULT 'automatic_web';
ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""TelegramInviteUrl"" character varying(512) NULL;
ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""DeliveryInstructions"" character varying(2000) NULL;
ALTER TABLE ""BillingPlanVersions"" ADD COLUMN IF NOT EXISTS ""ContentOverridesJson"" text NULL;

ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""FulfilmentStatus"" character varying(24) NOT NULL DEFAULT 'auto';
");

            // Owner decision #6: all 27 existing plans ship as automatic_web; the owner
            // sets exceptions in the admin UI after deploy. Existing subscriptions were
            // all delivered automatically, so nothing is waiting on an admin.
            migrationBuilder.Sql(@"UPDATE ""BillingPlans"" SET ""DeliveryMethod"" = 'automatic_web' WHERE ""DeliveryMethod"" IS NULL OR ""DeliveryMethod"" = '';");
            migrationBuilder.Sql(@"UPDATE ""BillingPlanVersions"" SET ""DeliveryMethod"" = 'automatic_web' WHERE ""DeliveryMethod"" IS NULL OR ""DeliveryMethod"" = '';");
            migrationBuilder.Sql(@"UPDATE ""Subscriptions"" SET ""FulfilmentStatus"" = 'auto' WHERE ""FulfilmentStatus"" IS NULL OR ""FulfilmentStatus"" = '';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" DROP COLUMN IF EXISTS ""FulfilmentStatus"";");

            migrationBuilder.Sql(@"
ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""ContentOverridesJson"";
ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""DeliveryInstructions"";
ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""TelegramInviteUrl"";
ALTER TABLE ""BillingPlanVersions"" DROP COLUMN IF EXISTS ""DeliveryMethod"";

ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""ContentOverridesJson"";
ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""DeliveryInstructions"";
ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""TelegramInviteUrl"";
ALTER TABLE ""BillingPlans"" DROP COLUMN IF EXISTS ""DeliveryMethod"";
");

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_ManualPaymentRequests_Kind_Status"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_ManualPaymentRequests_PaymentTransactionId"";");

            // Restore the NOT NULL contract: gateway receipts (the only null-ProofUrl rows)
            // collapse back to the pre-migration empty-string representation.
            migrationBuilder.Sql(@"UPDATE ""ManualPaymentRequests"" SET ""ProofUrl"" = '' WHERE ""ProofUrl"" IS NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""ManualPaymentRequests"" ALTER COLUMN ""ProofUrl"" SET NOT NULL;");

            migrationBuilder.Sql(@"
ALTER TABLE ""ManualPaymentRequests"" DROP COLUMN IF EXISTS ""ProofWaiverReason"";
ALTER TABLE ""ManualPaymentRequests"" DROP COLUMN IF EXISTS ""ProofWaivedAt"";
ALTER TABLE ""ManualPaymentRequests"" DROP COLUMN IF EXISTS ""ProofWaivedByAdminId"";
ALTER TABLE ""ManualPaymentRequests"" DROP COLUMN IF EXISTS ""ProfessionId"";
ALTER TABLE ""ManualPaymentRequests"" DROP COLUMN IF EXISTS ""PaymentTransactionId"";
ALTER TABLE ""ManualPaymentRequests"" DROP COLUMN IF EXISTS ""Gateway"";
ALTER TABLE ""ManualPaymentRequests"" DROP COLUMN IF EXISTS ""Kind"";
");
        }
    }
}
