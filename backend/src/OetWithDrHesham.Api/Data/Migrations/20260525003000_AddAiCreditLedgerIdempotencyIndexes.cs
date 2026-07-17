using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260525003000_AddAiCreditLedgerIdempotencyIndexes")]
    public partial class AddAiCreditLedgerIdempotencyIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AiCreditSource ordinals from Domain/AiCreditEntities.cs at migration creation:
            // PlanRenewal=0, Purchase=2, AdminAdjustment=3, UsageDebit=4, Expiration=5.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                                        IF EXISTS (
                        SELECT 1
                        FROM "AiCreditLedger"
                        WHERE "ReferenceId" IS NOT NULL
                                                    AND "Source" IN (4, 5)
                        GROUP BY "Source", "ReferenceId"
                        HAVING COUNT(*) > 1
                    ) THEN
                                                RAISE EXCEPTION 'Duplicate AI credit ledger idempotency references exist for usage debit or expiration sources.';
                                        END IF;

                                        IF EXISTS (
                                                SELECT 1
                                                FROM "AiCreditLedger"
                                                WHERE "ReferenceId" IS NOT NULL
                                                    AND "Source" = 2
                                                GROUP BY "UserId", "ReferenceId", "Source"
                                                HAVING COUNT(*) > 1
                                        ) THEN
                                                RAISE EXCEPTION 'Duplicate AI credit ledger purchase idempotency references exist for the same user.';
                                        END IF;

                                        IF EXISTS (
                                                SELECT 1
                                                FROM "AiCreditLedger"
                                                WHERE "ReferenceId" IS NOT NULL
                                                    AND "Source" = 3
                                                    AND ("ReferenceId" LIKE 'addon-refund:%' OR "ReferenceId" LIKE 'plan-refund:%')
                                                GROUP BY "UserId", "ReferenceId", "Source"
                                                HAVING COUNT(*) > 1
                                        ) THEN
                                                RAISE EXCEPTION 'Duplicate AI credit ledger refund adjustment idempotency references exist for the same user.';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_AiCreditLedger_UsageDebit_ReferenceId",
                table: "AiCreditLedger",
                columns: new[] { "ReferenceId", "Source" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND \"Source\" = 4");

            migrationBuilder.CreateIndex(
                name: "UX_AiCreditLedger_Purchase_ReferenceId",
                table: "AiCreditLedger",
                columns: new[] { "UserId", "ReferenceId", "Source" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND \"Source\" = 2");

            migrationBuilder.CreateIndex(
                name: "UX_AiCreditLedger_Expiration_ReferenceId",
                table: "AiCreditLedger",
                columns: new[] { "Source", "ReferenceId" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND \"Source\" = 5");

            migrationBuilder.CreateIndex(
                name: "UX_AiCreditLedger_RefundAdjustment_ReferenceId",
                table: "AiCreditLedger",
                columns: new[] { "Source", "UserId", "ReferenceId" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND \"Source\" = 3 AND (\"ReferenceId\" LIKE 'addon-refund:%' OR \"ReferenceId\" LIKE 'plan-refund:%')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_AiCreditLedger_RefundAdjustment_ReferenceId",
                table: "AiCreditLedger");

            migrationBuilder.DropIndex(
                name: "UX_AiCreditLedger_Expiration_ReferenceId",
                table: "AiCreditLedger");

            migrationBuilder.DropIndex(
                name: "UX_AiCreditLedger_Purchase_ReferenceId",
                table: "AiCreditLedger");

            migrationBuilder.DropIndex(
                name: "UX_AiCreditLedger_UsageDebit_ReferenceId",
                table: "AiCreditLedger");
        }
    }
}