param(
  [switch]$DryRun = $true,
  [switch]$Execute,
  [switch]$IHaveABackup,
  [switch]$IAmOnLocalOrRestoredDb,
  [string]$ConnectionString = $env:OET_RESET_CONNECTION_STRING
)

$ErrorActionPreference = "Stop"

if (-not $ConnectionString) {
  throw "Set OET_RESET_CONNECTION_STRING or pass -ConnectionString. Never point this at production."
}

if ($Execute -and (-not $IHaveABackup -or -not $IAmOnLocalOrRestoredDb)) {
  throw "Execution requires -IHaveABackup and -IAmOnLocalOrRestoredDb. This reset is local/dev/restored-DB only."
}

$sqlCounts = @"
SELECT 'ContentPapers(reading)' AS table_name, COUNT(*) FROM "ContentPapers" WHERE "SubtestCode" = 'reading'
UNION ALL SELECT 'ReadingParts', COUNT(*) FROM "ReadingParts" p JOIN "ContentPapers" cp ON cp."Id" = p."PaperId" WHERE cp."SubtestCode" = 'reading'
UNION ALL SELECT 'ReadingTexts', COUNT(*) FROM "ReadingTexts" t JOIN "ReadingParts" p ON p."Id" = t."ReadingPartId" JOIN "ContentPapers" cp ON cp."Id" = p."PaperId" WHERE cp."SubtestCode" = 'reading'
UNION ALL SELECT 'ReadingQuestions', COUNT(*) FROM "ReadingQuestions" q JOIN "ReadingParts" p ON p."Id" = q."ReadingPartId" JOIN "ContentPapers" cp ON cp."Id" = p."PaperId" WHERE cp."SubtestCode" = 'reading'
UNION ALL SELECT 'ReadingAttempts', COUNT(*) FROM "ReadingAttempts" a JOIN "ContentPapers" cp ON cp."Id" = a."PaperId" WHERE cp."SubtestCode" = 'reading'
UNION ALL SELECT 'ReadingAnswers', COUNT(*) FROM "ReadingAnswers" ans JOIN "ReadingAttempts" a ON a."Id" = ans."ReadingAttemptId" JOIN "ContentPapers" cp ON cp."Id" = a."PaperId" WHERE cp."SubtestCode" = 'reading'
UNION ALL SELECT 'ReadingPaperAnnotations', COUNT(*) FROM "ReadingPaperAnnotations" a JOIN "ContentPapers" cp ON cp."Id" = a."PaperId" WHERE cp."SubtestCode" = 'reading'
UNION ALL SELECT 'ReadingExtractionDrafts', COUNT(*) FROM "ReadingExtractionDrafts" d JOIN "ContentPapers" cp ON cp."Id" = d."PaperId" WHERE cp."SubtestCode" = 'reading'
UNION ALL SELECT 'ContentPaperAssets(reading)', COUNT(*) FROM "ContentPaperAssets" a JOIN "ContentPapers" cp ON cp."Id" = a."PaperId" WHERE cp."SubtestCode" = 'reading';
"@

$sqlDelete = @"
BEGIN;
CREATE TEMP TABLE _reading_papers AS SELECT "Id" FROM "ContentPapers" WHERE "SubtestCode" = 'reading';
CREATE TEMP TABLE _reading_parts AS SELECT "Id" FROM "ReadingParts" WHERE "PaperId" IN (SELECT "Id" FROM _reading_papers);
CREATE TEMP TABLE _reading_questions AS SELECT "Id" FROM "ReadingQuestions" WHERE "ReadingPartId" IN (SELECT "Id" FROM _reading_parts);
CREATE TEMP TABLE _reading_attempts AS SELECT "Id" FROM "ReadingAttempts" WHERE "PaperId" IN (SELECT "Id" FROM _reading_papers);

DELETE FROM "ReadingAnswerRevisions" WHERE "ReadingAttemptId" IN (SELECT "Id" FROM _reading_attempts);
DELETE FROM "ReadingAnswers" WHERE "ReadingAttemptId" IN (SELECT "Id" FROM _reading_attempts);
DELETE FROM "ReadingAttemptFeedbacks" WHERE "ReadingAttemptId" IN (SELECT "Id" FROM _reading_attempts);
DELETE FROM "ReadingErrorBankEntries" WHERE "PaperId" IN (SELECT "Id" FROM _reading_papers) OR "ReadingQuestionId" IN (SELECT "Id" FROM _reading_questions);
DELETE FROM "ReadingQuestionReviewLogs" WHERE "ReadingQuestionId" IN (SELECT "Id" FROM _reading_questions);
DELETE FROM "ReadingPaperAnnotations" WHERE "PaperId" IN (SELECT "Id" FROM _reading_papers);
DELETE FROM "ReadingExtractionDrafts" WHERE "PaperId" IN (SELECT "Id" FROM _reading_papers);
DELETE FROM "MockSectionAttempts" WHERE "SubtestCode" = 'reading' AND "ContentId" IN (SELECT "Id" FROM _reading_papers);
DELETE FROM "MockBundleSections" WHERE "SubtestCode" = 'reading' AND "ContentId" IN (SELECT "Id" FROM _reading_papers);
DELETE FROM "ReadingAttempts" WHERE "Id" IN (SELECT "Id" FROM _reading_attempts);
DELETE FROM "ReadingQuestions" WHERE "Id" IN (SELECT "Id" FROM _reading_questions);
DELETE FROM "ReadingTexts" WHERE "ReadingPartId" IN (SELECT "Id" FROM _reading_parts);
DELETE FROM "ReadingParts" WHERE "Id" IN (SELECT "Id" FROM _reading_parts);
DELETE FROM "ContentPaperAssets" WHERE "PaperId" IN (SELECT "Id" FROM _reading_papers);
DELETE FROM "ContentPapers" WHERE "Id" IN (SELECT "Id" FROM _reading_papers);
COMMIT;
"@

Write-Host "Reading reset dry-run counts:"
$sqlCounts | psql $ConnectionString

if (-not $Execute -or $DryRun) {
  Write-Host "Dry run only. Re-run with -Execute -DryRun:`$false -IHaveABackup -IAmOnLocalOrRestoredDb to mutate a local/restored DB."
  exit 0
}

Write-Host "Executing local/restored Reading reset..."
$sqlDelete | psql $ConnectionString
Write-Host "Post-delete counts:"
$sqlCounts | psql $ConnectionString
Write-Host "Media files were not removed by this script. Orphan MediaAsset cleanup must run separately through storage abstractions."
