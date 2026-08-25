using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261026090000_TwoWayWritingVideoIsolation")]
    public partial class TwoWayWritingVideoIsolation : Migration
    {
        // Owner directive 2026-08-26: supersede the narrower 18-id rule in
        // 20260822090000_RestrictCrashCourseVideoAccess with a two-way
        // tag-driven isolation that the admin can manage from the video
        // upload/edit form going forward.
        //
        // Mechanism: extend the existing "videos" override node with a new
        // "excludeTags" array. Videos whose TagsCsv contains any of those
        // tags are denied. An explicit per-plan video include id still wins,
        // matching the spec's "explicit include beats exclude" rule.
        //
        // Scope: Writing videos only (per owner audit directive — Listening,
        // Reading, and Speaking are largely shared and were not flagged for
        // additional isolation).
        //
        // Writing Crash Course / Fast Track folders (owner-confirmed):
        //   - "Arabic / New Medicine Crash Course / Sessions / Day 1"
        //   - "Arabic / New Medicine Crash Course / Sessions / Day 2"
        //   - "Medicine / Arabic / Fast-Track Crash Course"
        //   - "Arabic / New Medicine Crash Course / Workshops"
        //
        // The first two folders correspond to the 18 videos already covered
        // by 20260822090000 (batch:new-medicine-crash-course,
        // batch:writing-sessions-crash-course-old). The latter two folders
        // are new territory — admins tag those videos with
        // batch:crash-course-arabic-writing and batch:crash-course-workshops
        // respectively. The migration does NOT auto-tag live videos for the
        // new batches (no live data here, would be a destructive bulk
        // rewrite), but the entitlement service and the admin form are now
        // ready to honour those tags the moment an admin publishes one.
        //
        // Per-plan ContentOverridesJson merge target:
        //   full-*       ← add excludeTags for all four Writing crash-course
        //                   batch tags (and the prior spec's two batch tags)
        //   crash-*      ← (no automatic exclusion in this migration; reverse
        //                   isolation for Full-Course-only Writing is left to
        //                   admin tagging of future uploads with
        //                   batch:full-course-only, which the resolver
        //                   already understands via excludeTags on crash-*).

        private const string FullCoursePlanPrefix = "full-";
        private const string FullCourseExcludedTagsJson =
            "[\"batch:crash-course-arabic-writing\"," +
            "\"batch:writing-sessions-crash-course-old\"," +
            "\"batch:fast-track-crash-course\"," +
            "\"batch:crash-course-workshops\"]";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Append excludeTags to the "videos" node of every current or
            // legacy full-* plan (live + immutable purchase snapshot).
            // Idempotent: existing excludeTags arrays on the same plan are
            // unioned rather than overwritten.
            AppendExcludeTags(migrationBuilder, "BillingPlans", updateTimestamp: true);
            AppendExcludeTags(migrationBuilder, "BillingPlanVersions", updateTimestamp: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the excludeTags we added, leaving any pre-existing
            // ones (if an admin has since added their own) intact. Other
            // override keys (include/exclude) are untouched.
            RemoveExcludeTags(migrationBuilder, "BillingPlans", updateTimestamp: true);
            RemoveExcludeTags(migrationBuilder, "BillingPlanVersions", updateTimestamp: false);
        }

        private static void AppendExcludeTags(
            MigrationBuilder migrationBuilder,
            string table,
            bool updateTimestamp)
        {
            var timestamp = updateTimestamp ? @", ""UpdatedAt"" = now()" : string.Empty;
            migrationBuilder.Sql($@"
UPDATE ""{table}""
SET ""ContentOverridesJson"" = CAST(
        CAST(COALESCE(""ContentOverridesJson"", '{{}}') AS jsonb)
        || jsonb_build_object(
            'videos',
            jsonb_build_object(
                'excludeTags',
                (
                    SELECT COALESCE(jsonb_agg(DISTINCT value), '[]'::jsonb)
                    FROM (
                        SELECT jsonb_array_elements_text(
                            COALESCE(
                                CAST(COALESCE(""ContentOverridesJson"", '{{}}') AS jsonb)
                                    -> 'videos' -> 'excludeTags',
                                '[]'::jsonb
                            )
                        ) AS value
                        UNION
                        SELECT jsonb_array_elements_text(CAST('{FullCourseExcludedTagsJson}' AS jsonb)) AS value
                    ) AS combined
                )
            )
        )
        AS text){timestamp}
WHERE ""Code"" LIKE '{FullCoursePlanPrefix}%';
");
        }

        private static void RemoveExcludeTags(
            MigrationBuilder migrationBuilder,
            string table,
            bool updateTimestamp)
        {
            var timestamp = updateTimestamp ? @", ""UpdatedAt"" = now()" : string.Empty;
            migrationBuilder.Sql($@"
UPDATE ""{table}""
SET ""ContentOverridesJson"" = CAST(
        (
            CAST(COALESCE(""ContentOverridesJson"", '{{}}') AS jsonb)
            || jsonb_build_object(
                'videos',
                (CAST(COALESCE(""ContentOverridesJson"", '{{}}') AS jsonb) -> 'videos')
                    - 'excludeTags'
            )
        ) AS text){timestamp}
WHERE ""Code"" LIKE '{FullCoursePlanPrefix}%'
  AND CAST(COALESCE(""ContentOverridesJson"", '{{}}') AS jsonb)
        -> 'videos' -> 'excludeTags' IS NOT NULL;
");
        }
    }
}
