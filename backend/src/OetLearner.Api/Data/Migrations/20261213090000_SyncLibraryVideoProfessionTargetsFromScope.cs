using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261213090000_SyncLibraryVideoProfessionTargetsFromScope")]
    public partial class SyncLibraryVideoProfessionTargetsFromScope : Migration
    {
        // Video Library profession targeting: Writing and Speaking videos are strictly
        // profession-specific in BOTH English and Arabic.
        // Synchronize ProfessionIdsJson for all Writing/Speaking videos based on their
        // VisibilityScope:
        // - FULL_MEDICINE / CRASH -> ["medicine", "physiotherapy", "dentistry", "radiography"]
        // - FULL_NURSING -> ["nursing"]
        // - FULL_PHARMACY -> ["pharmacy"]
        // Listening, Reading, and basic-english remain shared across all professions ("[]").

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. FULL_MEDICINE / CRASH Writing & Speaking videos with empty or missing profession targets
            migrationBuilder.Sql(@"
UPDATE ""LibraryVideos""
SET ""ProfessionIdsJson"" = '[""medicine"", ""physiotherapy"", ""dentistry"", ""radiography""]'
WHERE LOWER(COALESCE(""SubtestCode"", '')) IN ('writing', 'speaking')
  AND UPPER(COALESCE(""VisibilityScope"", '')) IN ('FULL_MEDICINE', 'CRASH')
  AND (
    ""ProfessionIdsJson"" IS NULL
    OR ""ProfessionIdsJson"" = '[]'
    OR NOT ((""ProfessionIdsJson"")::jsonb ? 'medicine')
  );");

            // 2. FULL_NURSING Writing & Speaking videos
            migrationBuilder.Sql(@"
UPDATE ""LibraryVideos""
SET ""ProfessionIdsJson"" = '[""nursing""]'
WHERE LOWER(COALESCE(""SubtestCode"", '')) IN ('writing', 'speaking')
  AND UPPER(COALESCE(""VisibilityScope"", '')) = 'FULL_NURSING'
  AND (
    ""ProfessionIdsJson"" IS NULL
    OR ""ProfessionIdsJson"" = '[]'
    OR NOT ((""ProfessionIdsJson"")::jsonb ? 'nursing')
  );");

            // 3. FULL_PHARMACY Writing & Speaking videos
            migrationBuilder.Sql(@"
UPDATE ""LibraryVideos""
SET ""ProfessionIdsJson"" = '[""pharmacy""]'
WHERE LOWER(COALESCE(""SubtestCode"", '')) IN ('writing', 'speaking')
  AND UPPER(COALESCE(""VisibilityScope"", '')) = 'FULL_PHARMACY'
  AND (
    ""ProfessionIdsJson"" IS NULL
    OR ""ProfessionIdsJson"" = '[]'
    OR NOT ((""ProfessionIdsJson"")::jsonb ? 'pharmacy')
  );");

            // 4. Ensure Listening, Reading, and Basic English have '[]'
            migrationBuilder.Sql(@"
UPDATE ""LibraryVideos""
SET ""ProfessionIdsJson"" = '[]'
WHERE LOWER(COALESCE(""SubtestCode"", '')) IN ('listening', 'reading', 'basic-english')
  AND (""ProfessionIdsJson"" IS NULL OR ""ProfessionIdsJson"" <> '[]');");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No destructive down needed for data alignment.
        }
    }
}
