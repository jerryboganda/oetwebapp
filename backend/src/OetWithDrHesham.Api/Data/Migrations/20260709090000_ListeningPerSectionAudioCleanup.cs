using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Moves Listening to the per-section audio model: Part B now plays ONE
    /// shared audio across all six questions, and Part A is always
    /// per-subsection (A1, A2). This cleans up the data the old model left:
    ///
    ///   1. Promotes a legacy per-question B1 audio upload to the canonical
    ///      single Part B clip (Part "B"), unless a "B" clip already exists.
    ///   2. Deletes every remaining per-question Part B audio asset (B1..B6).
    ///      Media rows are intentionally left for the orphan sweep.
    ///   3. Strips the obsolete "single audio" Part A flag
    ///      (partAAudioMode) from ContentPaper.ExtractedTextJson.
    ///   4. Consolidates the Part B countdown onto B1 and clears B2..B6 timers.
    ///
    /// Audio is a ContentPaperAsset(Role=Audio=0, Part). ListeningPartCode ints:
    /// A1=1, A2=2, B1=3, B2=4, B3=5, B4=6, B5=7, B6=8, C1=9, C2=10.
    ///
    /// Down() is a no-op: deleted per-question audio assets cannot be recovered.
    /// Hand-written (inline [Migration] attribute, no Designer) because the EF
    /// Core model snapshot is intentionally drifted — see the sibling
    /// 20260708090000 migration and memories/repo/migration-drift-note.md.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260709090000_ListeningPerSectionAudioCleanup")]
    public partial class ListeningPerSectionAudioCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Promote a legacy per-question B1 upload to the single Part B
            //    audio, but only when the paper has no canonical "B" upload yet.
            //    Part is author-entered free text, so compare case-insensitively.
            migrationBuilder.Sql(@"
UPDATE ""ContentPaperAssets"" a
SET ""Part"" = 'B'
WHERE a.""Role"" = 0
  AND UPPER(a.""Part"") = 'B1'
  AND NOT EXISTS (
      SELECT 1 FROM ""ContentPaperAssets"" b
      WHERE b.""PaperId"" = a.""PaperId"" AND b.""Role"" = 0 AND UPPER(b.""Part"") = 'B');
");

            // 2) Drop every remaining per-question Part B audio (B1..B6). After
            //    step 1 the canonical clip is stored under ""B"", so anything still
            //    tagged B1..B6 is a now-unused leftover. Media rows stay behind for
            //    the orphan sweep.
            migrationBuilder.Sql(@"
DELETE FROM ""ContentPaperAssets""
WHERE ""Role"" = 0 AND UPPER(""Part"") IN ('B1', 'B2', 'B3', 'B4', 'B5', 'B6');
");

            // 3) Remove the obsolete Part A ""single audio"" flag from the paper
            //    JSON blob (plain text column holding JSON). Only touch rows that
            //    actually contain the key to minimise jsonb casts.
            migrationBuilder.Sql(@"
UPDATE ""ContentPapers""
SET ""ExtractedTextJson"" = (""ExtractedTextJson""::jsonb - 'partAAudioMode')::text
WHERE ""ExtractedTextJson"" LIKE '%partAAudioMode%';
");

            // 4) Consolidate the Part B countdown onto B1 (the learner-section
            //    representative); clear the now-unused B2..B6 timers (codes 4..8).
            migrationBuilder.Sql(@"UPDATE ""ListeningParts"" SET ""TimeLimitSeconds"" = NULL WHERE ""PartCode"" IN (4, 5, 6, 7, 8);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: this migration deletes per-question Part B audio assets and
            // strips a JSON flag — neither is reversible. Re-uploading audio and
            // re-toggling the (removed) Part A mode is a manual, forward-only fix.
        }
    }
}
