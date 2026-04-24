using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds standalone B-tree indexes on the retention-sweep timestamp columns
    /// so <c>DataRetentionWorker</c> range deletes have an index-supported seek
    /// that does not depend on the compound indexes that lead with another
    /// column (EventName, Status, etc.).
    ///
    /// These complement the pre-existing BRIN indexes added in
    /// <c>AddBrinIndexesForAppendOnlyTables</c>.
    ///
    /// Idempotent via IF NOT EXISTS. Pure SQL so no <c>LearnerDbContextModelSnapshot</c>
    /// changes are required — matches the pattern used by the BRIN migration.
    /// </summary>
    public partial class AddRetentionIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_AnalyticsEvents_OccurredAt\" " +
                "ON \"AnalyticsEvents\" (\"OccurredAt\");");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_PaymentWebhookEvents_ReceivedAt\" " +
                "ON \"PaymentWebhookEvents\" (\"ReceivedAt\");");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_NotificationDeliveryAttempts_AttemptedAt\" " +
                "ON \"NotificationDeliveryAttempts\" (\"AttemptedAt\");");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_NotificationDeliveryAttempts_AttemptedAt\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_PaymentWebhookEvents_ReceivedAt\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AnalyticsEvents_OccurredAt\";");
        }
    }
}
