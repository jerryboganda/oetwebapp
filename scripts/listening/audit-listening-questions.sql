-- Listening Part B / C data-quality audit (spec §12)
-- Run on learner DB: psql $DATABASE_URL -f scripts/listening/audit-listening-questions.sql
-- Flags: See PDF sentinel, CPDF/PDF placeholder, Option A/B/C placeholders,
--        PAGE artifacts, Practice Test footers, Word bullet glyph, empty prompts

\pset border 2
\echo '=== 1) Sentinel prompts (See PDF / CPDF / View PDF) — should be 0 after fix ==='
SELECT
  p.title AS paper,
  q."QuestionNumber" AS number,
  q."ListeningPartId" AS part_id,
  LEFT(q."Stem", 80) AS stem_preview
FROM "ListeningQuestions" q
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
WHERE LOWER(TRIM(q."Stem")) IN ('see pdf','cpdf','pdf','view pdf')
ORDER BY p.title, q."QuestionNumber";

\echo '=== 2) Placeholder options (Option A/B/C) — should be 0 ==='
SELECT
  p.title AS paper,
  q."QuestionNumber" AS number,
  o."OptionKey" AS key,
  o."Text" AS text
FROM "ListeningQuestionOptions" o
JOIN "ListeningQuestions" q ON q."Id" = o."ListeningQuestionId"
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
WHERE LOWER(TRIM(o."Text")) IN ('option a','option b','option c')
ORDER BY p.title, q."QuestionNumber", o."OptionKey";

\echo '=== 3) Artifact lines in prompts (PAGE / Practice Test / ======) ==='
SELECT
  p.title AS paper,
  q."QuestionNumber" AS number,
  LEFT(q."Stem", 120) AS stem_preview
FROM "ListeningQuestions" q
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
WHERE q."Stem" ~* 'PAGE\s+\d+'
   OR q."Stem" ~* '======\s*PAGE'
   OR q."Stem" ~* 'Practice Test'
   OR q."Stem" ~* E'\uF0B7'
ORDER BY p.title, q."QuestionNumber";

\echo '=== 4) Artifact lines in options ==='
SELECT
  p.title AS paper,
  q."QuestionNumber" AS number,
  o."OptionKey" AS key,
  LEFT(o."Text", 80) AS text_preview
FROM "ListeningQuestionOptions" o
JOIN "ListeningQuestions" q ON q."Id" = o."ListeningQuestionId"
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
WHERE o."Text" ~* 'PAGE\s+\d+'
   OR o."Text" ~* 'Practice Test'
   OR o."Text" ~* E'\uF0B7'
ORDER BY p.title, q."QuestionNumber";

\echo '=== 5) Per-paper Part B/C counts vs expected (B=6, C1=6, C2=6, A=12+12) ==='
SELECT
  p.title AS paper,
  COUNT(*) FILTER (WHERE part."PartCode" = 2) AS part_b_questions, -- adjust enum if needed
  COUNT(*) FILTER (WHERE part."PartCode" IN (3,4)) AS part_c_questions
FROM "ListeningQuestions" q
JOIN "ListeningParts" part ON part."Id" = q."ListeningPartId"
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
GROUP BY p.title
ORDER BY p.title;

\echo '=== 6) Orphan sentinel count by paper (for drill-down) ==='
SELECT p.title, COUNT(*) AS sentinel_count
FROM "ListeningQuestions" q
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
WHERE LOWER(TRIM(q."Stem")) = 'see pdf'
GROUP BY p.title
ORDER BY sentinel_count DESC;

\echo '=== 7) Empty prompts after cleaning (would render empty candidate card) ==='
SELECT p.title, q."QuestionNumber", LENGTH(TRIM(q."Stem")) AS len
FROM "ListeningQuestions" q
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
WHERE TRIM(q."Stem") = ''
ORDER BY p.title, q."QuestionNumber";
