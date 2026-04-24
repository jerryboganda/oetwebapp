using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds BRIN indexes on <c>OccurredAt</c>/<c>CreatedAt</c>/<c>ReceivedAt</c>
    /// for append-only, time-ordered tables. BRIN (Block Range INdex) is
    /// ~1% the size of a B-tree and is a near-perfect match for tables whose
    /// physical row order correlates with the indexed column (i.e. rows are
    /// appended in time order and rarely updated).
    ///
    /// Target tables:
    ///   * AnalyticsEvents (OccurredAt)
    ///   * AuditEvents (OccurredAt)
    ///   * AiUsageRecords (CreatedAt)
    ///   * PaymentWebhookEvents (ReceivedAt)
    ///   * NotificationDeliveryAttempts (AttemptedAt)
    ///
    /// These complement (do not replace) the existing B-tree indexes that
    /// lead with other columns. They are specifically optimised for retention
    /// sweepers that range-scan "older than N" predicates.
    ///
    /// Idempotent via IF NOT EXISTS.
    /// </summary>
    public partial class AddBrinIndexesForAppendOnlyTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_AnalyticsEvents_OccurredAt_brin\" " +
                "ON \"AnalyticsEvents\" USING BRIN (\"OccurredAt\");");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_AuditEvents_OccurredAt_brin\" " +
                "ON \"AuditEvents\" USING BRIN (\"OccurredAt\");");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_AiUsageRecords_CreatedAt_brin\" " +
                "ON \"AiUsageRecords\" USING BRIN (\"CreatedAt\");");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_PaymentWebhookEvents_ReceivedAt_brin\" " +
                "ON \"PaymentWebhookEvents\" USING BRIN (\"ReceivedAt\");");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_NotificationDeliveryAttempts_AttemptedAt_brin\" " +
                "ON \"NotificationDeliveryAttempts\" USING BRIN (\"AttemptedAt\");");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_NotificationDeliveryAttempts_AttemptedAt_brin\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_PaymentWebhookEvents_ReceivedAt_brin\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AiUsageRecords_CreatedAt_brin\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AuditEvents_OccurredAt_brin\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AnalyticsEvents_OccurredAt_brin\";");
        }
    }
}
