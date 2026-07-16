using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Access &amp; payment spec 2026-07-15 §3 — collapse the three parallel profession
    /// taxonomies onto the canonical SignupProfessionCatalog list.
    ///
    ///   1. Professions gains the missing 'other-allied-health' row. It is registerable
    ///      (SignupProfessionCatalog seeds it) but absent from the reference registry, so
    ///      the Materials discipline filter fell through and those learners saw EVERY
    ///      discipline's materials. Same reasoning as 20260726090000: prod is already
    ///      seeded, so reference rows only reach it via a migration.
    ///   2. The billing taxonomy's 'allied_health' is remapped to the canonical
    ///      'other-allied-health'. One plan (plan_full-allied-health) and its ACTIVE
    ///      version snapshot carry it; the snapshot is updated too, otherwise checkout
    ///      resolving the profession from the version would still see a non-catalog id.
    ///   3. Legacy EntitlementsJson.video_library.subtests moves into the existing
    ///      IncludedSubtestsJson column, which is now the single subtest axis of the
    ///      subtest × profession content model.
    ///
    /// HAND-AUTHORED (repo convention). Data-only — no schema change, so the model
    /// snapshot is unaffected.
    ///
    /// SAFETY: idempotent. The insert is ON CONFLICT DO NOTHING; the remap is a no-op once
    /// no 'allied_health' rows remain; the subtest backfill only writes rows whose
    /// IncludedSubtestsJson is still empty, and skips (rather than fails on) malformed
    /// EntitlementsJson.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260729091000_UnifyProfessionTaxonomy")]
    public partial class UnifyProfessionTaxonomy : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. The missing reference row ─────────────────────────────────────
            // SignupProfessionCatalog already carries 'other-allied-health' (SeedData +
            // 20260505090000 both reference it), so only Professions needs backfilling.
            // Label mirrors the catalog label; no Materials folder is named after it,
            // which is correct — it is a catch-all, not a discipline folder.
            migrationBuilder.Sql(@"
INSERT INTO ""Professions"" (""Id"", ""Code"", ""Label"", ""Status"", ""SortOrder"")
VALUES ('other-allied-health', 'other-allied-health', 'Other Allied health profession', 'active', 8)
ON CONFLICT (""Id"") DO NOTHING;
");

            // ── 2. Billing taxonomy → canonical id ───────────────────────────────
            migrationBuilder.Sql(@"UPDATE ""BillingPlans"" SET ""Profession"" = 'other-allied-health', ""UpdatedAt"" = now() WHERE ""Profession"" = 'allied_health';");
            migrationBuilder.Sql(@"UPDATE ""BillingPlanVersions"" SET ""Profession"" = 'other-allied-health' WHERE ""Profession"" = 'allied_health';");

            // ── 3. EntitlementsJson.video_library.subtests → IncludedSubtestsJson ─
            // Per-row exception handling: a malformed EntitlementsJson leaves that row
            // untouched instead of aborting the whole migration. Rows that already
            // declare IncludedSubtestsJson are authoritative and are never overwritten.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    r RECORD;
    entitlements jsonb;
    subtests jsonb;
BEGIN
    FOR r IN
        SELECT ""Id"", ""EntitlementsJson""
        FROM ""BillingPlans""
        WHERE ""EntitlementsJson"" IS NOT NULL
          AND ""EntitlementsJson"" <> ''
          AND (""IncludedSubtestsJson"" IS NULL OR ""IncludedSubtestsJson"" IN ('', '[]'))
    LOOP
        BEGIN
            entitlements := r.""EntitlementsJson""::jsonb;
        EXCEPTION WHEN others THEN
            CONTINUE;
        END;

        IF jsonb_typeof(entitlements) <> 'object' THEN CONTINUE; END IF;

        subtests := entitlements -> 'video_library' -> 'subtests';
        IF subtests IS NULL
           OR jsonb_typeof(subtests) <> 'array'
           OR jsonb_array_length(subtests) = 0 THEN
            CONTINUE;
        END IF;

        UPDATE ""BillingPlans"" SET ""IncludedSubtestsJson"" = subtests::text WHERE ""Id"" = r.""Id"";
    END LOOP;
END $$;
");

            migrationBuilder.Sql(@"
DO $$
DECLARE
    r RECORD;
    entitlements jsonb;
    subtests jsonb;
BEGIN
    FOR r IN
        SELECT ""Id"", ""EntitlementsJson""
        FROM ""BillingPlanVersions""
        WHERE ""EntitlementsJson"" IS NOT NULL
          AND ""EntitlementsJson"" <> ''
          AND (""IncludedSubtestsJson"" IS NULL OR ""IncludedSubtestsJson"" IN ('', '[]'))
    LOOP
        BEGIN
            entitlements := r.""EntitlementsJson""::jsonb;
        EXCEPTION WHEN others THEN
            CONTINUE;
        END;

        IF jsonb_typeof(entitlements) <> 'object' THEN CONTINUE; END IF;

        subtests := entitlements -> 'video_library' -> 'subtests';
        IF subtests IS NULL
           OR jsonb_typeof(subtests) <> 'array'
           OR jsonb_array_length(subtests) = 0 THEN
            CONTINUE;
        END IF;

        UPDATE ""BillingPlanVersions"" SET ""IncludedSubtestsJson"" = subtests::text WHERE ""Id"" = r.""Id"";
    END LOOP;
END $$;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Step 3 is NOT reversed: the source EntitlementsJson is left intact by Up(),
            // so nothing is lost, but which rows were backfilled is not recorded and
            // blanking IncludedSubtestsJson wholesale would destroy admin-authored values.
            migrationBuilder.Sql(@"UPDATE ""BillingPlanVersions"" SET ""Profession"" = 'allied_health' WHERE ""Profession"" = 'other-allied-health';");
            migrationBuilder.Sql(@"UPDATE ""BillingPlans"" SET ""Profession"" = 'allied_health', ""UpdatedAt"" = now() WHERE ""Profession"" = 'other-allied-health';");
            migrationBuilder.Sql(@"DELETE FROM ""Professions"" WHERE ""Id"" = 'other-allied-health';");
        }
    }
}
