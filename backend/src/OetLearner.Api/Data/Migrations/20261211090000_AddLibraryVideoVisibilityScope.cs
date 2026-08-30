using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261211090000_AddLibraryVideoVisibilityScope")]
    public partial class AddLibraryVideoVisibilityScope : Migration
    {
        // Video visibility rules (spec §2/§6): LibraryVideos gains a first-class VisibilityScope
        // (SHARED | FULL_MEDICINE | FULL_NURSING | FULL_PHARMACY | CRASH) so Writing/Speaking
        // videos can be isolated per package while Listening/Reading stay shared. Nullable ON
        // PURPOSE — null/empty rows are legacy content the engine still gates with the tag/label
        // course-family classifier, so this migration can never lock anyone out mid-deploy.
        //
        // Hand-authored like every production migration (raw `dotnet ef migrations add` output
        // re-creates live tables). The ModelSnapshot is intentionally left untouched.
        //
        // SAFETY: idempotent — ADD COLUMN IF NOT EXISTS; every backfill UPDATE is guarded with
        // "VisibilityScope" IS NULL so re-runs are no-ops and the crash-first precedence holds
        // (each later UPDATE only touches still-unset rows). No data is destroyed; Down drops
        // only the added column.
        //
        // BACKFILL ORDER (crash-first precedence): 7.2a CRASH → 7.2b FULL_PHARMACY →
        // 7.2c FULL_NURSING → 7.2d FULL_MEDICINE (all remaining W/S) → 7.2e SHARED (L/R/
        // basic-english). The two NULL-subtest draft rows are intentionally left null.
        // batch:* tags are matched as-is (OQ-5) — no translation layer.

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Additive nullable column.
            migrationBuilder.Sql(@"ALTER TABLE ""LibraryVideos"" ADD COLUMN IF NOT EXISTS ""VisibilityScope"" character varying(32);");

            // 7.2a — CRASH first (W/S videos whose batch tag/category marks them crash).
            migrationBuilder.Sql(@"
UPDATE ""LibraryVideos"" v
SET ""VisibilityScope"" = 'CRASH'
WHERE v.""VisibilityScope"" IS NULL
  AND LOWER(COALESCE(v.""SubtestCode"",'')) IN ('writing','speaking')
  AND (
    POSITION('batch:crash-course'     IN LOWER(COALESCE(v.""TagsCsv"",''))) > 0
    OR POSITION('batch:fast-track-crash' IN LOWER(COALESCE(v.""TagsCsv"",''))) > 0
    OR POSITION('batch:writing-sessions-crash-course' IN LOWER(COALESCE(v.""TagsCsv"",''))) > 0
    OR POSITION('batch:new-medicine-crash-course' IN LOWER(COALESCE(v.""TagsCsv"",''))) > 0
    OR EXISTS (
         SELECT 1 FROM ""VideoCategoryItems"" i
         JOIN ""VideoCategories"" c ON c.""Id"" = i.""CategoryId""
         WHERE i.""VideoId"" = v.""Id""
           AND (c.""Title"" ILIKE '%crash%' OR c.""Title"" ILIKE '%fast-track%' OR c.""Title"" ILIKE '%fast track%'))
  );");

            // 7.2b — FULL_PHARMACY (remaining W/S with a pharmacy signal).
            migrationBuilder.Sql(@"
UPDATE ""LibraryVideos"" v
SET ""VisibilityScope"" = 'FULL_PHARMACY'
WHERE v.""VisibilityScope"" IS NULL
  AND LOWER(COALESCE(v.""SubtestCode"",'')) IN ('writing','speaking')
  AND (
    (v.""ProfessionIdsJson"")::jsonb ? 'pharmacy'
    OR EXISTS (SELECT 1 FROM ""VideoCategoryItems"" i JOIN ""VideoCategories"" c ON c.""Id"" = i.""CategoryId""
               WHERE i.""VideoId"" = v.""Id"" AND c.""Title"" ILIKE '%pharmacy%')
  );");

            // 7.2c — FULL_NURSING (remaining W/S with a nursing signal).
            migrationBuilder.Sql(@"
UPDATE ""LibraryVideos"" v
SET ""VisibilityScope"" = 'FULL_NURSING'
WHERE v.""VisibilityScope"" IS NULL
  AND LOWER(COALESCE(v.""SubtestCode"",'')) IN ('writing','speaking')
  AND (
    (v.""ProfessionIdsJson"")::jsonb ? 'nursing'
    OR EXISTS (SELECT 1 FROM ""VideoCategoryItems"" i JOIN ""VideoCategories"" c ON c.""Id"" = i.""CategoryId""
               WHERE i.""VideoId"" = v.""Id"" AND c.""Title"" ILIKE '%nursing%')
  );");

            // 7.2d — FULL_MEDICINE (all remaining W/S — medicine group + empty/ambiguous,
            // platform default per OQ-3).
            migrationBuilder.Sql(@"
UPDATE ""LibraryVideos"" v
SET ""VisibilityScope"" = 'FULL_MEDICINE'
WHERE v.""VisibilityScope"" IS NULL
  AND LOWER(COALESCE(v.""SubtestCode"",'')) IN ('writing','speaking');");

            // 7.2e — SHARED (explicit for L/R/basic-english so prod is fully populated).
            migrationBuilder.Sql(@"
UPDATE ""LibraryVideos"" v
SET ""VisibilityScope"" = 'SHARED'
WHERE v.""VisibilityScope"" IS NULL
  AND LOWER(COALESCE(v.""SubtestCode"",'')) IN ('listening','reading','basic-english');");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""LibraryVideos"" DROP COLUMN IF EXISTS ""VisibilityScope"";");
        }
    }
}
