using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Converges the production catalogue (where the JSON seeder is disabled) on
/// the owner-approved 2026 website descriptions and package semantics.
/// Prices, Pharmacy publication state, TutorBook fulfilment, historical
/// purchases and unrelated catalogue rows are intentionally left untouched.
/// The pre-deploy production aggregate found zero purchases/subscriptions for
/// the changed Writing, unlimited L/R, Mastery, Physiotherapy and Allied rows,
/// so no speculative learner-balance backfill is performed.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260820090000_AlignWebsiteCoursePackageDescriptions")]
public partial class AlignWebsiteCoursePackageDescriptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        UpdatePlan(migrationBuilder,
            """full-condensed-medicine""",
            """Full Condensed Recorded OET Course - Medicine""",
            """A complete, condensed and exam-focused recorded Medicine course covering Listening, Reading, Writing and Speaking. It is designed for candidates who want a structured route through all four OET sub-tests with the freedom to repeat lessons throughout the access period.""",
            """["160+ Listening exams, including practice beyond the usual Jahshan and Benchmark resources","100+ Reading exams with answer keys and rationales","90+ Writing tasks covering the main OET letter types","100+ Speaking cards across different scenarios and card types","Recent recall updates plus older recalls from 2023 onwards","5 Writing letter assessments with personalised correction","1 private Speaking session","5 AI practice credits","Continuous Q\u0026A support during the access period"]""");

        UpdatePlan(migrationBuilder,
            """full-physiotherapy""",
            """Full Physiotherapy OET Course""",
            """A complete, condensed and exam-focused recorded Physiotherapy course covering Listening, Reading, Writing and Speaking. It is designed for candidates who want a structured route through all four OET sub-tests with the freedom to repeat lessons throughout the access period.""",
            """["160+ Listening exams, including practice beyond the usual Jahshan and Benchmark resources","100+ Reading exams with answer keys and rationales","90+ Writing tasks covering the main OET letter types","100+ Speaking cards across different scenarios and card types","Recent recall updates plus older recalls from 2023 onwards","5 Writing letter assessments with personalised correction","1 private Speaking session","5 AI practice credits","Continuous Q\u0026A support during the access period"]""", writingAssessments: 5, speakingSessions: 1, aiCredits: 5);

        UpdatePlan(migrationBuilder,
            """full-allied-health""",
            """Full Allied Health Profession OET Course""",
            """A complete, condensed and exam-focused recorded Allied Health Profession course covering Listening, Reading, Writing and Speaking. It is designed for candidates who want a structured route through all four OET sub-tests with the freedom to repeat lessons throughout the access period.""",
            """["160+ Listening exams, including practice beyond the usual Jahshan and Benchmark resources","100+ Reading exams with answer keys and rationales","90+ Writing tasks covering the main OET letter types","100+ Speaking cards across different scenarios and card types","Recent recall updates plus older recalls from 2023 onwards","5 Writing letter assessments with personalised correction","1 private Speaking session","5 AI practice credits","Continuous Q\u0026A support during the access period"]""", writingAssessments: 5, speakingSessions: 1, aiCredits: 5);

        UpdatePlan(migrationBuilder,
            """full-condensed-medicine-tbook""",
            """Full Condensed Recorded Course + TutorBook""",
            """The flagship recorded course bundled with TutorBook. TutorBook is a 2026 recall-based OET preparation book built around 8 full exams covering Listening, Reading, Writing and Speaking. It brings together the main 2026 exam ideas and recall themes, with complete model answers, rationales, Listening scripts, Reading vocabulary support and additional recent recall-based exams already included as add-ons.""",
            """["Everything included in the Full Condensed Recorded Medicine Course","TutorBook as a personalised watermarked PDF","8 full 2026 recall-based OET exams covering Listening, Reading, Writing and Speaking","The main exam ideas and recall themes from 2026 across all four sub-tests","New Reading dictionary including the vocabulary from the 2026 Reading recalls","Model answers for Writing and relevant practice tasks","Answer rationales and justifications to help candidates understand why each answer is correct","Listening scripts for recall-based Listening practice","Listening recall vocabulary and repeated words from recent exams","Already-included add-on exams with more recent recall-based practice","Private update channel access for new book updates and recall additions"]""");

        UpdatePlan(migrationBuilder,
            """full-nursing""",
            """Full Nursing OET Course""",
            """A profession-specific OET course for nurses covering Listening, Reading, Writing and Speaking with nursing-focused examples, letters and role-play scenarios.""",
            """["Full Nursing OET preparation across Listening, Reading, Writing and Speaking","Recall-based Nursing Writing letters and model answers","Recall-based Nursing Speaking cards with expected ideas","Listening and Reading practice library","Continuous Q\u0026A support during the access period"]""");

        UpdatePlan(migrationBuilder,
            """full-nursing-assessment""",
            """Nursing Course + Assessment Package""",
            """The full Nursing OET course bundled with personalised Writing assessment support and AI practice credits. This option is designed for nurses who want structured learning plus direct feedback on their letters.""",
            """["Everything included in the Full Nursing OET Course","5 Writing letter assessments","Detailed correction and voice-note feedback via WhatsApp","5 AI credits for instant practice feedback"]""");

        UpdatePlan(migrationBuilder,
            """full-nursing-premium""",
            """Nursing Premium Bundle""",
            """The most complete Nursing package, combining the full Nursing OET course, Writing assessment support, AI practice credits and the Basic English foundation course for candidates who want to strengthen their English before or alongside OET preparation.""",
            """["Everything included in the Nursing Course + Assessment Package","Basic English Course - Preparation for OET","11+ hours of foundation English training","Grammar, vocabulary and sentence-formation support","Course booklet for the Basic English module"]""");

        UpdatePlan(migrationBuilder,
            """full-pharmacy""",
            """Full Pharmacy OET Course""",
            """A profession-specific OET preparation course for pharmacists covering all four sub-tests with pharmacy-focused Writing and Speaking content. The course includes pharmacy scenarios such as complaint handling, dosage issues, drug safety, expiry-date discrepancies, interactions and counselling.""",
            """["Full Pharmacy OET preparation across Listening, Reading, Writing and Speaking","Pharmacy-specific Writing examples and model answers","Pharmacy Speaking cards with expected ideas and useful language","Recall-based practice resources","Continuous Q\u0026A support during the access period"]""");

        UpdatePlan(migrationBuilder,
            """basic-english""",
            """Basic English Course - Preparation for OET""",
            """A preparatory English course for candidates at Beginner, A1, A2 or B1 level who need to build a stronger English base before intensive OET preparation. The course focuses on essential grammar, healthcare vocabulary, sentence formation, listening foundations and a practical study plan.""",
            """["Fully recorded preparatory English course","Essential grammar explained from the basics and linked to OET production","Medical and healthcare vocabulary foundation","Sentence formation for OET-style communication","Conversation and listening foundations for healthcare contexts","Simplified course booklet","Full study plan"]""");

        UpdatePlan(migrationBuilder,
            """crash-course""",
            """Full Crash Course - General OET""",
            """A condensed, high-impact OET course for candidates with limited time before the exam. It covers the four sub-tests in an exam-oriented format with high-yield strategies and recall-based guidance.""",
            """["Condensed recorded preparation across Listening, Reading, Writing and Speaking","High-yield exam strategies and practical techniques","Recall-based guidance for recent exam trends","Selected study materials and Listening recalls"]""");

        UpdatePlan(migrationBuilder,
            """crash-3letters""",
            """Full Crash Course + 3 Writing Assessments""",
            """The Full Crash Course bundled with assessment of 3 Writing letters. It combines condensed preparation across the exam with personalised Writing correction.""",
            """["Everything included in the Full Crash Course","Assessment of 3 Writing letters","Estimated score, detailed correction and voice-note feedback","Letters may be candidate-chosen or recall-recommended"]""");

        UpdatePlan(migrationBuilder,
            """crash-5letters""",
            """Full Crash Course + 5 Writing Assessments""",
            """The Full Crash Course bundled with assessment of 5 Writing letters. It is the recommended crash-course bundle for candidates who want more personalised Writing feedback while studying the four sub-tests.""",
            """["Everything included in the Full Crash Course","Assessment of 5 Writing letters","Estimated score, detailed correction and voice-note feedback","Letters may be candidate-chosen or recall-recommended"]""");

        UpdatePlan(migrationBuilder,
            """writing-crash""",
            """Recorded Writing Crash Course""",
            """A standalone recorded Writing course covering the OET Writing sub-test from A-Z. It explains task analysis, purpose, audience, case-note relevance, introductions, body paragraphs, closing paragraphs, all major letter types, assessment criteria and profession-specific examples.""",
            """["Full recorded Writing explanation from A-Z","Task, purpose, audience and case-note relevance","Referral, discharge, transfer, update, complaint and profession-specific letters","Grammar, sentence structure, clarity and conciseness","Latest OET Writing assessment criteria","Profession-specific examples and recall-based ideas"]""");

        UpdatePlan(migrationBuilder,
            """writing-crash-2""",
            """Writing Crash Course + 2 Letter Assessments""",
            """The Recorded Writing Crash Course bundled with 2 Writing letter assessments. It gives candidates the full Writing explanation plus a small amount of personalised feedback.""",
            """["Everything included in the Recorded Writing Crash Course","2 Writing letter assessments","Estimated score, detailed correction and voice-note feedback"]""");

        UpdatePlan(migrationBuilder,
            """writing-crash-3""",
            """Writing Crash Course + 3 Letter Assessments""",
            """The Recorded Writing Crash Course bundled with 3 Writing letter assessments. It combines the full Writing explanation with personalised assessment support.""",
            """["Everything included in the Recorded Writing Crash Course","3 Writing letter assessments","Estimated score, detailed correction and voice-note feedback"]""");

        UpdatePlan(migrationBuilder,
            """writing-crash-5""",
            """Writing Crash Course + 5 Letter Assessments""",
            """The Recorded Writing Crash Course bundled with 5 Writing letter assessments. It is the recommended Writing bundle for candidates who want structured lessons and a strong amount of personalised correction.""",
            """["Everything included in the Recorded Writing Crash Course","5 Writing letter assessments","Estimated score, detailed correction and voice-note feedback"]""");

        UpdatePlan(migrationBuilder,
            """writing-crash-7""",
            """Writing Crash Course + 7 Letter Assessments""",
            """The Recorded Writing Crash Course bundled with 7 Writing letter assessments. It is designed for candidates who want deeper Writing practice and repeated feedback.""",
            """["Everything included in the Recorded Writing Crash Course","7 Writing letter assessments","Estimated score, detailed correction and voice-note feedback"]""");

        UpdatePlan(migrationBuilder,
            """writing-crash-10""",
            """Writing Crash Course + 10 Letter Assessments""",
            """The Recorded Writing Crash Course bundled with 10 Writing letter assessments. This is the heaviest Writing-focused option and is designed for candidates who want maximum correction practice.""",
            """["Everything included in the Recorded Writing Crash Course","10 Writing letter assessments","Estimated score, detailed correction and voice-note feedback"]""");

        UpdatePlan(migrationBuilder,
            """speaking-crash""",
            """Recorded Speaking Crash Course""",
            """A standalone recorded Speaking course covering OET Speaking scenarios, card types, recent recall themes and exam performance for Medicine, Nursing and Pharmacy candidates.""",
            """["Complete explanation of the Speaking sub-test and role-play structure","All major card types covered in detail","Recall-based focus on repeated card types","Opening, information gathering, addressing concerns and safe closing","Handling anxious, angry, confused, reluctant or non-compliant patients","Empathy, reassurance, signposting and checking understanding"]""");

        UpdatePlan(migrationBuilder,
            """speaking-1session""",
            """1 Private Speaking Assessment Session""",
            """A one-to-one live Speaking practice session using different OET cards with detailed performance feedback. It can be used as a top-up alongside any course or as a standalone speaking assessment session.""",
            """["1 live 1:1 Speaking session","Multiple cards covered","Detailed performance feedback"]""");

        UpdatePlan(migrationBuilder,
            """speaking-2sessions""",
            """2 Private Speaking Assessment Sessions""",
            """Two one-to-one live Speaking assessment sessions using different cards with detailed feedback after each session.""",
            """["2 live 1:1 Speaking sessions","Different cards in each session","Detailed performance feedback for each session"]""");

        UpdatePlan(migrationBuilder,
            """double-special""",
            """Double Special Package - Writing + Speaking""",
            """A combined productive-skills package including the full recorded Writing course and the full recorded Speaking course. It is suitable for candidates who are confident in Listening and Reading and want to focus on Writing and Speaking.""",
            """["Full recorded Writing course with the latest criteria","Full recorded Speaking course from A-Z","Model letters, Writing rules, Speaking cards and useful phrases","Materials for focused productive-skills preparation"]""");

        UpdatePlan(migrationBuilder,
            """mega-special""",
            """Mega Special Package""",
            """A flagship Writing and Speaking combo package that includes the full recorded Writing course, full recorded Speaking course, one private Speaking session and 5 Writing letter assessments.""",
            """["Full recorded Writing sessions with the latest criteria","Full recorded Speaking course from A-Z","1 private Speaking session","5 Writing letter assessments","18+ hours of focused Writing and Speaking preparation"]""");

        UpdatePlan(migrationBuilder,
            """tutor-book""",
            """TutorBook - First Edition 2026""",
            """TutorBook is a 2026 recall-based OET preparation book built around 8 full exams covering Listening, Reading, Writing and Speaking. It brings together the main 2026 exam ideas and recall themes, with complete model answers, rationales, Listening scripts, Reading vocabulary support and additional recent recall-based exams already included as add-ons.""",
            """["8 full 2026 recall-based OET exams covering Listening, Reading, Writing and Speaking","The main exam ideas and recall themes from 2026 across all four sub-tests","New Reading dictionary including the vocabulary from the 2026 Reading recalls","Model answers for Writing and relevant practice tasks","Answer rationales and justifications to help candidates understand why each answer is correct","Listening scripts for recall-based Listening practice","Listening recall vocabulary and repeated words from recent exams","Already-included add-on exams with more recent recall-based practice","Private update channel access for new book updates and recall additions"]""");

        UpdateManualAddOn(migrationBuilder,
            """addon-3-letters""",
            """3 Writing Letter Assessments - Add-on""",
            """A stackable Writing assessment add-on that gives candidates personalised feedback on 3 OET letters. It is intended for candidates already enrolled in an eligible course or package.""");

        UpdateManualAddOn(migrationBuilder,
            """addon-5-letters""",
            """5 Writing Letter Assessments - Add-on""",
            """A stackable Writing assessment add-on that gives candidates personalised feedback on 5 OET letters. It is intended for candidates already enrolled in an eligible course or package.""");

        UpdateManualAddOn(migrationBuilder,
            """addon-7-letters""",
            """7 Writing Letter Assessments - Add-on""",
            """A stackable Writing assessment add-on that gives candidates personalised feedback on 7 OET letters using the same assessment format as the smaller Writing packages.""");

        UpdateManualAddOn(migrationBuilder,
            """addon-10-letters""",
            """10 Writing Letter Assessments - Add-on""",
            """A stackable Writing assessment add-on that gives candidates personalised feedback on 10 OET letters. It provides the heaviest Writing correction option for candidates who want extensive practice.""");

        UpdateManualAddOn(migrationBuilder,
            """addon-speaking-1session""",
            """1 Private Speaking Assessment Session — Add-on""",
            """A one-to-one live Speaking practice session using different OET cards with detailed performance feedback. It can be used as a top-up alongside any course or as a standalone speaking assessment session.""");

        UpdateManualAddOn(migrationBuilder,
            """addon-speaking-2sessions""",
            """2 Private Speaking Assessment Sessions — Add-on""",
            """Two one-to-one live Speaking assessment sessions using different cards with detailed feedback after each session.""");

        UpdateManualAddOn(migrationBuilder,
            """tutor-book-addon""",
            """TutorBook - Add-on for Enrolled Students""",
            """A TutorBook add-on available to candidates with an eligible active enrolment. TutorBook is a 2026 recall-based OET preparation book built around 8 full exams covering Listening, Reading, Writing and Speaking. It brings together the main 2026 exam ideas and recall themes, with complete model answers, rationales, Listening scripts, Reading vocabulary support and additional recent recall-based exams already included as add-ons.""");

        UpdateAiPackage(migrationBuilder,
            """pkg_quick_check""",
            """Quick Check""",
            """A targeted one-off readiness package for candidates who want instant AI feedback on Writing or Speaking together with a small Listening and Reading practice allowance. This package includes 5 flexible AI grading credits for Writing letters or Speaking cards, plus 3 Listening exams and 3 Reading exams for focused practice.""",
            5,
            """{"package_type":"full","flexible_credits":5,"listening_tests":3,"reading_tests":3}""",
            """full""",
            """["5 flexible AI grading credits for Writing or Speaking","3 Listening practice exams","3 Reading practice exams","AI feedback reports for graded Writing or Speaking submissions","30-day validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_exam_prep_pro""",
            """Exam Prep Pro""",
            """A larger one-off exam preparation package for candidates who need repeated AI grading and more Listening and Reading practice before the exam. It includes 15 flexible AI grading credits for Writing letters or Speaking cards, plus 6 Listening exams and 6 Reading exams.""",
            15,
            """{"package_type":"full","flexible_credits":15,"listening_tests":6,"reading_tests":6}""",
            """full""",
            """["15 flexible AI grading credits for Writing or Speaking","6 Listening practice exams","6 Reading practice exams","AI feedback reports for graded Writing or Speaking submissions","90-day validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_oet_mastery""",
            """OET Mastery""",
            """The highest AI practice package for candidates who want unlimited assessment during the access period. It includes unlimited AI assessment for Writing and Speaking, unlimited Listening and Reading practice, detailed AI feedback reports and priority queue access.""",
            0,
            """{"package_type":"full","listening_tests":null,"reading_tests":null,"unlimited_grading":true,"priority_queue":true}""",
            """full""",
            """["Unlimited AI assessment for Writing letters and Speaking cards during the access period","Unlimited Listening practice","Unlimited Reading practice","Detailed AI feedback reports","Priority grading queue","6-month validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_mock_1""",
            """1 Full Mock""",
            """One complete OET mock exam covering all four sub-tests. Writing and Speaking are AI-graded, while Listening and Reading are auto-marked using answer-key marking. Mock exam allowances are separate from AI grading credits.""",
            0,
            """{"package_type":"mock","mock_exams":1,"listening_tests":0,"reading_tests":0}""",
            """mock""",
            """["1 full mock exam covering Listening, Reading, Writing and Speaking","Writing and Speaking AI-graded","Listening and Reading auto-marked","Mock allowance separate from AI credits","6-month validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_mock_3""",
            """3 Full Mocks""",
            """Three complete OET mock exams covering all four sub-tests. Writing and Speaking are AI-graded, while Listening and Reading are auto-marked using answer-key marking. Mock exam allowances are separate from AI grading credits.""",
            0,
            """{"package_type":"mock","mock_exams":3,"listening_tests":0,"reading_tests":0}""",
            """mock""",
            """["3 full mock exams covering Listening, Reading, Writing and Speaking","Writing and Speaking AI-graded","Listening and Reading auto-marked","Mock allowance separate from AI credits","6-month validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_mock_5""",
            """5 Full Mocks""",
            """Five complete OET mock exams covering all four sub-tests. Writing and Speaking are AI-graded, while Listening and Reading are auto-marked using answer-key marking. Mock exam allowances are separate from AI grading credits.""",
            0,
            """{"package_type":"mock","mock_exams":5,"listening_tests":0,"reading_tests":0}""",
            """mock""",
            """["5 full mock exams covering Listening, Reading, Writing and Speaking","Writing and Speaking AI-graded","Listening and Reading auto-marked","Mock allowance separate from AI credits","6-month validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_listening_starter""",
            """Listening Starter""",
            """A focused starter package for candidates who want a small set of Listening practice exams. It includes 3 Listening exams with deterministic answer-key marking.""",
            0,
            """{"package_type":"listening","listening_tests":3,"reading_tests":0}""",
            """listening""",
            """["3 Listening practice exams","Deterministic answer-key marking","Always free to grade - no AI credits used","30-day validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_listening_standard""",
            """Listening Standard""",
            """A standard Listening practice package for candidates who want more focused Listening practice. It includes 6 Listening exams with deterministic answer-key marking.""",
            0,
            """{"package_type":"listening","listening_tests":6,"reading_tests":0}""",
            """listening""",
            """["6 Listening practice exams","Deterministic answer-key marking","Always free to grade - no AI credits used","90-day validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_listening_pro""",
            """Listening Pro""",
            """An unlimited Listening practice package for candidates who want open Listening practice throughout the access period. Listening is auto-marked with deterministic answer-key marking and does not use AI credits.""",
            0,
            """{"package_type":"listening","listening_tests":null,"reading_tests":0}""",
            """listening""",
            """["Unlimited Listening practice","Deterministic answer-key marking","Always free to grade - no AI credits used","6-month validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_reading_starter""",
            """Reading Starter""",
            """A focused starter package for candidates who want a small set of Reading practice exams. It includes 3 Reading exams with deterministic answer-key marking.""",
            0,
            """{"package_type":"reading","listening_tests":0,"reading_tests":3}""",
            """reading""",
            """["3 Reading practice exams","Deterministic answer-key marking","Always free to grade - no AI credits used","30-day validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_reading_standard""",
            """Reading Standard""",
            """A standard Reading practice package for candidates who want more focused Reading practice. It includes 6 Reading exams with deterministic answer-key marking.""",
            0,
            """{"package_type":"reading","listening_tests":0,"reading_tests":6}""",
            """reading""",
            """["6 Reading practice exams","Deterministic answer-key marking","Always free to grade - no AI credits used","90-day validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_reading_pro""",
            """Reading Pro""",
            """An unlimited Reading practice package for candidates who want open Reading practice throughout the access period. Reading is auto-marked with deterministic answer-key marking and does not use AI credits.""",
            0,
            """{"package_type":"reading","listening_tests":0,"reading_tests":null}""",
            """reading""",
            """["Unlimited Reading practice","Deterministic answer-key marking","Always free to grade - no AI credits used","6-month validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_writing_starter""",
            """Writing Starter""",
            """A starter Writing AI grading package sized for focused practice. It includes 3 AI-graded Writing letters with instant Claude feedback and detailed criterion-based comments.""",
            3,
            """{"package_type":"writing","writing_only_credits":6,"writing_items":3,"listening_tests":0,"reading_tests":0}""",
            """writing""",
            """["3 AI-graded Writing letters","Instant Claude feedback on every letter","Detailed per-criterion feedback","30-day validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_writing_standard""",
            """Writing Standard""",
            """A standard Writing AI grading package for candidates who want more letter practice. It includes 8 AI-graded Writing letters with instant Claude feedback and detailed criterion-based comments.""",
            8,
            """{"package_type":"writing","writing_only_credits":16,"writing_items":8,"listening_tests":0,"reading_tests":0}""",
            """writing""",
            """["8 AI-graded Writing letters","Instant Claude feedback on every letter","Detailed per-criterion feedback","90-day validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_writing_pro""",
            """Writing Pro""",
            """A larger Writing AI grading package for candidates who want intensive letter practice. It includes 15 AI-graded Writing letters with instant Claude feedback and detailed criterion-based comments.""",
            15,
            """{"package_type":"writing","writing_only_credits":30,"writing_items":15,"listening_tests":0,"reading_tests":0}""",
            """writing""",
            """["15 AI-graded Writing letters","Instant Claude feedback on every letter","Detailed per-criterion feedback","6-month validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_speaking_starter""",
            """Speaking Starter""",
            """A starter Speaking AI grading package sized for focused practice. It includes 3 AI-graded Speaking cards using Whisper transcription and Claude assessment with rule-cited transcript markers.""",
            3,
            """{"package_type":"speaking","speaking_only_credits":3,"speaking_items":3,"listening_tests":0,"reading_tests":0}""",
            """speaking""",
            """["3 AI-graded Speaking cards","Whisper transcription plus Claude assessment","Rule-cited transcript markers","30-day validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_speaking_standard""",
            """Speaking Standard""",
            """A standard Speaking AI grading package for candidates who want more role-play practice. It includes 8 AI-graded Speaking cards using Whisper transcription and Claude assessment with rule-cited transcript markers.""",
            8,
            """{"package_type":"speaking","speaking_only_credits":8,"speaking_items":8,"listening_tests":0,"reading_tests":0}""",
            """speaking""",
            """["8 AI-graded Speaking cards","Whisper transcription plus Claude assessment","Rule-cited transcript markers","90-day validity"]""");

        UpdateAiPackage(migrationBuilder,
            """pkg_speaking_pro""",
            """Speaking Pro""",
            """A larger Speaking AI grading package for candidates who want intensive role-play practice. It includes 15 AI-graded Speaking cards using Whisper transcription and Claude assessment with rule-cited transcript markers.""",
            15,
            """{"package_type":"speaking","speaking_only_credits":15,"speaking_items":15,"listening_tests":0,"reading_tests":0}""",
            """speaking""",
            """["15 AI-graded Speaking cards","Whisper transcription plus Claude assessment","Rule-cited transcript markers","6-month validity"]""");

        // These obsolete rows were superseded by the canonical 3/8/15
        // Speaking packages. Archive only; retained purchase history is not
        // deleted and a future manifest reseed cannot reactivate them.
        migrationBuilder.Sql("""
UPDATE "BillingAddOns"
SET "Status" = 3,
    "UpdatedAt" = now()
WHERE "Code" IN (
    'pkg_speaking_ai_starter',
    'pkg_speaking_ai_standard',
    'pkg_speaking_ai_pro'
);

UPDATE "BillingAddOnVersions"
SET "Status" = 3
WHERE "Code" IN (
    'pkg_speaking_ai_starter',
    'pkg_speaking_ai_standard',
    'pkg_speaking_ai_pro'
);

UPDATE "ContentPackages" AS cp
SET "Status" = 6,
    "PublishedAt" = NULL,
    "UpdatedAt" = now()
FROM "BillingAddOns" AS a
WHERE cp."BillingAddOnId" = a."Id"
  AND a."Code" IN (
      'pkg_speaking_ai_starter',
      'pkg_speaking_ai_standard',
      'pkg_speaking_ai_pro'
  );
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Copy/content migrations are deliberately not reversed: older copy
        // is no longer canonical and restoring it could misstate entitlements.
    }

    private static void UpdatePlan(
        MigrationBuilder migrationBuilder,
        string code,
        string name,
        string description,
        string comparisonFeaturesJson,
        int? writingAssessments = null,
        int? speakingSessions = null,
        int? aiCredits = null)
    {
        var codes = PlanCodes(code);

        migrationBuilder.Sql($$"""
UPDATE "BillingPlans"
SET "Name" = '{{Sql(name)}}',
    "Description" = '{{Sql(description)}}',
    "BundledWritingAssessments" = COALESCE({{SqlInt(writingAssessments)}}, "BundledWritingAssessments"),
    "BundledSpeakingSessions" = COALESCE({{SqlInt(speakingSessions)}}, "BundledSpeakingSessions"),
    "BundledAiCredits" = COALESCE({{SqlInt(aiCredits)}}, "BundledAiCredits"),
    "UpdatedAt" = now()
WHERE "Code" IN ({{codes}});

UPDATE "BillingPlanVersions" AS v
SET "Name" = '{{Sql(name)}}',
    "Description" = '{{Sql(description)}}',
    "BundledWritingAssessments" = COALESCE({{SqlInt(writingAssessments)}}, v."BundledWritingAssessments"),
    "BundledSpeakingSessions" = COALESCE({{SqlInt(speakingSessions)}}, v."BundledSpeakingSessions"),
    "BundledAiCredits" = COALESCE({{SqlInt(aiCredits)}}, v."BundledAiCredits")
FROM "BillingPlans" AS p
WHERE v."PlanId" = p."Id"
  AND p."Code" IN ({{codes}})
  AND (
      v."Status" = 1
      OR v."Id" = p."ActiveVersionId"
      OR v."Id" = p."LatestVersionId"
  );

UPDATE "ContentPackages" AS cp
SET "Title" = '{{Sql(name)}}',
    "Description" = '{{Sql(description)}}',
    "ComparisonFeaturesJson" = '{{Sql(comparisonFeaturesJson)}}',
    "UpdatedAt" = now()
FROM "BillingPlans" AS p
WHERE cp."BillingPlanId" = p."Id"
  AND p."Code" IN ({{codes}});
""");
    }

    private static void UpdateManualAddOn(
        MigrationBuilder migrationBuilder,
        string code,
        string name,
        string description)
    {
        var codes = AddOnCodes(code);
        migrationBuilder.Sql($$"""
UPDATE "BillingAddOns"
SET "Name" = '{{Sql(name)}}',
    "Description" = '{{Sql(description)}}',
    "UpdatedAt" = now()
WHERE "Code" IN ({{codes}});

UPDATE "BillingAddOnVersions" AS v
SET "Name" = '{{Sql(name)}}',
    "Description" = '{{Sql(description)}}'
FROM "BillingAddOns" AS a
WHERE v."AddOnId" = a."Id"
  AND a."Code" IN ({{codes}})
  AND (
      v."Status" = 1
      OR v."Id" = a."ActiveVersionId"
      OR v."Id" = a."LatestVersionId"
  );

UPDATE "ContentPackages" AS cp
SET "Title" = '{{Sql(name)}}',
    "Description" = '{{Sql(description)}}',
    "UpdatedAt" = now()
FROM "BillingAddOns" AS a
WHERE cp."BillingAddOnId" = a."Id"
  AND a."Code" IN ({{codes}});
""");
    }

    private static void UpdateAiPackage(
        MigrationBuilder migrationBuilder,
        string code,
        string name,
        string description,
        int grantCredits,
        string grantEntitlementsJson,
        string group,
        string featuresJson)
    {
        migrationBuilder.Sql($$"""
UPDATE "BillingAddOns"
SET "Name" = '{{Sql(name)}}',
    "Description" = '{{Sql(description)}}',
    "Status" = 1,
    "GrantCredits" = {{grantCredits}},
    "GrantEntitlementsJson" = '{{Sql(grantEntitlementsJson)}}',
    "AiPackageGroup" = '{{Sql(group)}}',
    "AiFeaturesJson" = '{{Sql(featuresJson)}}',
    "UpdatedAt" = now()
WHERE "Code" = '{{Sql(code)}}';

UPDATE "BillingAddOnVersions" AS v
SET "Name" = '{{Sql(name)}}',
    "Description" = '{{Sql(description)}}',
    "Status" = 1,
    "GrantCredits" = {{grantCredits}},
    "GrantEntitlementsJson" = '{{Sql(grantEntitlementsJson)}}',
    "AiPackageGroup" = '{{Sql(group)}}',
    "AiFeaturesJson" = '{{Sql(featuresJson)}}'
FROM "BillingAddOns" AS a
WHERE v."AddOnId" = a."Id"
  AND a."Code" = '{{Sql(code)}}'
  AND (
      v."Status" = 1
      OR v."Id" = a."ActiveVersionId"
      OR v."Id" = a."LatestVersionId"
  );

UPDATE "ContentPackages" AS cp
SET "Title" = '{{Sql(name)}}',
    "Description" = '{{Sql(description)}}',
    "ComparisonFeaturesJson" = '{{Sql(featuresJson)}}',
    "UpdatedAt" = now()
FROM "BillingAddOns" AS a
WHERE cp."BillingAddOnId" = a."Id"
  AND a."Code" = '{{Sql(code)}}';
""");
    }

    private static string PlanCodes(string code) => code switch
    {
        "speaking-1session" => "'speaking-1session','speaking-1session-plan'",
        "speaking-2sessions" => "'speaking-2sessions','speaking-2sessions-plan'",
        _ => $"'{Sql(code)}'"
    };

    private static string AddOnCodes(string code) => code switch
    {
        "addon-speaking-1session" => "'addon-speaking-1session','speaking-1session'",
        "addon-speaking-2sessions" => "'addon-speaking-2sessions','speaking-2sessions'",
        _ => $"'{Sql(code)}'"
    };

    private static string Sql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string SqlInt(int? value) => value?.ToString() ?? "NULL";
}
