using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds evidence-source tracking to <c>Invoices</c> so a "Paid" invoice
    /// can be told apart from one that was minted with no payment evidence
    /// at all (the root cause of "Paid invoices with no payment evidence" --
    /// see <c>LearnerService.EnsureSubscriptionInvoiceAsync</c> and
    /// <c>BillingExpansionEndpoints.EnsureSubscriptionInvoice</c>).
    ///
    /// Adds three columns to <c>Invoices</c>:
    /// <list type="bullet">
    ///   <item><c>SubscriptionId</c> (nullable, 64) -- links the invoice to
    ///   the subscription it was issued for, when known, so evidence lookups
    ///   and the reconciliation pass can find the real quote/payment/proof
    ///   instead of guessing.</item>
    ///   <item><c>Source</c> (not null, 24, default <c>'gateway'</c> --
    ///   see <see cref="OetLearner.Api.Domain.InvoiceSources"/>) -- how the
    ///   invoice's "Paid" status was actually established.
    ///   <para>
    ///   IMPORTANT: the SQL-level <c>DEFAULT 'gateway'</c> here is
    ///   deliberately only an interim back-fill value for every pre-existing
    ///   row -- it is NOT a claim that every existing invoice actually has
    ///   gateway evidence. A startup reconciliation service (a later stage
    ///   of this fix) re-derives the real source for each row and corrects
    ///   it, using the NULL-vs-set state of <c>ReconciledAt</c> below as its
    ///   "needs review" signal. A future reader must not treat this default
    ///   as the final word on any pre-existing invoice's evidence.
    ///   </para></item>
    ///   <item><c>ReconciledAt</c> (nullable) -- the moment this row's
    ///   Source/evidence-linkage was correctly determined, either at
    ///   creation by evidence-aware code or later by the one-time legacy
    ///   reconciliation pass. NULL on every pre-existing row is exactly the
    ///   "not yet reviewed" signal the reconciliation pass keys off.</item>
    /// </list>
    ///
    /// Strictly additive: one existing table gains three nullable/defaulted
    /// columns and one new index -- no data rewritten beyond the SQL
    /// default, no existing column touched. Hand-authored per repo
    /// convention: inline <c>[Migration]</c>/<c>[DbContext]</c>, no Designer
    /// file, ModelSnapshot deliberately untouched. Ordered after the
    /// already-committed 20261129000000_RestoreListeningPartBCSourceStems.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261130090000_AddInvoiceEvidenceSourceTracking")]
    public partial class AddInvoiceEvidenceSourceTracking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubscriptionId",
                table: "Invoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Invoices",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "gateway");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReconciledAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SubscriptionId",
                table: "Invoices",
                column: "SubscriptionId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_SubscriptionId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ReconciledAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SubscriptionId",
                table: "Invoices");
        }
    }
}
