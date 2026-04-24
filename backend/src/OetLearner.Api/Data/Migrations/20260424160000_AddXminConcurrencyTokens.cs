using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Model-snapshot bump only: adds xmin-based optimistic concurrency tokens
    /// on Subscription, Invoice, SubscriptionItem, and Evaluation.
    ///
    /// <para>
    /// xmin is a Postgres system column present on every row, so there is no
    /// DDL change. EF now emits <c>WHERE xmin = @original</c> on every UPDATE
    /// to these tables. Pre-existing rows work without any data migration.
    /// </para>
    /// </summary>
    public partial class AddXminConcurrencyTokens : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentional no-op at the SQL level. Snapshot-only migration.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentional no-op. Downgrading simply drops the concurrency
            // annotations in the model; no SQL needed.
        }
    }
}
