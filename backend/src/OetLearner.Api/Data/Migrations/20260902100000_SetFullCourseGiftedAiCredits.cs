using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Production JSON seeder is disabled, so BundledAiCredits=5 in
/// oet-2026-catalog.json never reached live BillingPlans for Nursing,
/// Pharmacy, Crash Course and the other Full Courses. Admin Access then hid
/// the "includes 5 gifted AI credits" line and GrantPackage skipped the gift.
/// This writes 5 onto the live plan + active version rows and restores the
/// storefront ticks. Existing purchases are not backfilled.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260902100000_SetFullCourseGiftedAiCredits")]
public partial class SetFullCourseGiftedAiCredits : Migration
{
    private const string FullCourseCodes = """
        'full-condensed-medicine',
        'full-condensed-medicine-tbook',
        'full-nursing',
        'full-nursing-assessment',
        'full-nursing-premium',
        'full-pharmacy',
        'full-physiotherapy',
        'full-allied-health',
        'crash-course',
        'crash-3letters',
        'crash-5letters'
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
UPDATE "BillingPlans"
SET "BundledAiCredits" = 5,
    "UpdatedAt" = now()
WHERE "Code" IN ({FullCourseCodes});

UPDATE "BillingPlanVersions" AS v
SET "BundledAiCredits" = 5
FROM "BillingPlans" AS p
WHERE v."PlanId" = p."Id"
  AND p."Code" IN ({FullCourseCodes})
  AND (
      v."Status" = 1
      OR v."Id" = p."ActiveVersionId"
      OR v."Id" = p."LatestVersionId"
  );
""");

        SetFeatures(migrationBuilder, "full-nursing",
            """["Full Nursing OET preparation across Listening, Reading, Writing and Speaking","Recall-based Nursing Writing letters and model answers","Recall-based Nursing Speaking cards with expected ideas","Listening and Reading practice library","5 AI practice credits","Continuous Q&A support during the access period"]""");
        SetFeatures(migrationBuilder, "full-nursing-assessment",
            """["Everything included in the Full Nursing OET Course","5 Writing letter assessments","Detailed correction and voice-note feedback via WhatsApp","5 AI credits for instant practice feedback"]""");
        SetFeatures(migrationBuilder, "full-nursing-premium",
            """["Everything included in the Nursing Course + Assessment Package","5 AI practice credits","Basic English Course - Preparation for OET","11+ hours of foundation English training","Grammar, vocabulary and sentence-formation support","Course booklet for the Basic English module"]""");
        SetFeatures(migrationBuilder, "full-pharmacy",
            """["Full Pharmacy OET preparation across Listening, Reading, Writing and Speaking","Pharmacy-specific Writing examples and model answers","Pharmacy Speaking cards with expected ideas and useful language","Recall-based practice resources","5 AI practice credits","Continuous Q&A support during the access period"]""");
        SetFeatures(migrationBuilder, "crash-course",
            """["Condensed recorded preparation across Listening, Reading, Writing and Speaking","High-yield exam strategies and practical techniques","Recall-based guidance for recent exam trends","Selected study materials and Listening recalls","5 AI practice credits"]""");
        SetFeatures(migrationBuilder, "crash-3letters",
            """["Everything included in the Full Crash Course","Assessment of 3 Writing letters","Estimated score, detailed correction and voice-note feedback","Letters may be candidate-chosen or recall-recommended","5 AI practice credits"]""");
        SetFeatures(migrationBuilder, "crash-5letters",
            """["Everything included in the Full Crash Course","Assessment of 5 Writing letters","Estimated score, detailed correction and voice-note feedback","Letters may be candidate-chosen or recall-recommended","5 AI practice credits"]""");
        SetFeatures(migrationBuilder, "full-condensed-medicine-tbook",
            """["Everything included in the Full Condensed Recorded Medicine Course","5 AI practice credits","TutorBook as a personalised watermarked PDF","8 full 2026 recall-based OET exams covering Listening, Reading, Writing and Speaking","The main exam ideas and recall themes from 2026 across all four sub-tests","New Reading dictionary including the vocabulary from the 2026 Reading recalls","Model answers for Writing and relevant practice tasks","Answer rationales and justifications to help candidates understand why each answer is correct","Listening scripts for recall-based Listening practice","Listening recall vocabulary and repeated words from recent exams","Already-included add-on exams with more recent recall-based practice","Private update channel access for new book updates and recall additions"]""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Gifted AI credits are canonical catalogue policy and must not be reversed.
    }

    private static void SetFeatures(MigrationBuilder migrationBuilder, string code, string comparisonFeaturesJson)
    {
        var escaped = comparisonFeaturesJson.Replace("'", "''", StringComparison.Ordinal);
        migrationBuilder.Sql($"""
UPDATE "ContentPackages" AS cp
SET "ComparisonFeaturesJson" = '{escaped}',
    "UpdatedAt" = now()
FROM "BillingPlans" AS p
WHERE cp."BillingPlanId" = p."Id"
  AND p."Code" = '{code}';
""");
    }
}
