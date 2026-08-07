using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260831090000_ApplyCrashCourseVideoExclusionsToFullCourses")]
    public partial class ApplyCrashCourseVideoExclusionsToFullCourses : Migration
    {
        // These are the 11 New Medicine Crash Course and 7 Crash Course Old
        // Arabic Writing videos verified in production. They are shared by the
        // Medicine, Physiotherapy, Dentistry, and Radiography content map.
        private const string CrashCourseWritingVideoIdsJson =
            "[\"vid_0a7f7c153e504c8abbd0d39ac5af7e4c\", \"vid_d5a4fb9818cf4d03acb5d22667daecf7\", " +
            "\"vid_6f94f9c12e1f4e6f8a9c725683d50e01\", \"vid_ee844a4d9b5947f196e2d1a84335e086\", " +
            "\"vid_f98228ad43dd4554bd7baefb9b018b53\", \"vid_276bf8ce5e89457988d8791a381a8007\", " +
            "\"vid_924ed872ceed40f193031fdaa19cb166\", \"vid_e5c1a946a99d426db661d1bf4282cb6e\", " +
            "\"vid_a6d9cbfec229404f9e569d822a1e49e1\", \"vid_ca26ce5f81ec499fa104104896db6e44\", " +
            "\"vid_e670bb4a3e324336863649ba2fe46408\", \"vid_2ebde52c771e44b79c4c52ae41cb894e\", " +
            "\"vid_0fb466adddc04b9d871d90325402484e\", \"vid_ce0bb5be6c9f4966827fc61ec25d3ca0\", " +
            "\"vid_c10ef349922c4aac95231f5d6403bda1\", \"vid_f999d97a44eb4a63a4f6cf040e598e7b\", " +
            "\"vid_cd7c2202084f4d27bb9eb689d97f6bc8\", \"vid_7f855ac92cc34d55ae60dca949c60cb1\"]";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!migrationBuilder.ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)) return;

            ApplyFullCourseRule(migrationBuilder, "BillingPlans", updateTimestamp: true);
            ApplyFullCourseRule(migrationBuilder, "BillingPlanVersions", updateTimestamp: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately no-op: removing these entries could undo the original
            // Crash Course rule or an administrator-managed content exclusion.
        }

        private static void ApplyFullCourseRule(
            MigrationBuilder migrationBuilder,
            string table,
            bool updateTimestamp)
        {
            var timestamp = updateTimestamp ? @", ""UpdatedAt"" = now()" : string.Empty;
            migrationBuilder.Sql($@"
WITH source AS (
    SELECT ""Id"",
           CAST(COALESCE(""ContentOverridesJson"", '{{}}') AS jsonb) AS base
    FROM ""{table}""
    WHERE ""Code"" LIKE 'full-%'
), arrays AS (
    SELECT ""Id"",
           base,
           (
               SELECT COALESCE(jsonb_agg(value), '[]'::jsonb)
               FROM (
                   SELECT item.value
                   FROM jsonb_array_elements(
                       COALESCE(base #> '{{videos,include}}', '[]'::jsonb)
                   ) AS item(value)
                   WHERE NOT EXISTS (
                       SELECT 1
                       FROM jsonb_array_elements(CAST('{CrashCourseWritingVideoIdsJson}' AS jsonb)) AS blocked(value)
                       WHERE blocked.value = item.value
                   )
               ) AS filtered
           ) AS filtered_includes,
           (
               SELECT COALESCE(jsonb_agg(value), '[]'::jsonb)
               FROM (
                   SELECT DISTINCT merged.value
                   FROM jsonb_array_elements(
                       COALESCE(base #> '{{videos,exclude}}', '[]'::jsonb)
                       || CAST('{CrashCourseWritingVideoIdsJson}' AS jsonb)
                   ) AS merged(value)
               ) AS deduplicated
           ) AS merged_excludes
    FROM source
), normalized AS (
    SELECT ""Id"",
           jsonb_set(
               base,
               '{{videos}}',
               COALESCE(base -> 'videos', '{{}}'::jsonb)
                   || jsonb_build_object(
                       'include', filtered_includes,
                       'exclude', merged_excludes
                   ),
               true
           ) AS content_overrides
    FROM arrays
)
UPDATE ""{table}"" AS target
SET ""ContentOverridesJson"" = CAST(normalized.content_overrides AS text){timestamp}
FROM normalized
WHERE target.""Id"" = normalized.""Id"";
");
        }
    }
}
