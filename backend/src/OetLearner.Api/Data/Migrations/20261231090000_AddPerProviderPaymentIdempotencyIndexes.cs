using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Security standard §3 (PAY-08 / PAY-11): scope payment idempotency to the
    /// provider instead of globally, and back the wallet/grant atomicity gaps with a
    /// database invariant.
    ///
    ///   * PaymentTransactions / PaymentWebhookEvents: the global UNIQUE on
    ///     GatewayTransactionId / GatewayEventId is replaced by a composite UNIQUE on
    ///     (Gateway, …). Two gateways may legitimately mint the same opaque id, so a
    ///     global constraint produced spurious cross-provider collisions; the
    ///     composite still rejects a genuine provider-side replay.
    ///   * ManualPaymentRequests.PaymentTransactionId becomes a partial UNIQUE, so an
    ///     admin approve can never mint two access grants for one gateway payment.
    ///   * CheckoutSessions gains a partial UNIQUE on (Gateway, GatewayOrderId), so the
    ///     fulfilment "already fulfilled" short-circuit is backed by the database.
    ///
    /// HAND-AUTHORED (repo convention, see 20260729090000): inline
    /// <c>[Migration]</c>/<c>[DbContext]</c>, no Designer file, ModelSnapshot
    /// deliberately untouched.
    ///
    /// SAFETY: the two composite UNIQUEs are strictly weaker than the global UNIQUEs
    /// they replace and therefore cannot fail on existing data. The two remaining
    /// UNIQUE indexes could fail on legacy duplicate rows, so they are created only
    /// when the data already satisfies them — a live database with duplicates keeps
    /// its current (non-unique) index and the migration completes with a NOTICE.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261231090000_AddPerProviderPaymentIdempotencyIndexes")]
    public partial class AddPerProviderPaymentIdempotencyIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_PaymentTransactions_GatewayTransactionId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_PaymentWebhookEvents_GatewayEventId"";");

            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PaymentTransactions_Gateway_GatewayTransactionId"" ON ""PaymentTransactions"" (""Gateway"", ""GatewayTransactionId"");");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PaymentWebhookEvents_Gateway_GatewayEventId"" ON ""PaymentWebhookEvents"" (""Gateway"", ""GatewayEventId"");");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM ""ManualPaymentRequests"" WHERE ""PaymentTransactionId"" IS NOT NULL GROUP BY ""PaymentTransactionId"" HAVING COUNT(*) > 1) THEN
        RAISE NOTICE 'AddPerProviderPaymentIdempotencyIndexes: duplicate ManualPaymentRequests.PaymentTransactionId rows exist; keeping the non-unique index.';
    ELSE
        DROP INDEX IF EXISTS ""IX_ManualPaymentRequests_PaymentTransactionId"";
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ManualPaymentRequests_PaymentTransactionId"" ON ""ManualPaymentRequests"" (""PaymentTransactionId"") WHERE ""PaymentTransactionId"" IS NOT NULL;
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM ""CheckoutSessions"" WHERE ""GatewayOrderId"" IS NOT NULL GROUP BY ""Gateway"", ""GatewayOrderId"" HAVING COUNT(*) > 1) THEN
        RAISE NOTICE 'AddPerProviderPaymentIdempotencyIndexes: duplicate CheckoutSessions (Gateway, GatewayOrderId) rows exist; skipping the unique index.';
    ELSE
        DROP INDEX IF EXISTS ""IX_CheckoutSessions_GatewayOrderId"";
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CheckoutSessions_Gateway_GatewayOrderId"" ON ""CheckoutSessions"" (""Gateway"", ""GatewayOrderId"") WHERE ""GatewayOrderId"" IS NOT NULL;
    END IF;
END $$;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CheckoutSessions_Gateway_GatewayOrderId"";");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_CheckoutSessions_GatewayOrderId"" ON ""CheckoutSessions"" (""GatewayOrderId"");");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_PaymentTransactions_Gateway_GatewayTransactionId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_PaymentWebhookEvents_Gateway_GatewayEventId"";");

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_ManualPaymentRequests_PaymentTransactionId"";");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_ManualPaymentRequests_PaymentTransactionId"" ON ""ManualPaymentRequests"" (""PaymentTransactionId"");");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM ""PaymentTransactions"" GROUP BY ""GatewayTransactionId"" HAVING COUNT(*) > 1) THEN
        RAISE NOTICE 'AddPerProviderPaymentIdempotencyIndexes.Down: duplicate PaymentTransactions.GatewayTransactionId rows exist; skipping the global unique index.';
    ELSE
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PaymentTransactions_GatewayTransactionId"" ON ""PaymentTransactions"" (""GatewayTransactionId"");
    END IF;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM ""PaymentWebhookEvents"" GROUP BY ""GatewayEventId"" HAVING COUNT(*) > 1) THEN
        RAISE NOTICE 'AddPerProviderPaymentIdempotencyIndexes.Down: duplicate PaymentWebhookEvents.GatewayEventId rows exist; skipping the global unique index.';
    ELSE
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PaymentWebhookEvents_GatewayEventId"" ON ""PaymentWebhookEvents"" (""GatewayEventId"");
    END IF;
END $$;
");
        }
    }
}
