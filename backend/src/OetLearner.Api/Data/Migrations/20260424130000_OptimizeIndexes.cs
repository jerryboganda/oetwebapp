using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Schema performance pass: drops redundant indexes (prefix-covered or
    /// duplicates of [Index]-attribute declarations) and adds missing indexes
    /// on hot query paths surfaced by the April-2026 schema audit.
    ///
    /// All statements are idempotent (IF EXISTS / IF NOT EXISTS) so the
    /// migration can be re-applied safely after manual DBA intervention.
    /// Indexes that include a WHERE clause are partial; that is a PostgreSQL
    /// feature and requires no server-side support beyond PG 10+.
    /// </summary>
    public partial class OptimizeIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Drop redundant indexes ────────────────────────────────────
            // (NormalizedEmail, Role) is strictly weaker than UNIQUE(NormalizedEmail).
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ApplicationUserAccounts_NormalizedEmail_Role\";");
            // MockAttempts(UserId) is a strict prefix of (UserId, State).
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_MockAttempts_UserId\";");
            // ForumReplies(ThreadId) replaced by (ThreadId, CreatedAt) below.
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ForumReplies_ThreadId\";");

            // ── Add missing indexes on hot query paths ────────────────────

            // Admin / QA: "who has attempted item X?" — previously full-scan.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_Attempts_ContentId\" " +
                "ON \"Attempts\" (\"ContentId\");");

            // Background worker polls pending evaluations without a user filter.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_Evaluations_State_LastTransitionAt\" " +
                "ON \"Evaluations\" (\"State\", \"LastTransitionAt\");");

            // Expert review queue: list + sort by age.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_ReviewRequests_State_CreatedAt\" " +
                "ON \"ReviewRequests\" (\"State\", \"CreatedAt\");");

            // Admin user table filters/sorts.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_ApplicationUserAccounts_LastLoginAt\" " +
                "ON \"ApplicationUserAccounts\" (\"LastLoginAt\");");

            // LearnerUser admin search + engagement segmentation.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_Users_Email\" " +
                "ON \"Users\" (\"Email\");");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_Users_LastActiveAt\" " +
                "ON \"Users\" (\"LastActiveAt\");");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_Users_AccountStatus\" " +
                "ON \"Users\" (\"AccountStatus\");");

            // Refresh-token cleanup: partial index over non-revoked tokens.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_RefreshTokenRecord_Active\" " +
                "ON \"RefreshTokenRecords\" (\"ApplicationUserAccountId\") " +
                "WHERE \"RevokedAt\" IS NULL;");

            // OTP expiry sweep: solo index on ExpiresAt (composite prefix unusable).
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_EmailOtpChallenges_ExpiresAt\" " +
                "ON \"EmailOtpChallenges\" (\"ExpiresAt\");");

            // Paginated forum thread view: ThreadId filter + CreatedAt ORDER BY.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_ForumReplies_ThreadId_CreatedAt\" " +
                "ON \"ForumReplies\" (\"ThreadId\", \"CreatedAt\");");

            // Platform-wide analytics funnels (no user filter).
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_AnalyticsEvents_EventName_OccurredAt\" " +
                "ON \"AnalyticsEvents\" (\"EventName\", \"OccurredAt\");");

            // Payment reconciliation job.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_PaymentTransactions_Status_CreatedAt\" " +
                "ON \"PaymentTransactions\" (\"Status\", \"CreatedAt\");");

            // NotificationDeliveryAttempts: status+time for retry workers.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_NotificationDeliveryAttempts_Status_AttemptedAt\" " +
                "ON \"NotificationDeliveryAttempts\" (\"Status\", \"AttemptedAt\");");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the new indexes in reverse order.
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_NotificationDeliveryAttempts_Status_AttemptedAt\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_PaymentTransactions_Status_CreatedAt\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AnalyticsEvents_EventName_OccurredAt\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ForumReplies_ThreadId_CreatedAt\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_EmailOtpChallenges_ExpiresAt\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_RefreshTokenRecord_Active\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Users_AccountStatus\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Users_LastActiveAt\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Users_Email\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ApplicationUserAccounts_LastLoginAt\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ReviewRequests_State_CreatedAt\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Evaluations_State_LastTransitionAt\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Attempts_ContentId\";");

            // Recreate the dropped indexes (shape used before this migration).
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_ForumReplies_ThreadId\" " +
                "ON \"ForumReplies\" (\"ThreadId\");");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_MockAttempts_UserId\" " +
                "ON \"MockAttempts\" (\"UserId\");");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_ApplicationUserAccounts_NormalizedEmail_Role\" " +
                "ON \"ApplicationUserAccounts\" (\"NormalizedEmail\", \"Role\");");
        }
    }
}
