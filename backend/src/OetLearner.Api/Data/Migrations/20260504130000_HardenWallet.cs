using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Wallet hardening (Slice A, May 2026):
    ///   • Adds a stable kebab-case <c>Slug</c> column to
    ///     <c>WalletTopUpTierConfigs</c> with a partial unique index.
    ///   • Adds a DB-level <c>CHECK (CreditBalance &gt;= 0)</c> constraint on
    ///     <c>Wallets</c> as a backstop for the application-level invariant
    ///     enforced in <c>WalletService.DebitAsync</c>.
    ///   • Adds a defensive composite index for the IdempotencyRecord
    ///     wallet-credit / wallet-debit scopes used by the new replay path.
    ///
    /// All operations are additive and PostgreSQL-compatible. CHECK creation
    /// is wrapped in <c>DO $$ ... $$</c> blocks so re-applying the migration
    /// against a partially-migrated database is a no-op.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260504130000_HardenWallet")]
    public partial class HardenWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Slug column on WalletTopUpTierConfigs ----------------------------
            migrationBuilder.Sql("""
                ALTER TABLE "WalletTopUpTierConfigs"
                ADD COLUMN IF NOT EXISTS "Slug" character varying(64) NULL;
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_WalletTopUpTierConfigs_Slug"
                ON "WalletTopUpTierConfigs" ("Slug")
                WHERE "Slug" IS NOT NULL;
                """);

            // Negative-balance CHECK constraint --------------------------------
            // Wrapped in DO block because PostgreSQL has no
            // "ADD CONSTRAINT IF NOT EXISTS" syntax pre-15. Idempotent across
            // re-runs and partial deploys.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'CK_Wallets_CreditBalance_NonNegative'
                    ) THEN
                        ALTER TABLE "Wallets"
                        ADD CONSTRAINT "CK_Wallets_CreditBalance_NonNegative"
                        CHECK ("CreditBalance" >= 0);
                    END IF;
                END
                $$;
                """);

            // Idempotency support index for wallet credit/debit replays --------
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_IdempotencyRecords_Scope_Key_WalletPrefix"
                ON "IdempotencyRecords" ("Scope", "Key")
                WHERE "Scope" LIKE 'wallet-%';
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "WalletTransactions"
                ADD COLUMN IF NOT EXISTS "IdempotencyKey" character varying(128) NULL;
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_WalletTransactions_WalletId_IdempotencyKey"
                ON "WalletTransactions" ("WalletId", "IdempotencyKey")
                WHERE "IdempotencyKey" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_WalletTransactions_WalletId_IdempotencyKey";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "WalletTransactions" DROP COLUMN IF EXISTS "IdempotencyKey";
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_IdempotencyRecords_Scope_Key_WalletPrefix";
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'CK_Wallets_CreditBalance_NonNegative'
                    ) THEN
                        ALTER TABLE "Wallets"
                        DROP CONSTRAINT "CK_Wallets_CreditBalance_NonNegative";
                    END IF;
                END
                $$;
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_WalletTopUpTierConfigs_Slug";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "WalletTopUpTierConfigs" DROP COLUMN IF EXISTS "Slug";
                """);
        }
    }
}
