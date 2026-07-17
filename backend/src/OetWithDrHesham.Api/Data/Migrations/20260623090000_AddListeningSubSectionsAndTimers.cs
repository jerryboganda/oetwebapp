using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Splits the monolithic Listening Part B into six independent sub-sections
    /// (B1..B6) and adds a per-sub-section countdown timer column.
    ///
    /// ListeningPartCode was changed from {A1=1,A2=2,B=3,C1=4,C2=5} to the
    /// sequential {A1=1,A2=2,B1=3,B2=4,B3=5,B4=6,B5=7,B6=8,C1=9,C2=10}. The old
    /// "B=3" rows are reinterpreted as B1 (same int). C1/C2 move 4->9 / 5->10,
    /// so this migration remaps those two persisted integer codes BEFORE the new
    /// B2..B6 rows (codes 4..8) are created, then splits each existing Part B
    /// (its single extract + six questions) into B1..B6.
    ///
    /// Question IDs are preserved (only ListeningPartId / ListeningExtractId are
    /// re-pointed) so existing ListeningAnswer rows are never orphaned — this is
    /// the only safe path for papers that already have learner attempts, which
    /// cannot be re-projected from JSON. Runs once inside EF's per-migration
    /// transaction. Template: 20260620100000_AddReadingSections.
    /// </summary>
    public partial class AddListeningSubSectionsAndTimers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Per-sub-section countdown timer (seconds). Nullable — runtime
            //    falls back to a default when unset.
            migrationBuilder.Sql(@"ALTER TABLE ""ListeningParts"" ADD COLUMN IF NOT EXISTS ""TimeLimitSeconds"" integer NULL;");

            // 2) Remap the two persisted C codes to their new sequential ints.
            //    MUST run before any B2..B6 rows occupy 4..8. C2 first so the two
            //    updates never transiently collide.
            migrationBuilder.Sql(@"UPDATE ""ListeningParts"" SET ""PartCode"" = 10 WHERE ""PartCode"" = 5;");
            migrationBuilder.Sql(@"UPDATE ""ListeningParts"" SET ""PartCode"" = 9  WHERE ""PartCode"" = 4;");

            // 3) Create B2..B6 part rows for every paper that still has a single
            //    Part B (now reinterpreted as B1 = code 3). Deterministic ids via
            //    md5(b1Id || ':Bn'); guarded by NOT EXISTS so a re-run is a no-op.
            migrationBuilder.Sql(@"
INSERT INTO ""ListeningParts"" (""Id"", ""PaperId"", ""PartCode"", ""MaxRawScore"", ""Instructions"", ""TimeLimitSeconds"", ""CreatedAt"", ""UpdatedAt"")
SELECT md5(b1.""Id"" || ':B2'), b1.""PaperId"", 4, 1, NULL, NULL, NOW(), NOW()
FROM ""ListeningParts"" b1
WHERE b1.""PartCode"" = 3
  AND NOT EXISTS (SELECT 1 FROM ""ListeningParts"" x WHERE x.""PaperId"" = b1.""PaperId"" AND x.""PartCode"" = 4);
INSERT INTO ""ListeningParts"" (""Id"", ""PaperId"", ""PartCode"", ""MaxRawScore"", ""Instructions"", ""TimeLimitSeconds"", ""CreatedAt"", ""UpdatedAt"")
SELECT md5(b1.""Id"" || ':B3'), b1.""PaperId"", 5, 1, NULL, NULL, NOW(), NOW()
FROM ""ListeningParts"" b1
WHERE b1.""PartCode"" = 3
  AND NOT EXISTS (SELECT 1 FROM ""ListeningParts"" x WHERE x.""PaperId"" = b1.""PaperId"" AND x.""PartCode"" = 5);
INSERT INTO ""ListeningParts"" (""Id"", ""PaperId"", ""PartCode"", ""MaxRawScore"", ""Instructions"", ""TimeLimitSeconds"", ""CreatedAt"", ""UpdatedAt"")
SELECT md5(b1.""Id"" || ':B4'), b1.""PaperId"", 6, 1, NULL, NULL, NOW(), NOW()
FROM ""ListeningParts"" b1
WHERE b1.""PartCode"" = 3
  AND NOT EXISTS (SELECT 1 FROM ""ListeningParts"" x WHERE x.""PaperId"" = b1.""PaperId"" AND x.""PartCode"" = 6);
INSERT INTO ""ListeningParts"" (""Id"", ""PaperId"", ""PartCode"", ""MaxRawScore"", ""Instructions"", ""TimeLimitSeconds"", ""CreatedAt"", ""UpdatedAt"")
SELECT md5(b1.""Id"" || ':B5'), b1.""PaperId"", 7, 1, NULL, NULL, NOW(), NOW()
FROM ""ListeningParts"" b1
WHERE b1.""PartCode"" = 3
  AND NOT EXISTS (SELECT 1 FROM ""ListeningParts"" x WHERE x.""PaperId"" = b1.""PaperId"" AND x.""PartCode"" = 7);
INSERT INTO ""ListeningParts"" (""Id"", ""PaperId"", ""PartCode"", ""MaxRawScore"", ""Instructions"", ""TimeLimitSeconds"", ""CreatedAt"", ""UpdatedAt"")
SELECT md5(b1.""Id"" || ':B6'), b1.""PaperId"", 8, 1, NULL, NULL, NOW(), NOW()
FROM ""ListeningParts"" b1
WHERE b1.""PartCode"" = 3
  AND NOT EXISTS (SELECT 1 FROM ""ListeningParts"" x WHERE x.""PaperId"" = b1.""PaperId"" AND x.""PartCode"" = 8);
");

            // 4) One extract per new B part (the projection keeps one extract per
            //    part code). Workplace kind (=1); DisplayOrder = (int)partCode to
            //    match ListeningBackfillService. Idempotent per part.
            migrationBuilder.Sql(@"
INSERT INTO ""ListeningExtracts"" (""Id"", ""ListeningPartId"", ""DisplayOrder"", ""Kind"", ""Title"", ""SpeakersJson"", ""ReplayInLearningOnly"", ""TranscriptSegmentsJson"", ""CreatedAt"", ""UpdatedAt"")
SELECT md5(nb.""Id"" || ':ext'), nb.""Id"", nb.""PartCode"", 1,
       'Workplace extract ' || (nb.""PartCode"" - 2)::text, '[]', TRUE, '[]', NOW(), NOW()
FROM ""ListeningParts"" nb
WHERE nb.""PartCode"" IN (4, 5, 6, 7, 8)
  AND NOT EXISTS (SELECT 1 FROM ""ListeningExtracts"" e WHERE e.""ListeningPartId"" = nb.""Id"");
");

            // 5) Re-home the six Part B questions. Ranked by QuestionNumber within
            //    each paper: rank 1 stays on B1, ranks 2..6 move to B2..B6 and
            //    re-point to that sub-section's new extract. Question IDs unchanged.
            migrationBuilder.Sql(@"
WITH ranked AS (
    SELECT q.""Id"" AS qid, q.""PaperId"",
           ROW_NUMBER() OVER (PARTITION BY q.""PaperId"" ORDER BY q.""QuestionNumber"") AS rn
    FROM ""ListeningQuestions"" q
    JOIN ""ListeningParts"" p ON p.""Id"" = q.""ListeningPartId""
    WHERE p.""PartCode"" = 3
)
UPDATE ""ListeningQuestions"" q
SET ""ListeningPartId"" = nb.""Id"",
    ""ListeningExtractId"" = ne.""Id""
FROM ranked r
JOIN ""ListeningParts"" nb ON nb.""PaperId"" = r.""PaperId"" AND nb.""PartCode"" = 2 + r.rn
JOIN ""ListeningExtracts"" ne ON ne.""ListeningPartId"" = nb.""Id""
WHERE q.""Id"" = r.qid AND r.rn BETWEEN 2 AND 6;
");

            // 6) Each B sub-section now holds a single 1-point item.
            migrationBuilder.Sql(@"UPDATE ""ListeningParts"" SET ""MaxRawScore"" = 1 WHERE ""PartCode"" = 3 AND ""MaxRawScore"" <> 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Collapse B2..B6 back onto B1 (re-point questions to B1's extract),
            // then delete the B2..B6 extracts and parts.
            migrationBuilder.Sql(@"
UPDATE ""ListeningQuestions"" q
SET ""ListeningPartId"" = b1.""Id"",
    ""ListeningExtractId"" = (SELECT e.""Id"" FROM ""ListeningExtracts"" e WHERE e.""ListeningPartId"" = b1.""Id"" ORDER BY e.""DisplayOrder"" LIMIT 1)
FROM ""ListeningParts"" b1
JOIN ""ListeningParts"" bn ON bn.""PaperId"" = b1.""PaperId"" AND bn.""PartCode"" IN (4, 5, 6, 7, 8)
WHERE b1.""PartCode"" = 3 AND q.""ListeningPartId"" = bn.""Id"";
");
            migrationBuilder.Sql(@"
DELETE FROM ""ListeningExtracts"" e
USING ""ListeningParts"" bn
WHERE bn.""PartCode"" IN (4, 5, 6, 7, 8) AND e.""ListeningPartId"" = bn.""Id"";
");
            migrationBuilder.Sql(@"DELETE FROM ""ListeningParts"" WHERE ""PartCode"" IN (4, 5, 6, 7, 8);");

            // Restore B1's MaxRawScore to its actual (re-collapsed) item count.
            migrationBuilder.Sql(@"
UPDATE ""ListeningParts"" b1
SET ""MaxRawScore"" = GREATEST(1, (SELECT COUNT(*) FROM ""ListeningQuestions"" q WHERE q.""ListeningPartId"" = b1.""Id""))
WHERE b1.""PartCode"" = 3;
");

            // Remap C codes back (9->4, 10->5) now that 4/5 are free again.
            migrationBuilder.Sql(@"UPDATE ""ListeningParts"" SET ""PartCode"" = 4 WHERE ""PartCode"" = 9;");
            migrationBuilder.Sql(@"UPDATE ""ListeningParts"" SET ""PartCode"" = 5 WHERE ""PartCode"" = 10;");

            migrationBuilder.Sql(@"ALTER TABLE ""ListeningParts"" DROP COLUMN IF EXISTS ""TimeLimitSeconds"";");
        }
    }
}
