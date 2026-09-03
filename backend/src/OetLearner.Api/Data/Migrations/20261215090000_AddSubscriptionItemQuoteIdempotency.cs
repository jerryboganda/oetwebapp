using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261215090000_AddSubscriptionItemQuoteIdempotency")]
    public partial class AddSubscriptionItemQuoteIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Repair genuine defect duplicates BEFORE the unique index lands:
            // same (SubscriptionId, ItemCode, QuoteId) with QuoteId NOT NULL is
            // one logical grant (one quote). Keep the earliest row, drop later
            // replay inserts. A legitimate second purchase carries a different
            // QuoteId and is never touched. QuoteId IS NULL rows (legacy/admin
            // grants without a quote) are excluded entirely.
            migrationBuilder.Sql(@"
DELETE FROM ""SubscriptionItems"" a
USING ""SubscriptionItems"" b
WHERE a.""QuoteId"" IS NOT NULL
  AND b.""QuoteId"" IS NOT NULL
  AND a.""SubscriptionId"" = b.""SubscriptionId""
  AND a.""ItemCode"" = b.""ItemCode""
  AND a.""QuoteId"" = b.""QuoteId""
  AND a.""Id"" > b.""Id"";
");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionItems_SubscriptionId_ItemCode_QuoteId",
                table: "SubscriptionItems",
                columns: new[] { "SubscriptionId", "ItemCode", "QuoteId" },
                unique: true,
                filter: "\"QuoteId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionItems_SubscriptionId_ItemCode_QuoteId",
                table: "SubscriptionItems");
        }
    }
}
