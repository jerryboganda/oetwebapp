using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260702090000_AddAiPackageCreditsAndSeedCatalog")]
    public partial class AddAiPackageCreditsAndSeedCatalog : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS "AiPackageCreditAccounts" (
    "Id" varchar(64) NOT NULL,
    "UserId" varchar(64) NOT NULL,
    "FlexibleCredits" integer NOT NULL DEFAULT 0,
    "WritingOnlyCredits" integer NOT NULL DEFAULT 0,
    "SpeakingOnlyCredits" integer NOT NULL DEFAULT 0,
    "ListeningTestsRemaining" integer NULL,
    "ReadingTestsRemaining" integer NULL,
    "MockExamsRemaining" integer NOT NULL DEFAULT 0,
    "ExpiresAt" timestamp with time zone NULL,
    "ExpiredBecausePassed" boolean NOT NULL DEFAULT false,
    "PassedAt" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AiPackageCreditAccounts" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AiPackageCreditAccounts_UserId"
    ON "AiPackageCreditAccounts" ("UserId");

CREATE TABLE IF NOT EXISTS "AiPackageCreditTransactions" (
    "Id" varchar(64) NOT NULL,
    "UserId" varchar(64) NOT NULL,
    "AccountId" varchar(64) NOT NULL,
    "StripeSessionId" varchar(128) NULL,
    "PackageId" varchar(64) NULL,
    "PackageType" varchar(32) NULL,
    "FlexibleCreditsDelta" integer NOT NULL DEFAULT 0,
    "WritingOnlyCreditsDelta" integer NOT NULL DEFAULT 0,
    "SpeakingOnlyCreditsDelta" integer NOT NULL DEFAULT 0,
    "ListeningTestsDelta" integer NOT NULL DEFAULT 0,
    "ReadingTestsDelta" integer NOT NULL DEFAULT 0,
    "MockExamsDelta" integer NOT NULL DEFAULT 0,
    "Reason" integer NOT NULL,
    "ReferenceId" varchar(128) NULL,
    "JobId" varchar(64) NULL,
    "Description" varchar(512) NOT NULL DEFAULT '',
    "ExpiresAt" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedByAdminId" varchar(64) NULL,
    CONSTRAINT "PK_AiPackageCreditTransactions" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_AiPackageCreditTransactions_UserId_CreatedAt"
    ON "AiPackageCreditTransactions" ("UserId", "CreatedAt");
CREATE UNIQUE INDEX IF NOT EXISTS "UX_AiPackageCreditTransactions_StripeSessionId"
    ON "AiPackageCreditTransactions" ("StripeSessionId")
    WHERE "StripeSessionId" IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS "UX_AiPackageCreditTransactions_Reference_Reason"
    ON "AiPackageCreditTransactions" ("ReferenceId", "Reason")
    WHERE "ReferenceId" IS NOT NULL;

CREATE TABLE IF NOT EXISTS "LearnerExamOutcomes" (
    "Id" varchar(64) NOT NULL,
    "UserId" varchar(64) NOT NULL,
    "Passed" boolean NOT NULL,
    "ExamDate" timestamp with time zone NOT NULL,
    "RecordedByAdminId" varchar(64) NOT NULL,
    "RecordedByAdminName" varchar(128) NOT NULL,
    "EvidenceNote" varchar(512) NULL,
    "RecordedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_LearnerExamOutcomes" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_LearnerExamOutcomes_UserId_ExamDate"
    ON "LearnerExamOutcomes" ("UserId", "ExamDate");
""");

            migrationBuilder.Sql("""
WITH ai_packages("Code","Name","Description","Price","DurationDays","GrantCredits","GrantEntitlementsJson","LettersGranted","SessionsGranted","DisplayOrder") AS (
    VALUES
    ('pkg_quick_check','Quick Check','5 flexible AI grading credits for Writing or Speaking, valid for 30 days.',19,30,5,'{"package_type":"full","flexible_credits":5,"listening_tests":null,"reading_tests":null,"mock_exams":0,"feedback_reports":true}',0,0,10),
    ('pkg_exam_prep_pro','Exam Prep Pro','15 flexible AI grading credits plus 2 full mock exams, valid for 90 days.',42,90,15,'{"package_type":"full","flexible_credits":15,"listening_tests":null,"reading_tests":null,"mock_exams":2,"feedback_reports":true,"personalised_study_recs":true}',0,0,20),
    ('pkg_oet_mastery','OET Mastery','30 flexible AI grading credits plus 5 full mock exams, priority queue, and 6-month validity.',100,180,30,'{"package_type":"full","flexible_credits":30,"listening_tests":null,"reading_tests":null,"mock_exams":5,"feedback_reports":true,"priority_queue":true,"personalised_study_recs":true,"pass_guarantee_extension_months":1}',0,0,30),
    ('pkg_listening_starter','Listening Starter','5 deterministic Listening practice tests, valid for 30 days.',5,30,0,'{"package_type":"listening","listening_tests":5}',0,0,110),
    ('pkg_listening_standard','Listening Standard','15 deterministic Listening practice tests, valid for 90 days.',12,90,0,'{"package_type":"listening","listening_tests":15}',0,0,120),
    ('pkg_listening_pro','Listening Pro','Unlimited deterministic Listening practice tests, valid for 6 months.',19,180,0,'{"package_type":"listening","listening_tests":null}',0,0,130),
    ('pkg_reading_starter','Reading Starter','5 deterministic Reading practice tests, valid for 30 days.',5,30,0,'{"package_type":"reading","reading_tests":5}',0,0,210),
    ('pkg_reading_standard','Reading Standard','15 deterministic Reading practice tests, valid for 90 days.',12,90,0,'{"package_type":"reading","reading_tests":15}',0,0,220),
    ('pkg_reading_pro','Reading Pro','Unlimited deterministic Reading practice tests, valid for 6 months.',19,180,0,'{"package_type":"reading","reading_tests":null}',0,0,230),
    ('pkg_writing_starter','Writing Starter','3 AI-graded Writing letters, valid for 30 days.',12,30,3,'{"package_type":"writing","writing_only_credits":3}',3,0,310),
    ('pkg_writing_standard','Writing Standard','8 AI-graded Writing letters, valid for 90 days.',25,90,8,'{"package_type":"writing","writing_only_credits":8}',8,0,320),
    ('pkg_writing_pro','Writing Pro','15 AI-graded Writing letters, valid for 6 months.',42,180,15,'{"package_type":"writing","writing_only_credits":15}',15,0,330),
    ('pkg_speaking_starter','Speaking Starter','3 AI-graded Speaking role-play cards, valid for 30 days.',15,30,3,'{"package_type":"speaking","speaking_only_credits":3}',0,3,410),
    ('pkg_speaking_standard','Speaking Standard','8 AI-graded Speaking role-play cards, valid for 90 days.',32,90,8,'{"package_type":"speaking","speaking_only_credits":8}',0,8,420),
    ('pkg_speaking_pro','Speaking Pro','15 AI-graded Speaking role-play cards, valid for 6 months.',55,180,15,'{"package_type":"speaking","speaking_only_credits":15}',0,15,430),
    ('pkg_mock_1','Mock Package 1','1 full mock exam with separate mock allowance, valid for 6 months.',25,180,0,'{"package_type":"mock","mock_exams":1,"mockFull":1}',0,0,510),
    ('pkg_mock_3','Mock Package 3','3 full mock exams with separate mock allowance, valid for 6 months.',59,180,0,'{"package_type":"mock","mock_exams":3,"mockFull":3}',0,0,520),
    ('pkg_mock_5','Mock Package 5','5 full mock exams with separate mock allowance, valid for 6 months.',89,180,0,'{"package_type":"mock","mock_exams":5,"mockFull":5}',0,0,530)
)
INSERT INTO "BillingAddOns" (
    "Id", "Code", "Name", "Description", "Price", "Currency", "Interval",
    "Status", "IsRecurring", "DurationDays", "GrantCredits", "GrantEntitlementsJson",
    "CompatiblePlanCodesJson", "ActiveVersionId", "LatestVersionId", "AppliesToAllPlans",
    "IsStackable", "QuantityStep", "MaxQuantity", "DisplayOrder", "CreatedAt", "UpdatedAt",
    "OriginalPriceGbp", "AddonKind", "RequiresEligibleParent", "EligibilityFlag",
    "LettersGranted", "SessionsGranted", "ExtensionDays"
)
SELECT
    'addon_' || p."Code", p."Code", p."Name", p."Description", p."Price", 'GBP', 'one_time',
    1, false, p."DurationDays", p."GrantCredits", p."GrantEntitlementsJson",
    '[]', 'addonv_' || p."Code" || '_v1', 'addonv_' || p."Code" || '_v1', true,
    true, 1, NULL, p."DisplayOrder", now(), now(),
    NULL, 'ai_package', false, '',
    p."LettersGranted", p."SessionsGranted", 0
FROM ai_packages p
WHERE NOT EXISTS (
    SELECT 1 FROM "BillingAddOns" existing WHERE existing."Code" = p."Code"
);

WITH ai_packages("Code","Name","Description","Price","DurationDays","GrantCredits","GrantEntitlementsJson","LettersGranted","SessionsGranted","DisplayOrder") AS (
    VALUES
    ('pkg_quick_check','Quick Check','5 flexible AI grading credits for Writing or Speaking, valid for 30 days.',19,30,5,'{"package_type":"full","flexible_credits":5,"listening_tests":null,"reading_tests":null,"mock_exams":0,"feedback_reports":true}',0,0,10),
    ('pkg_exam_prep_pro','Exam Prep Pro','15 flexible AI grading credits plus 2 full mock exams, valid for 90 days.',42,90,15,'{"package_type":"full","flexible_credits":15,"listening_tests":null,"reading_tests":null,"mock_exams":2,"feedback_reports":true,"personalised_study_recs":true}',0,0,20),
    ('pkg_oet_mastery','OET Mastery','30 flexible AI grading credits plus 5 full mock exams, priority queue, and 6-month validity.',100,180,30,'{"package_type":"full","flexible_credits":30,"listening_tests":null,"reading_tests":null,"mock_exams":5,"feedback_reports":true,"priority_queue":true,"personalised_study_recs":true,"pass_guarantee_extension_months":1}',0,0,30),
    ('pkg_listening_starter','Listening Starter','5 deterministic Listening practice tests, valid for 30 days.',5,30,0,'{"package_type":"listening","listening_tests":5}',0,0,110),
    ('pkg_listening_standard','Listening Standard','15 deterministic Listening practice tests, valid for 90 days.',12,90,0,'{"package_type":"listening","listening_tests":15}',0,0,120),
    ('pkg_listening_pro','Listening Pro','Unlimited deterministic Listening practice tests, valid for 6 months.',19,180,0,'{"package_type":"listening","listening_tests":null}',0,0,130),
    ('pkg_reading_starter','Reading Starter','5 deterministic Reading practice tests, valid for 30 days.',5,30,0,'{"package_type":"reading","reading_tests":5}',0,0,210),
    ('pkg_reading_standard','Reading Standard','15 deterministic Reading practice tests, valid for 90 days.',12,90,0,'{"package_type":"reading","reading_tests":15}',0,0,220),
    ('pkg_reading_pro','Reading Pro','Unlimited deterministic Reading practice tests, valid for 6 months.',19,180,0,'{"package_type":"reading","reading_tests":null}',0,0,230),
    ('pkg_writing_starter','Writing Starter','3 AI-graded Writing letters, valid for 30 days.',12,30,3,'{"package_type":"writing","writing_only_credits":3}',3,0,310),
    ('pkg_writing_standard','Writing Standard','8 AI-graded Writing letters, valid for 90 days.',25,90,8,'{"package_type":"writing","writing_only_credits":8}',8,0,320),
    ('pkg_writing_pro','Writing Pro','15 AI-graded Writing letters, valid for 6 months.',42,180,15,'{"package_type":"writing","writing_only_credits":15}',15,0,330),
    ('pkg_speaking_starter','Speaking Starter','3 AI-graded Speaking role-play cards, valid for 30 days.',15,30,3,'{"package_type":"speaking","speaking_only_credits":3}',0,3,410),
    ('pkg_speaking_standard','Speaking Standard','8 AI-graded Speaking role-play cards, valid for 90 days.',32,90,8,'{"package_type":"speaking","speaking_only_credits":8}',0,8,420),
    ('pkg_speaking_pro','Speaking Pro','15 AI-graded Speaking role-play cards, valid for 6 months.',55,180,15,'{"package_type":"speaking","speaking_only_credits":15}',0,15,430),
    ('pkg_mock_1','Mock Package 1','1 full mock exam with separate mock allowance, valid for 6 months.',25,180,0,'{"package_type":"mock","mock_exams":1,"mockFull":1}',0,0,510),
    ('pkg_mock_3','Mock Package 3','3 full mock exams with separate mock allowance, valid for 6 months.',59,180,0,'{"package_type":"mock","mock_exams":3,"mockFull":3}',0,0,520),
    ('pkg_mock_5','Mock Package 5','5 full mock exams with separate mock allowance, valid for 6 months.',89,180,0,'{"package_type":"mock","mock_exams":5,"mockFull":5}',0,0,530)
)
INSERT INTO "BillingAddOnVersions" (
    "Id", "AddOnId", "VersionNumber", "Code", "Name", "Description", "Price",
    "Currency", "Interval", "Status", "IsRecurring", "DurationDays", "GrantCredits",
    "GrantEntitlementsJson", "CompatiblePlanCodesJson", "AppliesToAllPlans", "IsStackable",
    "QuantityStep", "MaxQuantity", "DisplayOrder", "CreatedByAdminId", "CreatedByAdminName",
    "CreatedAt", "OriginalPriceGbp", "AddonKind", "RequiresEligibleParent", "EligibilityFlag",
    "LettersGranted", "SessionsGranted", "ExtensionDays"
)
SELECT
    'addonv_' || p."Code" || '_v1', 'addon_' || p."Code", 1, p."Code", p."Name", p."Description", p."Price",
    'GBP', 'one_time', 1, false, p."DurationDays", p."GrantCredits",
    p."GrantEntitlementsJson", '[]', true, true,
    1, NULL, p."DisplayOrder", 'system:ai-packages-pdf', 'AI Packages PDF Seeder',
    now(), NULL, 'ai_package', false, '',
    p."LettersGranted", p."SessionsGranted", 0
FROM ai_packages p
WHERE NOT EXISTS (
    SELECT 1 FROM "BillingAddOnVersions" existing WHERE existing."Id" = 'addonv_' || p."Code" || '_v1'
);
""");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DELETE FROM "BillingAddOnVersions" WHERE "AddonKind" = 'ai_package' AND "Code" LIKE 'pkg_%';
DELETE FROM "BillingAddOns" WHERE "AddonKind" = 'ai_package' AND "Code" LIKE 'pkg_%';
DROP TABLE IF EXISTS "LearnerExamOutcomes";
DROP TABLE IF EXISTS "AiPackageCreditTransactions";
DROP TABLE IF EXISTS "AiPackageCreditAccounts";
""");
        }
    }
}
