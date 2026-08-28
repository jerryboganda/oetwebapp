using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// W1 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01).
    ///
    /// Adds the nullable provenance columns described on
    /// <see cref="OetLearner.Api.Domain.AiUsageRecord"/> that link a usage
    /// row back to the <c>AiOperation</c>/<c>AiOperationAttempt</c> that
    /// produced it (once a later wave starts populating them) and record the
    /// actual billed token/cost breakdown instead of only the call-time
    /// <c>CostEstimateUsd</c> estimate.
    ///
    /// <para><b>Backfill is idempotent</b> — both <c>UPDATE</c> statements
    /// are gated on <c>IS NULL</c>, so re-running this migration (or a
    /// future migration that touches the same rows) never re-stamps already
    /// backfilled data:</para>
    /// <list type="bullet">
    ///   <item><c>PricingVersion</c> → <c>'legacy-pre-remediation'</c> for
    ///   every historical row, so a null value always means "never
    ///   recalculated" rather than "used the current rate card".</item>
    ///   <item><c>ProviderInvoked</c> → <c>(ProviderId IS NOT NULL)</c>,
    ///   deriving the new boolean from the existing nullable ProviderId
    ///   column exactly as the gateway already treats it.</item>
    /// </list>
    ///
    /// <para><b>Partition-safe unique index.</b> <c>AiUsageRecords</c> may or
    /// may not be a partitioned parent depending on whether the opt-in
    /// <c>ConvertAppendOnlyTablesToPartitioned</c> conversion has run in a
    /// given environment (see that migration). A composite unique index on a
    /// partitioned parent must include the partition key, which
    /// (OperationId, AttemptNumber) does not, so
    /// <c>UX_AiUsageRecords_Operation_Attempt</c> is created only inside a
    /// guarded <c>DO</c> block that checks <c>pg_class.relkind &lt;&gt; 'p'</c>
    /// first. When the table is partitioned, the index is skipped there —
    /// the <see cref="OetLearner.Api.Domain.AiOperationAttempt"/> composite
    /// primary key (<c>OperationId</c>, <c>AttemptNumber</c>) remains the
    /// authoritative exactly-one-attempt constraint in every shape, since
    /// that table is a plain, never-partitioned table.</para>
    ///
    /// <para>Strictly additive: fourteen new nullable columns and one
    /// conditional index, no existing column touched. Hand-authored per repo
    /// convention: inline <c>[Migration]</c>/<c>[DbContext]</c>, no Designer
    /// file, ModelSnapshot deliberately untouched.</para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261103090000_ExtendAiUsageRecordProvenance")]
    public partial class ExtendAiUsageRecordProvenance : Migration
    {
        private const string ConditionalUniqueIndexSql = @"
DO $$
DECLARE
    v_oid oid;
    v_relkind char;
BEGIN
    -- to_regclass honours the current search_path, so this resolves the
    -- table in whichever schema it actually lives in (production's
    -- ""public"" schema, or a per-test schema under integration tests)
    -- instead of assuming a hardcoded schema name.
    v_oid := to_regclass('""AiUsageRecords""');

    IF v_oid IS NULL THEN
        RAISE NOTICE 'ExtendAiUsageRecordProvenance: AiUsageRecords missing, skip index';
    ELSE
        SELECT relkind INTO v_relkind FROM pg_class WHERE oid = v_oid;

        IF v_relkind = 'p' THEN
            RAISE NOTICE 'ExtendAiUsageRecordProvenance: AiUsageRecords is partitioned (relkind=p); UX_AiUsageRecords_Operation_Attempt skipped — a composite unique index on a partitioned parent must include the partition key, which (OperationId, AttemptNumber) does not. AiOperationAttempts.PK remains authoritative.';
        ELSE
            CREATE UNIQUE INDEX IF NOT EXISTS ""UX_AiUsageRecords_Operation_Attempt""
                ON ""AiUsageRecords"" (""OperationId"", ""AttemptNumber"")
                WHERE ""OperationId"" IS NOT NULL AND ""AttemptNumber"" IS NOT NULL;
        END IF;
    END IF;
END $$;";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OperationId",
                table: "AiUsageRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttemptNumber",
                table: "AiUsageRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProviderInvoked",
                table: "AiUsageRecords",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderRequestId",
                table: "AiUsageRecords",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProviderHttpStatus",
                table: "AiUsageRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NormalInputTokens",
                table: "AiUsageRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NormalOutputTokens",
                table: "AiUsageRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheWriteTokens",
                table: "AiUsageRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheReadTokens",
                table: "AiUsageRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BilledTokenClass",
                table: "AiUsageRecords",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingVersion",
                table: "AiUsageRecords",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CalculatedCostUsd",
                table: "AiUsageRecords",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetryReason",
                table: "AiUsageRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorClass",
                table: "AiUsageRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Idempotent historical backfill — see class doc comment.
            migrationBuilder.Sql(
                "UPDATE \"AiUsageRecords\" SET \"PricingVersion\" = 'legacy-pre-remediation' " +
                "WHERE \"PricingVersion\" IS NULL;");

            migrationBuilder.Sql(
                "UPDATE \"AiUsageRecords\" SET \"ProviderInvoked\" = (\"ProviderId\" IS NOT NULL) " +
                "WHERE \"ProviderInvoked\" IS NULL;");

            migrationBuilder.Sql(ConditionalUniqueIndexSql);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: once W2 starts populating these columns, this Down()
            // will drop that provenance data along with the columns — a
            // deliberate destructive rollback, not currently reachable
            // because nothing writes to these columns yet.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"UX_AiUsageRecords_Operation_Attempt\";");

            migrationBuilder.DropColumn(name: "ErrorClass", table: "AiUsageRecords");
            migrationBuilder.DropColumn(name: "RetryReason", table: "AiUsageRecords");
            migrationBuilder.DropColumn(name: "CalculatedCostUsd", table: "AiUsageRecords");
            migrationBuilder.DropColumn(name: "PricingVersion", table: "AiUsageRecords");
            migrationBuilder.DropColumn(name: "BilledTokenClass", table: "AiUsageRecords");
            migrationBuilder.DropColumn(name: "CacheReadTokens", table: "AiUsageRecords");
            migrationBuilder.DropColumn(name: "CacheWriteTokens", table: "AiUsageRecords");
            migrationBuilder.DropColumn(name: "NormalOutputTokens", table: "AiUsageRecords");
            migrationBuilder.DropColumn(name: "NormalInputTokens", table: "AiUsageRecords");
            migrationBuilder.DropColumn(name: "ProviderHttpStatus", table: "AiUsageRecords");
            migrationBuilder.DropColumn(name: "ProviderRequestId", table: "AiUsageRecords");
            migrationBuilder.DropColumn(name: "ProviderInvoked", table: "AiUsageRecords");
            migrationBuilder.DropColumn(name: "AttemptNumber", table: "AiUsageRecords");
            migrationBuilder.DropColumn(name: "OperationId", table: "AiUsageRecords");
        }
    }
}
