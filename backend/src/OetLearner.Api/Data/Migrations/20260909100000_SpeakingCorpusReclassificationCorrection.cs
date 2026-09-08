using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// 2026-09-09 — corrective follow-up to the corpus reclassification
/// migrations (20260909091500-091800). Root cause: <c>ApplyIfUnclassified</c>
/// treated ANY "Other Cards" row as an automatic-sweep placeholder
/// regardless of provenance, so the boot-time sweep that runs on every API
/// start silently re-ran the deterministic classifier over 23 rows the
/// dual-review process had already confirmed (11 deliberately left "Other
/// Cards", 12 fully resolved to a specific category) — reclassifying them
/// with CategorySource back to 'classifier' the moment this repair's own
/// deploy restarted the API containers. Fixed at the root in
/// SpeakingCardClassifier.ApplyIfUnclassified (now skips any row whose
/// CategorySource is manual/reviewed/seed, full stop). This migration
/// restores the 23 affected rows to the dual-review's actual conclusion.
///
/// <para>Down() restores the exact clobbered state captured from production
/// immediately before this fix, for a true revert.</para>
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260909100000_SpeakingCorpusReclassificationCorrection")]
public partial class SpeakingCorpusReclassificationCorrection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
UPDATE "RolePlayCards" AS t
SET "PrimaryCategory" = v.category,
    "SecondaryTagsJson" = v.tags,
    "CategoryNeedsReview" = v.needs_review,
    "CategorySource" = 'reviewed',
    "CategoryClassifierVersion" = NULL,
    "CategoryClassifiedAt" = NULL
FROM (VALUES
  ('rpc-0988f25d739d4f9dadff0c2707dd0279', 'Other Cards', '[]', TRUE),
  ('rpc-0b9e6e509ac3427880fd48ca7118613c', 'Other Cards', '[]', TRUE),
  ('rpc-0de5fdc717984ae9b0d18b52d548007c', 'Other Cards', '[]', FALSE),
  ('rpc-15b96c943ecf4a5c8ff4a076613063ea', 'Other Cards', '[]', FALSE),
  ('rpc-1efd19f4a57d4e00806ef91cf892f315', 'Other Cards', '[]', TRUE),
  ('rpc-32c8d204e1914a3a871ab83b507d90d9', 'Other Cards', '[]', TRUE),
  ('rpc-38bed9cde54f4b80a249c3bcaf94698c', 'Other Cards', '[]', TRUE),
  ('rpc-41de074ef95145cdb9f018ca5aa21158', 'Other Cards', '[]', TRUE),
  ('rpc-441640f66df04020990281c213ad937b', 'Other Cards', '[]', TRUE),
  ('rpc-488ab731c92e4f038180ad66e62e910f', 'Other Cards', '[]', TRUE),
  ('rpc-4d2e8d04fd594b8983919fa65a08629b', 'Other Cards', '[]', TRUE),
  ('rpc-54c7858eec1a4248bb4a3c0ba07abb52', 'Other Cards', '[]', TRUE),
  ('rpc-5617ea849e9f41d3b21b3d3f11cfcef7', 'Other Cards', '[]', TRUE),
  ('rpc-62a4ab1f1bc247408502a000e53bca9f', 'Other Cards', '[]', TRUE),
  ('rpc-64d9d174344840c1a1c044f90bb3c33f', 'Other Cards', '[]', TRUE),
  ('rpc-677cae8ae8f3436bb65242e4240dfbbc', 'Other Cards', '[]', TRUE),
  ('rpc-6b0f49ec73834da78a3a4950d541fce2', 'Other Cards', '[]', TRUE),
  ('rpc-7c80160a343b403782ad91f132075774', 'Other Cards', '[]', TRUE),
  ('rpc-806752e7c63740c2802b1a2bc0663377', 'Other Cards', '[]', TRUE),
  ('rpc-81cbb8a1c4cb45568636c0c588e86aea', 'Other Cards', '[]', TRUE),
  ('rpc-89ca65c512a146c880b8ad567987f928', 'Other Cards', '[]', TRUE),
  ('rpc-8eb573c16e47407888b6feeac132a0fe', 'Other Cards', '[]', TRUE),
  ('rpc-a25fa383364f41bb951adc390386629f', 'Other Cards', '[]', TRUE)
) AS v(id, category, tags, needs_review)
WHERE t."Id" = v.id;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
UPDATE "RolePlayCards" AS t
SET "PrimaryCategory" = v.category,
    "SecondaryTagsJson" = v.tags,
    "CategoryNeedsReview" = v.needs_review,
    "CategorySource" = 'classifier'
FROM (VALUES
  ('rpc-0988f25d739d4f9dadff0c2707dd0279', 'Already Known Patient', '["Breaking Bad News","Angry"]', FALSE),
  ('rpc-0b9e6e509ac3427880fd48ca7118613c', 'Other Cards', '[]', TRUE),
  ('rpc-0de5fdc717984ae9b0d18b52d548007c', 'Other Cards', '[]', TRUE),
  ('rpc-15b96c943ecf4a5c8ff4a076613063ea', 'Other Cards', '[]', TRUE),
  ('rpc-1efd19f4a57d4e00806ef91cf892f315', 'Second Visit / Follow-up', '[]', FALSE),
  ('rpc-32c8d204e1914a3a871ab83b507d90d9', 'Other Cards', '[]', TRUE),
  ('rpc-38bed9cde54f4b80a249c3bcaf94698c', 'Other Cards', '[]', TRUE),
  ('rpc-41de074ef95145cdb9f018ca5aa21158', 'Other Cards', '[]', TRUE),
  ('rpc-441640f66df04020990281c213ad937b', 'Other Cards', '[]', TRUE),
  ('rpc-488ab731c92e4f038180ad66e62e910f', 'Already Known Patient', '["Breaking Bad News","Angry"]', FALSE),
  ('rpc-4d2e8d04fd594b8983919fa65a08629b', 'Already Known Patient', '["Breaking Bad News","Angry"]', FALSE),
  ('rpc-54c7858eec1a4248bb4a3c0ba07abb52', 'Other Cards', '[]', TRUE),
  ('rpc-5617ea849e9f41d3b21b3d3f11cfcef7', 'Other Cards', '[]', TRUE),
  ('rpc-62a4ab1f1bc247408502a000e53bca9f', 'Other Cards', '[]', TRUE),
  ('rpc-64d9d174344840c1a1c044f90bb3c33f', 'Reluctant Patient', '[]', FALSE),
  ('rpc-677cae8ae8f3436bb65242e4240dfbbc', 'Angry Patient', '[]', FALSE),
  ('rpc-6b0f49ec73834da78a3a4950d541fce2', 'Reluctant Patient', '[]', FALSE),
  ('rpc-7c80160a343b403782ad91f132075774', 'Reluctant Patient', '[]', FALSE),
  ('rpc-806752e7c63740c2802b1a2bc0663377', 'Already Known Patient', '[]', FALSE),
  ('rpc-81cbb8a1c4cb45568636c0c588e86aea', 'Other Cards', '[]', TRUE),
  ('rpc-89ca65c512a146c880b8ad567987f928', 'Already Known Patient', '["Breaking Bad News","Angry"]', FALSE),
  ('rpc-8eb573c16e47407888b6feeac132a0fe', 'Already Known Patient', '["Breaking Bad News","Reluctant"]', FALSE),
  ('rpc-a25fa383364f41bb951adc390386629f', 'Already Known Patient', '["Breaking Bad News","Reluctant"]', FALSE)
) AS v(id, category, tags, needs_review)
WHERE t."Id" = v.id;
""");
    }
}
