using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260809100000_ExpandArabicVideoSharingAndAddCourseFolders")]
    public partial class ExpandArabicVideoSharingAndAddCourseFolders : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CourseFolder",
                table: "LibraryVideos",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            if (!ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)) return;

            // One canonical Medicine Arabic Writing/Speaking set is projected to all
            // four requested professions. No video, Bunny id, progress, or asset is copied.
            migrationBuilder.Sql("""
                UPDATE "LibraryVideos"
                SET "ProfessionIdsJson" = '["medicine","physiotherapy","dentistry","radiography"]',
                    "UpdatedAt" = CURRENT_TIMESTAMP
                WHERE lower(coalesce("Language", '')) = 'ar'
                  AND lower(coalesce("SubtestCode", '')) IN ('writing','speaking')
                  AND (
                    "ProfessionIdsJson"::jsonb ? 'medicine'
                    OR "ProfessionIdsJson"::jsonb ? 'physiotherapy'
                    OR "ProfessionIdsJson"::jsonb ? 'dentistry'
                    OR "ProfessionIdsJson"::jsonb ? 'radiography'
                  );
                """);

            // Existing Workshop-labelled rows go to Workshops; every other existing
            // Writing/Speaking row goes to Sessions. This is idempotent and metadata-only.
            migrationBuilder.Sql("""
                UPDATE "LibraryVideos" v
                SET "CourseFolder" = CASE
                    WHEN lower(coalesce(v."Title", '')) LIKE '%workshop%'
                      OR EXISTS (
                        SELECT 1
                        FROM "VideoCategoryItems" i
                        JOIN "VideoCategories" c ON c."Id" = i."CategoryId"
                        WHERE i."VideoId" = v."Id"
                          AND lower(coalesce(c."Title", '')) LIKE '%workshop%'
                      )
                    THEN 'workshops'
                    ELSE 'sessions'
                  END,
                  "UpdatedAt" = CURRENT_TIMESTAMP
                WHERE lower(coalesce(v."SubtestCode", '')) IN ('writing','speaking')
                  AND v."CourseFolder" IS NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CourseFolder", table: "LibraryVideos");
        }
    }
}
