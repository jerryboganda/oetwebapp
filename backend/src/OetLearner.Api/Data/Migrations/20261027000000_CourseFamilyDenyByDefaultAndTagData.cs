using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261027000000_CourseFamilyDenyByDefaultAndTagData")]
    public partial class CourseFamilyDenyByDefaultAndTagData : Migration
    {
        // Owner directive 2026-08-26: replace the earlier per-id include/exclude +
        // subtest-restriction isolation model (migrations 20260822090000,
        // 20260831090000, 20261026090000) with a tag-only family model.
        //
        // What changes:
        //   1. Every existing premium video gets a family batch tag so it stays
        //      visible after the deny-by-default gate goes live.
        //      - 18 known crash-course Writing videos → batch:crash-course-only
        //      - All other premium videos without a family tag → batch:shared
        //   2. The old "videos" override node (include / exclude / excludeTags)
        //      is stripped from every full-* and crash-* plan — no more per-id
        //      maintenance.
        //   3. The video_library.subtests restriction on crash-* plans is removed
        //      — writing becomes granted like every other subtest, and the family
        //      tag is the sole gate.
        //
        // Down restores the subtests restriction and removes the batch tags we
        // added, but does NOT restore the old per-id overrides (data loss accepted
        // — the old model is intentionally retired).

        private const string CrashCourseVideoIds =
            "'vid_0a7f7c153e504c8abbd0d39ac5af7e4c','vid_d5a4fb9818cf4d03acb5d22667daecf7'," +
            "'vid_6f94f9c12e1f4e6f8a9c725683d50e01','vid_ee844a4d9b5947f196e2d1a84335e086'," +
            "'vid_f98228ad43dd4554bd7baefb9b018b53','vid_276bf8ce5e89457988d8791a381a8007'," +
            "'vid_924ed872ceed40f193031fdaa19cb166','vid_e5c1a946a99d426db661d1bf4282cb6e'," +
            "'vid_a6d9cbfec229404f9e569d822a1e49e1','vid_ca26ce5f81ec499fa104104896db6e44'," +
            "'vid_e670bb4a3e324336863649ba2fe46408','vid_2ebde52c771e44b79c4c52ae41cb894e'," +
            "'vid_0fb466adddc04b9d871d90325402484e','vid_ce0bb5be6c9f4966827fc61ec25d3ca0'," +
            "'vid_c10ef349922c4aac95231f5d6403bda1','vid_f999d97a44eb4a63a4f6cf040e598e7b'," +
            "'vid_cd7c2202084f4d27bb9eb689d97f6bc8','vid_7f855ac92cc34d55ae60dca949c60cb1'";

        private const string FullPlanPrefix = "full-";
        private const string CrashPlanPrefixes = "'crash-%','writing-crash%','speaking-crash%'";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1a. Tag the 18 crash videos with batch:crash-course-only if not already present.
            TagVideoBatch(migrationBuilder, CrashCourseVideoIds, "batch:crash-course-only");

            // 1b. Tag every other premium video with batch:shared if no family tag present.
            TagAllOtherPremiumVideos(migrationBuilder);

            // 2. Strip the entire "videos" node from ContentOverridesJson on full-* and crash-* plans.
            StripVideosContentOverride(migrationBuilder, "BillingPlans", updateTimestamp: true);
            StripVideosContentOverride(migrationBuilder, "BillingPlanVersions", updateTimestamp: false);

            // 3. Remove the video_library.subtests key from crash-* plans (keep tier:premium).
            RemoveVideoLibrarySubtests(migrationBuilder, "BillingPlans", updateTimestamp: true);
            RemoveVideoLibrarySubtests(migrationBuilder, "BillingPlanVersions", updateTimestamp: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the batch tags we added (innocuous no-op if already absent).
            RemoveBatchTag(migrationBuilder, "batch:crash-course-only");
            RemoveBatchTag(migrationBuilder, "batch:shared");

            // Strip videos overrides (same as Up — neutral: the old model is gone).
            StripVideosContentOverride(migrationBuilder, "BillingPlans", updateTimestamp: true);
            StripVideosContentOverride(migrationBuilder, "BillingPlanVersions", updateTimestamp: false);

            // Restore the video_library.subtests restriction on crash-* plans.
            SetVideoLibrarySubtests(migrationBuilder, "BillingPlans", updateTimestamp: true);
            SetVideoLibrarySubtests(migrationBuilder, "BillingPlanVersions", updateTimestamp: false);
        }

        // ── Video tag helpers ─────────────────────────────────────────────────

        private static void TagVideoBatch(
            MigrationBuilder migrationBuilder,
            string videoIds,
            string tag)
        {
            // Idempotent: skips videos already carrying the tag.
            migrationBuilder.Sql($@"
UPDATE ""LibraryVideos""
SET ""TagsCsv"" = CASE
    WHEN ""TagsCsv"" IS NULL OR TRIM(""TagsCsv"") = '' THEN '{tag}'
    WHEN POSITION('{tag}' IN LOWER(""TagsCsv"")) = 0 THEN ""TagsCsv"" || ', {tag}'
    ELSE ""TagsCsv"" END
WHERE ""Id"" IN ({videoIds});
");
        }

        private static void TagAllOtherPremiumVideos(MigrationBuilder migrationBuilder)
        {
            // Tag every premium video not in the crash 18 and not already carrying
            // a family tag (batch:crash-course*, batch:full-course*, or batch:shared).
            migrationBuilder.Sql($@"
UPDATE ""LibraryVideos""
SET ""TagsCsv"" = CASE
    WHEN ""TagsCsv"" IS NULL OR TRIM(""TagsCsv"") = '' THEN 'batch:shared'
    WHEN POSITION('batch:crash-course' IN LOWER(""TagsCsv"")) > 0
         OR POSITION('batch:full-course' IN LOWER(""TagsCsv"")) > 0
         OR POSITION('batch:shared' IN LOWER(""TagsCsv"")) > 0
         THEN ""TagsCsv""
    ELSE ""TagsCsv"" || ', batch:shared'
    END
WHERE ""AccessTier"" = 'premium'
  AND ""Id"" NOT IN ({CrashCourseVideoIds});
");
        }

        private static void RemoveBatchTag(MigrationBuilder migrationBuilder, string tag)
        {
            // The tag contains only letters, digits, colons, and hyphens — none
            // are regex metacharacters, so no escaping is needed.
            migrationBuilder.Sql($@"
UPDATE ""LibraryVideos""
SET ""TagsCsv"" = TRIM(BOTH ', ' FROM REGEXP_REPLACE(
    REGEXP_REPLACE(""TagsCsv"", '{tag}\s*,?\s*', '', 'gi'),
    ',\s*$', '', 'g'))
WHERE ""TagsCsv"" LIKE '%{tag}%';
");
        }

        // ── Plan override helpers ─────────────────────────────────────────────

        private static void StripVideosContentOverride(
            MigrationBuilder migrationBuilder,
            string table,
            bool updateTimestamp)
        {
            var timestamp = updateTimestamp ? @", ""UpdatedAt"" = now()" : string.Empty;
            migrationBuilder.Sql($@"
UPDATE ""{table}""
SET ""ContentOverridesJson"" = CAST(
        (CAST(COALESCE(""ContentOverridesJson"", '{{}}') AS jsonb) - 'videos') AS text){timestamp}
WHERE (""Code"" LIKE '{FullPlanPrefix}%' OR ""Code"" LIKE {CrashPlanPrefixes})
  AND ""ContentOverridesJson"" IS NOT NULL;
");
        }

        private static void RemoveVideoLibrarySubtests(
            MigrationBuilder migrationBuilder,
            string table,
            bool updateTimestamp)
        {
            var timestamp = updateTimestamp ? @", ""UpdatedAt"" = now()" : string.Empty;
            migrationBuilder.Sql($@"
UPDATE ""{table}""
SET ""EntitlementsJson"" = CAST(
        jsonb_set(
            CAST(COALESCE(""EntitlementsJson"", '{{}}') AS jsonb),
            '{{video_library}}',
            (CAST(COALESCE(""EntitlementsJson"", '{{}}') AS jsonb) -> 'video_library') - 'subtests',
            true
        ) AS text){timestamp}
WHERE (""Code"" LIKE {CrashPlanPrefixes})
  AND ""EntitlementsJson"" IS NOT NULL
  AND CAST(COALESCE(""EntitlementsJson"", '{{}}') AS jsonb) ? 'video_library';
");
        }

        private static void SetVideoLibrarySubtests(
            MigrationBuilder migrationBuilder,
            string table,
            bool updateTimestamp)
        {
            var timestamp = updateTimestamp ? @", ""UpdatedAt"" = now()" : string.Empty;
            migrationBuilder.Sql($@"
UPDATE ""{table}""
SET ""EntitlementsJson"" = CAST(
        CAST(COALESCE(""EntitlementsJson"", '{{}}') AS jsonb)
        || jsonb_build_object('video_library', jsonb_build_object(
               'tier', 'premium',
               'subtests', CAST('[""listening"",""reading"",""speaking""]' AS jsonb)))
        AS text){timestamp}
WHERE (""Code"" LIKE {CrashPlanPrefixes});
");
        }
    }
}