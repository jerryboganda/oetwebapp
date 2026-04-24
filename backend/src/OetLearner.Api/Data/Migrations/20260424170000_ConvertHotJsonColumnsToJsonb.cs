using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Converts the highest-volume / most-queryable JSON-string columns from
    /// <c>text</c> to <c>jsonb</c>:
    ///
    /// <list type="bullet">
    ///   <item><c>AnalyticsEvents.PayloadJson</c> — highest-volume analytics table</item>
    ///   <item><c>Attempts.AnalysisJson</c> — per-attempt signals used by reporting</item>
    ///   <item><c>Evaluations.StrengthsJson / IssuesJson / CriterionScoresJson / FeedbackItemsJson</c> — evaluation reports</item>
    ///   <item><c>PaymentWebhookEvents.PayloadJson</c> — large webhook bodies, occasionally inspected by hand</item>
    /// </list>
    ///
    /// <para>Benefits: smaller on-disk size (binary representation + dedup of
    /// whitespace), native JSON validation at the storage layer, and the
    /// ability to add GIN indexes later for arbitrary-key lookups.</para>
    ///
    /// <para>Each column is altered inside an idempotent DO block that first
    /// checks the current <c>information_schema</c> type, so the migration is
    /// safe to re-run. The cast uses <c>NULLIF(col, '')::jsonb</c> which
    /// coerces empty strings to NULL; any row containing invalid JSON will
    /// fail the cast loudly, surfacing a data-quality bug rather than silently
    /// corrupting the column.</para>
    /// </summary>
    public partial class ConvertHotJsonColumnsToJsonb : Migration
    {
        private const string ConvertTemplate = @"
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = '{0}'
                      AND column_name = '{1}'
                      AND data_type = 'text'
                ) THEN
                    EXECUTE 'ALTER TABLE ""{0}"" ALTER COLUMN ""{1}"" TYPE jsonb USING NULLIF(""{1}"", '''')::jsonb';
                END IF;
            END $$;";

        private const string RevertTemplate = @"
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = '{0}'
                      AND column_name = '{1}'
                      AND data_type = 'jsonb'
                ) THEN
                    EXECUTE 'ALTER TABLE ""{0}"" ALTER COLUMN ""{1}"" TYPE text USING ""{1}""::text';
                END IF;
            END $$;";

        private static readonly (string Table, string Column)[] Targets = new[]
        {
            ("AnalyticsEvents", "PayloadJson"),
            ("Attempts", "AnalysisJson"),
            ("Evaluations", "StrengthsJson"),
            ("Evaluations", "IssuesJson"),
            ("Evaluations", "CriterionScoresJson"),
            ("Evaluations", "FeedbackItemsJson"),
            ("PaymentWebhookEvents", "PayloadJson"),
        };

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, column) in Targets)
            {
                migrationBuilder.Sql(string.Format(ConvertTemplate, table, column));
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, column) in Targets)
            {
                migrationBuilder.Sql(string.Format(RevertTemplate, table, column));
            }
        }
    }
}
