using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>RecallSetOccurrencesJson</c> to <c>VocabularyTerms</c> — the
    /// per-recall-set occurrence map that becomes the SOURCE OF TRUTH for the
    /// learner "×N" badge. JSON object: canonical set code → times the term
    /// appears in that set's CSV, e.g. <c>{"old":2,"2023-2025":3,"2026":12}</c>.
    /// <c>ExamFrequencyCount</c> and <c>RecallSetCodesJson</c> become derived
    /// caches (sum of values / keys). New rows default to <c>"{}"</c>; existing
    /// rows keep their current derived values until each set's CSV is re-imported.
    /// Hand-written because the EF Core model snapshot is drifted (see
    /// <c>memories/repo/migration-drift-note.md</c>).
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260708090000_AddVocabularyRecallSetOccurrencesJson")]
    public partial class AddVocabularyRecallSetOccurrencesJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: an earlier partial deploy may have added the column.
            // PostgreSQL ALTER TABLE ... ADD COLUMN IF NOT EXISTS is safe and atomic.
            migrationBuilder.Sql(@"
                ALTER TABLE ""VocabularyTerms""
                    ADD COLUMN IF NOT EXISTS ""RecallSetOccurrencesJson"" text NOT NULL DEFAULT '{}';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RecallSetOccurrencesJson", table: "VocabularyTerms");
        }
    }
}
