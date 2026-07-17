using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Risk #2 data migration (Phase 5 closure) — replaces legacy Reading
    /// question-type strings that were emitted by the mock wizard before the
    /// Phase 5 canonical-type fix:
    /// <list type="bullet">
    ///   <item><c>WordPool</c> → <c>MatchingTextReference</c></item>
    ///   <item><c>TrueFalseNotGiven</c> → <c>MultipleChoice3</c></item>
    /// </list>
    ///
    /// <para>
    /// The strings are only present in <c>ReadingExtractionDrafts.ExtractedManifestJson</c>
    /// (a raw-JSON column that stores the AI-produced manifest before an admin
    /// approves it). <c>ReadingQuestion.QuestionType</c> is stored as an int enum
    /// so it was never affected by the wizard contract drift.
    /// </para>
    ///
    /// <para>
    /// The UPDATE is a no-op on rows that do not contain the legacy strings, so
    /// it is safe to run against any environment regardless of whether drafts
    /// with these strings ever existed.
    /// </para>
    ///
    /// <para>
    /// Hand-written following the <c>Mocks_V3_W1_ItemRetire</c> pattern.
    /// ⚠ <b>Postgres-only SQL.</b> The test suite uses SQLite via
    /// <c>EnsureCreatedAsync()</c> and bypasses migrations entirely.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260522090000_FixReadingQuestionTypeWordPool")]
    public partial class FixReadingQuestionTypeWordPool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""ReadingExtractionDrafts""
                SET ""ExtractedManifestJson"" =
                    REPLACE(
                        REPLACE(
                            ""ExtractedManifestJson"",
                            '""WordPool""',
                            '""MatchingTextReference""'
                        ),
                        '""TrueFalseNotGiven""',
                        '""MultipleChoice3""'
                    )
                WHERE ""ExtractedManifestJson"" IS NOT NULL
                  AND (
                        ""ExtractedManifestJson"" LIKE '%""WordPool""%'
                     OR ""ExtractedManifestJson"" LIKE '%""TrueFalseNotGiven""%'
                      );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The reverse mapping is ambiguous (MatchingTextReference also maps
            // to its own canonical uses, not just ex-WordPool rows), so Down is
            // intentionally a no-op. Rollback by restoring from a pre-migration
            // database backup.
        }
    }
}
