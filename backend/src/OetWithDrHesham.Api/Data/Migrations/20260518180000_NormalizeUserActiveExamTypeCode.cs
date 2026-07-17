using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Data-only migration: normalises legacy lowercase <c>"oet"</c> values to the
    /// canonical uppercase <c>"OET"</c> across exam-type-code columns. Pairs with
    /// the in-code <c>OetWithDrHesham.Api.Services.Common.ExamCodes</c> normalisation
    /// helper so that any rows inserted by legacy code paths, raw SQL, or one-off
    /// operator scripts are folded into the canonical form.
    ///
    /// Idempotent: re-running is a no-op once all rows are already <c>'OET'</c>.
    /// Does NOT touch <c>ExamTypes."Code"</c> — that lookup PK remains <c>'oet'</c>
    /// per the schema anchor in <c>SeedData.cs:2185</c>.
    ///
    /// Hand-written (no Designer.cs) per the repo's migration-drift policy in
    /// <c>memories/repo/migration-drift-note.md</c> — the model snapshot does not
    /// change because this migration only modifies row values.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260518180000_NormalizeUserActiveExamTypeCode")]
    public partial class NormalizeUserActiveExamTypeCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Users.ActiveExamTypeCode — the column whose lowercase value triggered
            // the original "I can't see the recalls I created" production bug.
            migrationBuilder.Sql("""
                UPDATE "Users"
                SET "ActiveExamTypeCode" = 'OET'
                WHERE LOWER("ActiveExamTypeCode") = 'oet'
                  AND "ActiveExamTypeCode" <> 'OET';
                """);

            // VocabularyTerms.ExamTypeCode — already remediated on 2026-05-18 but
            // kept idempotent so a fresh database matches production exactly.
            migrationBuilder.Sql("""
                UPDATE "VocabularyTerms"
                SET "ExamTypeCode" = 'OET'
                WHERE LOWER("ExamTypeCode") = 'oet'
                  AND "ExamTypeCode" <> 'OET';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally no-op: rolling back to lowercase would re-introduce
            // the original production bug. Operators wanting to revert must run
            // a manual SQL backfill after explicit approval.
        }
    }
}
