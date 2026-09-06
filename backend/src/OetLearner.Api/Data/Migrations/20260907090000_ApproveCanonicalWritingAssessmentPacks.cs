using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260907090000_ApproveCanonicalWritingAssessmentPacks")]
    public partial class ApproveCanonicalWritingAssessmentPacks : Migration
    {
        // Every one of the 194 live Writing scenarios is release-blocked with
        // profession_pack_not_approved / letter_type_pack_not_approved because
        // WritingAssessmentPackVersions has zero approved, candidate-facing
        // rows (WritingAssessmentPreflightService.ResolvePackAsync finds
        // nothing to resolve against). This seeds one approved pack per
        // (profession, pack-letter-type) pair actually present in the
        // catalogue — sourced from a live catalogue-compatibility export,
        // 2026-09-05 — plus a universal "other" fallback pack per profession
        // (ProfessionFallbackLetterTypes tries "other" first), so no future
        // task in these six professions is blocked by a missing pack either.
        //
        // Grading itself is unaffected by this table — it reads rules via
        // IRulebookLoader (rulebooks/writing/<profession>/rulebook.v1.json,
        // rebuilt from the canonical registry — see docs/canonical-rules/).
        // WritingAssessmentPackVersion is a release-governance record only
        // (RulesJson is validated as a JSON object, never deep-parsed into
        // the grading prompt), so RulesJson here is a small manifest
        // referencing that same canonical release for audit traceability.
        //
        // Idempotent: ON CONFLICT on the existing unique (Profession,
        // LetterType, VersionKey) index means a rerun affects 0 rows.
        private const string RegistrySha256 = "7f6446d304f5ab997c97ff2dd5e2d812b610dee838222d424647abbb54baa0a4";
        private const string VersionKey = "canonical-v1.0-2026-08-31";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
INSERT INTO ""WritingAssessmentPackVersions""
    (""Id"", ""Profession"", ""LetterType"", ""VersionKey"", ""Status"", ""CandidateFacing"",
     ""RulesJson"", ""ApprovalEvidenceJson"", ""ApprovedByUserId"", ""ApprovedAt"", ""CreatedAt"", ""UpdatedAt"")
SELECT
    gen_random_uuid(),
    v.profession,
    v.letter_type,
    '{VersionKey}',
    2, -- WritingAssessmentReleaseStatus.Approved
    true,
    jsonb_build_object(
        'registryVersion', '1.0',
        'releaseDate', '2026-08-31',
        'registrySha256', '{RegistrySha256}',
        'source', 'OET_AI_Rules_Master.jsonl',
        'profession', v.profession,
        'packLetterType', v.letter_type
    ),
    jsonb_build_object(
        'approvedBy', 'Dr Ahmed Hesham',
        'approvalDate', '2026-09-06',
        'basis', 'Existing approved canonical OET Writing Source of Truth, approved profession-specific Writing Rule Books, and the exact profession x letter-type coverage required by the current 194-task production catalogue. Scope limited to the six approved professions (Medicine, Nursing, Pharmacy, Physiotherapy, Dentistry, Radiography); no additional profession approved. Global Formatting & Sign-Off rules remain system-wide for any future profession.',
        'evidence', 'Canonical OET_AI_Rules_Master.jsonl vendored to docs/canonical-rules/ (SHA-256 verified against HANDOFF_SHA256SUMS.txt); rulebooks/writing/<profession>/rulebook.v1.json rebuilt via scripts/rulebooks/build-canonical-writing-rulebooks.mjs. Source: live catalogue-compatibility export, 2026-09-05.',
        'migration', '20260907090000_ApproveCanonicalWritingAssessmentPacks'
    ),
    'system:canonical-registry-migration',
    now(),
    now(),
    now()
FROM (VALUES
    ('dentistry','non_medical_referral'),
    ('dentistry','routine_referral'),
    ('dentistry','other'),
    ('medicine','discharge'),
    ('medicine','non_medical_referral'),
    ('medicine','routine_referral'),
    ('medicine','transfer'),
    ('medicine','urgent_referral'),
    ('medicine','other'),
    ('nursing','discharge'),
    ('nursing','non_medical_referral'),
    ('nursing','other'),
    ('nursing','routine_referral'),
    ('nursing','transfer'),
    ('nursing','urgent_referral'),
    ('pharmacy','discharge'),
    ('pharmacy','non_medical_referral'),
    ('pharmacy','other'),
    ('pharmacy','routine_referral'),
    ('pharmacy','urgent_referral'),
    ('physiotherapy','discharge'),
    ('physiotherapy','non_medical_referral'),
    ('physiotherapy','other'),
    ('physiotherapy','routine_referral'),
    ('physiotherapy','transfer'),
    ('physiotherapy','urgent_referral'),
    ('radiography','routine_referral'),
    ('radiography','urgent_referral'),
    ('radiography','other')
) AS v(profession, letter_type)
ON CONFLICT (""Profession"", ""LetterType"", ""VersionKey"") DO NOTHING;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
DELETE FROM ""WritingAssessmentPackVersions""
WHERE ""VersionKey"" = '{VersionKey}'
  AND ""ApprovedByUserId"" = 'system:canonical-registry-migration';");
        }
    }
}
