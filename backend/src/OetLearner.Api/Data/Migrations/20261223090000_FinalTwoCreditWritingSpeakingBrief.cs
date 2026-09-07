using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// FINAL implementation decision 2026-09-06 (Speaking platform &amp; other subtest modifications):
/// one Writing letter / one Speaking card costs 2 AI credits from ANY pool, and a full
/// two-card Speaking exam costs 4. Visible package quantities stay in complete
/// letters/cards; backend grants double to 6/16/30 (Writing/Speaking) and 10/30
/// flexible (Quick Check / Exam Prep Pro).
///
/// <para>
/// 1. Live-tutor eligibility flags: the eleven eligible course/package plans gain
/// <c>SpeakingAddonsEnabled = true</c> (live table + immutable version snapshots).
/// AI-credit ownership alone still grants no tutor access.
/// </para>
///
/// <para>
/// 2. AI package catalogue: the eight AI packs get their FINAL descriptions,
/// candidate-facing benefit-led feature bullets (no Claude/Whisper/backend
/// terminology), doubled credit grants, and GrantCredits zeroed so the JSON
/// grant is the only authority (live table + version snapshots).
/// </para>
///
/// <para>
/// 3. Ledger conservation: remaining dedicated (Writing/Speaking) and Flexible W/S
/// lot balances double so already-purchased complete letters/cards keep their
/// value under the 2-credit rule (e.g. a Starter holder with 3 remaining keeps
/// 3 letters: 3 → 6). Shared pool already costs 2/activity: untouched.
/// Transaction history is preserved; one audit row per adjusted account explains
/// the doubling. Accounts are doubled in lockstep with their live lots.
/// </para>
///
/// <para>⚠ <b>Postgres-only SQL.</b> The test suite uses SQLite via
/// <c>EnsureCreatedAsync()</c> and builds straight from the model,
/// bypassing migrations entirely.</para>
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20261223090000_FinalTwoCreditWritingSpeakingBrief")]
public partial class FinalTwoCreditWritingSpeakingBrief : Migration
{
    private const string TutorEligiblePlanCodes = "('full-condensed-medicine','full-condensed-medicine-tbook','full-nursing','full-nursing-assessment','full-nursing-premium','full-pharmacy','full-physiotherapy','full-allied-health','crash-course','crash-3letters','crash-5letters')";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── 1) Live-tutor eligibility flags ──────────────────────────────
        migrationBuilder.Sql($"""
UPDATE "BillingPlans"
SET "SpeakingAddonsEnabled" = TRUE,
    "UpdatedAt" = now()
WHERE "Code" IN {TutorEligiblePlanCodes};
""");

        migrationBuilder.Sql($"""
UPDATE "BillingPlanVersions" AS v
SET "SpeakingAddonsEnabled" = TRUE
FROM "BillingPlans" AS p
WHERE v."PlanId" = p."Id"
  AND p."Code" IN {TutorEligiblePlanCodes};
""");

        // ── 2) AI package catalogue: copy + grants ───────────────────────
        SetAddOn(migrationBuilder, "pkg_quick_check", 30,
            """{"package_type":"full","flexible_credits":10,"listening_tests":3,"reading_tests":3}""",
            "A targeted readiness package with 5 flexible AI practice attempts that can be used for Writing letters, Speaking cards, or any mix of both, plus 3 Listening and 3 Reading practice exams.",
            """["5 flexible AI practice attempts for Writing or Speaking","3 Listening practice exams","3 Reading practice exams","AI feedback reports for graded Writing or Speaking submissions","30-day validity"]""");
        SetAddOn(migrationBuilder, "pkg_exam_prep_pro", 90,
            """{"package_type":"full","flexible_credits":30,"listening_tests":6,"reading_tests":6}""",
            "A larger exam-preparation package with 15 flexible AI practice attempts that can be used for Writing letters, Speaking cards, or any mix of both, plus 6 Listening and 6 Reading practice exams.",
            """["15 flexible AI practice attempts for Writing or Speaking","6 Listening practice exams","6 Reading practice exams","AI feedback reports for graded Writing or Speaking submissions","90-day validity"]""");
        SetAddOn(migrationBuilder, "pkg_writing_starter", 30,
            """{"package_type":"writing","writing_only_credits":6,"writing_items":3,"listening_tests":0,"reading_tests":0}""",
            "A focused Writing practice package with 3 complete AI-graded OET Writing letters, each with instant, highly specialised feedback and detailed criterion-based comments.",
            """["3 AI-graded Writing letters","Instant specialised feedback on every letter","Detailed per-criterion feedback","30-day validity"]""");
        SetAddOn(migrationBuilder, "pkg_writing_standard", 90,
            """{"package_type":"writing","writing_only_credits":16,"writing_items":8,"listening_tests":0,"reading_tests":0}""",
            "A Writing practice package for candidates who want more repetition, with 8 complete AI-graded OET Writing letters, each with instant, highly specialised feedback and detailed criterion-based comments.",
            """["8 AI-graded Writing letters","Instant specialised feedback on every letter","Detailed per-criterion feedback","90-day validity"]""");
        SetAddOn(migrationBuilder, "pkg_writing_pro", 180,
            """{"package_type":"writing","writing_only_credits":30,"writing_items":15,"listening_tests":0,"reading_tests":0}""",
            "An intensive Writing practice package with 15 complete AI-graded OET Writing letters, each with instant, highly specialised feedback and detailed criterion-based comments.",
            """["15 AI-graded Writing letters","Instant specialised feedback on every letter","Detailed per-criterion feedback","6-month validity"]""");
        SetAddOn(migrationBuilder, "pkg_speaking_starter", 30,
            """{"package_type":"speaking","speaking_only_credits":6,"speaking_items":3,"listening_tests":0,"reading_tests":0}""",
            "A focused Speaking practice package with 3 complete AI-graded OET Speaking cards, each with instant, highly specialised feedback and detailed transcript-based comments aligned with OET Speaking criteria.",
            """["3 AI-graded Speaking cards","Instant specialised feedback on every card","Detailed transcript-based feedback aligned with OET Speaking criteria","30-day validity"]""");
        SetAddOn(migrationBuilder, "pkg_speaking_standard", 90,
            """{"package_type":"speaking","speaking_only_credits":16,"speaking_items":8,"listening_tests":0,"reading_tests":0}""",
            "A Speaking practice package for candidates who want more role-play repetition, with 8 complete AI-graded OET Speaking cards, each with instant, highly specialised feedback and detailed transcript-based comments aligned with OET Speaking criteria.",
            """["8 AI-graded Speaking cards","Instant specialised feedback on every card","Detailed transcript-based feedback aligned with OET Speaking criteria","90-day validity"]""");
        SetAddOn(migrationBuilder, "pkg_speaking_pro", 180,
            """{"package_type":"speaking","speaking_only_credits":30,"speaking_items":15,"listening_tests":0,"reading_tests":0}""",
            "An intensive Speaking practice package with 15 complete AI-graded OET Speaking cards, each with instant, highly specialised feedback and detailed transcript-based comments aligned with OET Speaking criteria.",
            """["15 AI-graded Speaking cards","Instant specialised feedback on every card","Detailed transcript-based feedback aligned with OET Speaking criteria","6-month validity"]""");

        // ── 3) Ledger conservation: double remaining dedicated + flexible ──
        // Audit row first (deltas equal the pre-doubling balances being added).
        migrationBuilder.Sql("""
INSERT INTO "AiPackageCreditTransactions" (
    "Id", "UserId", "AccountId", "StripeSessionId", "PackageId", "PackageType",
    "SharedCreditsDelta", "FlexibleCreditsDelta", "WritingOnlyCreditsDelta", "SpeakingOnlyCreditsDelta",
    "ListeningTestsDelta", "ReadingTestsDelta", "MockExamsDelta",
    "Reason", "ReferenceId", "JobId", "Description", "ExpiresAt", "CreatedAt", "CreatedByAdminId")
SELECT 'aipkg-tx-' || replace(gen_random_uuid()::text, '-', ''),
       a."UserId", a."Id", NULL, NULL, 'full',
       0, a."FlexibleCredits", a."WritingOnlyCredits", a."SpeakingOnlyCredits",
       0, 0, 0,
       4, 'migration:final-two-credit-brief:' || a."Id", NULL,
       'FINAL 2026-09-06: remaining Writing/Speaking/Flexible balances doubled so purchased complete letters/cards keep their value at 2 AI credits each.',
       NULL, now(), NULL
FROM "AiPackageCreditAccounts" AS a
WHERE a."FlexibleCredits" > 0 OR a."WritingOnlyCredits" > 0 OR a."SpeakingOnlyCredits" > 0;
""");

        migrationBuilder.Sql("""
UPDATE "AiPackageCreditLots"
SET "WritingOnlyCredits" = "WritingOnlyCredits" * 2,
    "SpeakingOnlyCredits" = "SpeakingOnlyCredits" * 2,
    "FlexibleCredits" = "FlexibleCredits" * 2
WHERE "Expired" = FALSE
  AND ("WritingOnlyCredits" > 0 OR "SpeakingOnlyCredits" > 0 OR "FlexibleCredits" > 0);
""");

        migrationBuilder.Sql("""
UPDATE "AiPackageCreditAccounts"
SET "WritingOnlyCredits" = "WritingOnlyCredits" * 2,
    "SpeakingOnlyCredits" = "SpeakingOnlyCredits" * 2,
    "FlexibleCredits" = "FlexibleCredits" * 2,
    "UpdatedAt" = now()
WHERE "FlexibleCredits" > 0 OR "WritingOnlyCredits" > 0 OR "SpeakingOnlyCredits" > 0;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Catalogue copy/grants and tutor flags are canonical and must not be
        // reversed; ledger doubling cannot be safely halved (odd balances).
    }

    private static void SetAddOn(
        MigrationBuilder migrationBuilder,
        string code,
        int durationDays,
        string grantJson,
        string description,
        string featuresJson)
    {
        var grant = grantJson.Replace("'", "''", StringComparison.Ordinal);
        var desc = description.Replace("'", "''", StringComparison.Ordinal);
        var features = featuresJson.Replace("'", "''", StringComparison.Ordinal);
        migrationBuilder.Sql($"""
UPDATE "BillingAddOns"
SET "GrantCredits" = 0,
    "DurationDays" = {durationDays},
    "GrantEntitlementsJson" = '{grant}',
    "Description" = '{desc}',
    "AiFeaturesJson" = '{features}',
    "UpdatedAt" = now()
WHERE "Code" = '{code}';

UPDATE "BillingAddOnVersions" AS v
SET "GrantCredits" = 0,
    "DurationDays" = {durationDays},
    "GrantEntitlementsJson" = '{grant}',
    "Description" = '{desc}',
    "AiFeaturesJson" = '{features}'
FROM "BillingAddOns" AS a
WHERE v."AddOnId" = a."Id"
  AND a."Code" = '{code}'
  AND (
      v."Status" = 1
      OR v."Id" = a."ActiveVersionId"
      OR v."Id" = a."LatestVersionId"
  );
""");
    }
}
