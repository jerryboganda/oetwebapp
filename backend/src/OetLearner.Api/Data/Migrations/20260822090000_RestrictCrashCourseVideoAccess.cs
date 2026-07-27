using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260822090000_RestrictCrashCourseVideoAccess")]
    public partial class RestrictCrashCourseVideoAccess : Migration
    {
        // Owner directive 2026-07-27: scope the "Crash Course" Writing videos (the only
        // subtest/profession combo with dedicated crash-course content today — confirmed
        // live via VideoCategories: 11 "New Medicine Crash Course" + 7 "Crash Course Old"
        // Writing videos, Arabic, Medicine profession group. No Speaking crash-course videos
        // and no Nursing/Pharmacy/other-profession crash-course videos exist yet).
        //
        //   * Full Course Medicine (full-condensed-medicine[-tbook]): every video stays
        //     allowed EXCEPT these 18 crash-course Writing videos.
        //   * Crash Course plans (crash-course, crash-3letters, crash-5letters — profession
        //     "all"): Listening/Reading/Speaking are untouched (same open access as today,
        //     still subject to the existing profession gate on each video). Writing is
        //     narrowed to ONLY these 18 crash-course videos — any other profession's Writing
        //     videos (Nursing/Pharmacy/…) are not crash-course-tagged, so those learners get
        //     no Writing videos under this plan until such content exists, matching the owner's
        //     confirmation that no crash-course content exists outside Arabic Medicine.
        //
        // MECHANISM: reuses the existing "subtest × profession + per-plan content override"
        // engine (VideoEntitlementService / BillingPlan.ContentOverridesJson) — no code change
        // needed, only catalog data. The video_library.subtests restriction narrows the
        // automatic premium grant to listening/reading/speaking; the ContentOverridesJson
        // "videos.include" list then carves the 18 crash-course ids back in for Writing (an
        // explicit include beats the subtest scope, per VideoEntitlementService.Evaluate).
        // Both BillingPlans (live) and BillingPlanVersions (immutable purchase snapshot) are
        // updated so the change reaches already-active subscribers immediately, not just new
        // signups — VideoEntitlementService.ResolvePlanEntitlementsJsonAsync prefers the
        // subscription's pinned PlanVersionId snapshot when one is recorded.
        private const string FullCourseMedicineCodes =
            "'full-condensed-medicine','full-condensed-medicine-tbook'";

        private const string CrashCoursePlanCodes =
            "'crash-course','crash-3letters','crash-5letters'";

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
            // Full Course Medicine: exclude the 18 crash-course Writing videos; everything
            // else stays open (unchanged).
            SetContentOverride(migrationBuilder, "BillingPlans", "exclude", FullCourseMedicineCodes, updateTimestamp: true);
            SetContentOverride(migrationBuilder, "BillingPlanVersions", "exclude", FullCourseMedicineCodes, updateTimestamp: false);

            // Crash Course plans: narrow the video_library grant to listening/reading/speaking
            // (Writing is dropped from the blanket grant)…
            SetVideoLibrarySubtests(migrationBuilder, "BillingPlans", CrashCoursePlanCodes, updateTimestamp: true);
            SetVideoLibrarySubtests(migrationBuilder, "BillingPlanVersions", CrashCoursePlanCodes, updateTimestamp: false);

            // …then carve the 18 crash-course videos back in for Writing.
            SetContentOverride(migrationBuilder, "BillingPlans", "include", CrashCoursePlanCodes, updateTimestamp: true);
            SetContentOverride(migrationBuilder, "BillingPlanVersions", "include", CrashCoursePlanCodes, updateTimestamp: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveContentOverride(migrationBuilder, "BillingPlans", $"{FullCourseMedicineCodes},{CrashCoursePlanCodes}", updateTimestamp: true);
            RemoveContentOverride(migrationBuilder, "BillingPlanVersions", $"{FullCourseMedicineCodes},{CrashCoursePlanCodes}", updateTimestamp: false);

            RemoveVideoLibrarySubtests(migrationBuilder, "BillingPlans", CrashCoursePlanCodes, updateTimestamp: true);
            RemoveVideoLibrarySubtests(migrationBuilder, "BillingPlanVersions", CrashCoursePlanCodes, updateTimestamp: false);
        }

        private static void SetContentOverride(
            MigrationBuilder migrationBuilder,
            string table,
            string direction,
            string planCodes,
            bool updateTimestamp)
        {
            var timestamp = updateTimestamp ? @", ""UpdatedAt"" = now()" : string.Empty;
            migrationBuilder.Sql($@"
UPDATE ""{table}""
SET ""ContentOverridesJson"" = CAST(
        CAST(COALESCE(""ContentOverridesJson"", '{{}}') AS jsonb)
        || jsonb_build_object('videos', jsonb_build_object('{direction}', CAST('{CrashCourseWritingVideoIdsJson}' AS jsonb)))
        AS text){timestamp}
WHERE ""Code"" IN ({planCodes});
");
        }

        private static void RemoveContentOverride(
            MigrationBuilder migrationBuilder,
            string table,
            string planCodes,
            bool updateTimestamp)
        {
            var timestamp = updateTimestamp ? @", ""UpdatedAt"" = now()" : string.Empty;
            migrationBuilder.Sql($@"
UPDATE ""{table}""
SET ""ContentOverridesJson"" = CAST(
        (CAST(COALESCE(""ContentOverridesJson"", '{{}}') AS jsonb) - 'videos') AS text){timestamp}
WHERE ""Code"" IN ({planCodes})
  AND ""ContentOverridesJson"" IS NOT NULL;
");
        }

        private static void SetVideoLibrarySubtests(
            MigrationBuilder migrationBuilder,
            string table,
            string planCodes,
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
WHERE ""Code"" IN ({planCodes});
");
        }

        private static void RemoveVideoLibrarySubtests(
            MigrationBuilder migrationBuilder,
            string table,
            string planCodes,
            bool updateTimestamp)
        {
            var timestamp = updateTimestamp ? @", ""UpdatedAt"" = now()" : string.Empty;
            migrationBuilder.Sql($@"
UPDATE ""{table}""
SET ""EntitlementsJson"" = CAST(
        (CAST(COALESCE(""EntitlementsJson"", '{{}}') AS jsonb) - 'video_library') AS text){timestamp}
WHERE ""Code"" IN ({planCodes})
  AND ""EntitlementsJson"" IS NOT NULL;
");
        }
    }
}
