using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Slice D — Checkout / Quote / Subscription / Invoice flow hardening
    /// (May 2026 billing hardening pass). All statements are written to be
    /// compatible with both PostgreSQL (production) and SQLite (used by the
    /// test suite via <c>EnsureCreated</c> — the migration itself does not
    /// run there, but kept SQLite-safe per slice contract).
    ///
    /// Additive only. No drops, no destructive changes.
    /// </summary>
    /// <inheritdoc />
    public partial class HardenCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Quote idempotency: filtered unique index per learner. Stale
            //    rows with NULL IdempotencyKey (legacy quotes) are excluded so
            //    backfill is unnecessary.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_BillingQuotes_UserId_IdempotencyKey_Unique"
                ON "BillingQuotes" ("UserId", "IdempotencyKey")
                WHERE "IdempotencyKey" IS NOT NULL;
                """);

            // 2. Double-charge protection: at most one paid invoice per
            //    provider checkout session. NULLs allowed (legacy / wallet
            //    top-ups without a session).
            migrationBuilder.Sql("""
                ALTER TABLE "Invoices"
                ADD COLUMN IF NOT EXISTS "Number" integer NULL;
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Invoices_UserId_Number"
                ON "Invoices" ("UserId", "Number")
                WHERE "Number" IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Invoices_CheckoutSessionId_Unique"
                ON "Invoices" ("CheckoutSessionId")
                WHERE "CheckoutSessionId" IS NOT NULL;
                """);

            // 3. Monotonic per-tenant invoice numbering. Standalone allocator
            //    table so we can ship without modifying the shared Invoice
            //    entity. (UserId, Sequence) is the natural key; InvoiceId is
            //    globally unique so the allocator is idempotent.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "InvoiceNumberAllocations" (
                    "UserId" TEXT NOT NULL,
                    "Sequence" INTEGER NOT NULL,
                    "InvoiceId" TEXT NOT NULL,
                    "AllocatedAt" TEXT NOT NULL,
                    CONSTRAINT "PK_InvoiceNumberAllocations" PRIMARY KEY ("UserId", "Sequence")
                );
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_InvoiceNumberAllocations_InvoiceId"
                ON "InvoiceNumberAllocations" ("InvoiceId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_InvoiceNumberAllocations_InvoiceId"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""InvoiceNumberAllocations"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Invoices_CheckoutSessionId_Unique"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Invoices_UserId_Number"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Invoices"" DROP COLUMN IF EXISTS ""Number"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_BillingQuotes_UserId_IdempotencyKey_Unique"";");
        }
    }
}
