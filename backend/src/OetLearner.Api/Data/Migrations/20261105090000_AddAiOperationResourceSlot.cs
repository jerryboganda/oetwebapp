using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01),
    /// corrective follow-up to 20261104090000_AddAiModelPricingAndFeaturePolicy.
    ///
    /// <para>
    /// Adds <c>AiOperations.ResourceSlotKey</c> (nullable, max 64) plus a
    /// UNIQUE <b>partial</b> index over its non-null values. The slot hashes
    /// the stable business identity of an operation
    /// (feature/module/user/resource/resource-type/resource-version/prompt/
    /// rulebook version) and deliberately EXCLUDES the request hash and the
    /// model route.
    /// </para>
    ///
    /// <para>
    /// Why this exists: <c>UX_AiOperations_IdempotencyKey</c> alone cannot
    /// stop two concurrent requests that target the same resource with a
    /// DIFFERENT payload — the request hash is one of the idempotency key's
    /// own dimensions, so those two requests hash to two different keys and
    /// both inserts succeed. The pre-existing guard for that case was a
    /// read-then-insert check, which is racy by construction. This partial
    /// unique index moves the guarantee into the database: exactly one of the
    /// two concurrent inserts can win, the loser gets SQLSTATE 23505 against
    /// <c>UX_AiOperations_ResourceSlotKey</c>, and the caller is told
    /// <c>ai_operation_conflict</c> instead of silently buying a second
    /// provider call.
    /// </para>
    ///
    /// <para>
    /// The filter (<c>WHERE "ResourceSlotKey" IS NOT NULL</c>) keeps free-form
    /// interactive calls — which have no stable caller resource and therefore
    /// leave the slot null — entirely outside the constraint, so unrelated
    /// interactions are never conflated.
    /// </para>
    ///
    /// <para>
    /// Strictly additive: one nullable column plus one partial index on a
    /// table that no released code has ever written to outside this wave
    /// (W1 landed it schema-only). <c>Down</c> drops both, which is safe
    /// because the column carries no data any other table references.
    /// Hand-authored per repo convention: inline <c>[Migration]</c>/
    /// <c>[DbContext]</c>, no Designer file, ModelSnapshot deliberately
    /// untouched (see <c>LearnerDbContext.PendingChangesWarningSuppression.cs</c>).
    /// Ordered after 20261104090000 and before the already-committed
    /// 20261128090000_HardenAiCreditSourceValidity — no schema overlap.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261105090000_AddAiOperationResourceSlot")]
    public partial class AddAiOperationResourceSlot : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IF NOT EXISTS keeps the migration replay-safe against a database
            // that already had the column hand-applied during the incident.
            migrationBuilder.Sql(
                "ALTER TABLE \"AiOperations\" " +
                "ADD COLUMN IF NOT EXISTS \"ResourceSlotKey\" character varying(64) NULL;");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_AiOperations_ResourceSlotKey\" " +
                "ON \"AiOperations\" (\"ResourceSlotKey\") " +
                "WHERE \"ResourceSlotKey\" IS NOT NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"UX_AiOperations_ResourceSlotKey\";");

            migrationBuilder.Sql(
                "ALTER TABLE \"AiOperations\" DROP COLUMN IF EXISTS \"ResourceSlotKey\";");
        }
    }
}
