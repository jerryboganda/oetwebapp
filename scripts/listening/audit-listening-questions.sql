-- Listening Part B / C data-quality audit (spec §12)
-- Run on learner DB: psql $DATABASE_URL -f scripts/listening/audit-listening-questions.sql
-- Flags: See PDF sentinel, CPDF/PDF placeholder, Option A/B/C placeholders,
--        PAGE artifacts, Practice Test footers, Word bullet glyph, empty prompts

\pset border 2
\echo '=== 1) Sentinel prompts (See PDF / CPDF / View PDF) — should be 0 after fix ==='
SELECT
  p."Title" AS paper,
  q."QuestionNumber" AS number,
  q."ListeningPartId" AS part_id,
  LEFT(q."Stem", 80) AS stem_preview
FROM "ListeningQuestions" q
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
WHERE LOWER(TRIM(q."Stem")) IN ('see pdf','cpdf','pdf','view pdf')
ORDER BY p."Title", q."QuestionNumber";

\echo '=== 2) Placeholder options (Option A/B/C) — should be 0 ==='
SELECT
  p."Title" AS paper,
  q."QuestionNumber" AS number,
  o."OptionKey" AS key,
  o."Text" AS text
FROM "ListeningQuestionOptions" o
JOIN "ListeningQuestions" q ON q."Id" = o."ListeningQuestionId"
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
WHERE LOWER(TRIM(o."Text")) IN ('option a','option b','option c')
ORDER BY p."Title", q."QuestionNumber", o."OptionKey";

\echo '=== 3) Artifact lines in prompts (PAGE / Practice Test / ======) ==='
SELECT
  p."Title" AS paper,
  q."QuestionNumber" AS number,
  LEFT(q."Stem", 120) AS stem_preview
FROM "ListeningQuestions" q
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
WHERE q."Stem" ~* 'PAGE\s+\d+'
   OR q."Stem" ~* '======\s*PAGE'
   OR q."Stem" ~* 'Practice Test'
   OR q."Stem" ~* E'\uF0B7'
ORDER BY p."Title", q."QuestionNumber";

\echo '=== 4) Artifact lines in options ==='
SELECT
  p."Title" AS paper,
  q."QuestionNumber" AS number,
  o."OptionKey" AS key,
  LEFT(o."Text", 80) AS text_preview
FROM "ListeningQuestionOptions" o
JOIN "ListeningQuestions" q ON q."Id" = o."ListeningQuestionId"
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
WHERE o."Text" ~* 'PAGE\s+\d+'
   OR o."Text" ~* 'Practice Test'
   OR o."Text" ~* E'\uF0B7'
ORDER BY p."Title", q."QuestionNumber";

\echo '=== 5) Per-paper Part B/C counts vs expected (B=6, C1=6, C2=6, A=12+12) ==='
SELECT
  p."Title" AS paper,
  COUNT(*) FILTER (WHERE part."PartCode" = 2) AS part_b_questions, -- adjust enum if needed
  COUNT(*) FILTER (WHERE part."PartCode" IN (3,4)) AS part_c_questions
FROM "ListeningQuestions" q
JOIN "ListeningParts" part ON part."Id" = q."ListeningPartId"
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
GROUP BY p."Title"
ORDER BY p."Title";

\echo '=== 6) Orphan sentinel count by paper (for drill-down) ==='
SELECT p."Title", COUNT(*) AS sentinel_count
FROM "ListeningQuestions" q
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
WHERE LOWER(TRIM(q."Stem")) = 'see pdf'
GROUP BY p."Title"
ORDER BY sentinel_count DESC;

\echo '=== 7) Empty prompts after cleaning (would render empty candidate card) ==='
SELECT p."Title", q."QuestionNumber", LENGTH(TRIM(q."Stem")) AS len
FROM "ListeningQuestions" q
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
WHERE TRIM(q."Stem") = ''
ORDER BY p."Title", q."QuestionNumber";

-- 8) Stray watermark letters. The question papers' diagonal "SAMPLE"/"BLANK"
--    watermark used to be extracted glyph by glyph into nearby lines, leaving
--    lone capitals in Part B/C stems and options. This is a COARSE net (it also
--    lists legit "vitamin A", "Plan B" ...); the authoritative, allow-listed
--    check is GET /v1/admin/listening/part-bc/watermark-audit. Should be 0
--    rows (after eyeballing legit labels) once the repair is applied.
\echo '=== 8a) Lone watermark letters in relational Part B/C stems and options ==='
SELECT p."Title" AS paper, q."QuestionNumber" AS number, 'stem' AS field, q."Stem" AS text
FROM "ListeningQuestions" q
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
WHERE p."Status" = 4 /* ContentStatus.Published */ AND q."QuestionNumber" BETWEEN 25 AND 42
  AND q."Stem" ~ '(^|\s)[SMPLEBNK](\s|$)|[a-z,;]\s+A\s+[a-z]'
UNION ALL
SELECT p."Title", q."QuestionNumber", 'option' || o."OptionKey", o."Text"
FROM "ListeningQuestionOptions" o
JOIN "ListeningQuestions" q ON q."Id" = o."ListeningQuestionId"
JOIN "ContentPapers" p ON p."Id" = q."PaperId"
WHERE p."Status" = 4 /* ContentStatus.Published */ AND q."QuestionNumber" BETWEEN 25 AND 42
  AND o."Text" ~ '\s[SMPLEBNK](\s|$)|[a-z,;]\s+A\s+[a-z]'
ORDER BY 1, 2, 3;

\echo '=== 8b) Same check on the authored JSON projection (what learners are served) ==='
SELECT p."Title" AS paper, (item->>'number')::int AS number,
       COALESCE(item->>'stem', item->>'text') AS stem, item->'options' AS options
FROM "ContentPapers" p
CROSS JOIN LATERAL jsonb_array_elements(
  CASE WHEN p."ExtractedTextJson" LIKE '{%'
        AND jsonb_typeof(p."ExtractedTextJson"::jsonb -> 'listeningQuestions') = 'array'
       THEN p."ExtractedTextJson"::jsonb -> 'listeningQuestions' ELSE '[]'::jsonb END) AS item
WHERE p."SubtestCode" = 'listening' AND p."Status" = 4 /* ContentStatus.Published */
  AND (item->>'number') ~ '^\d+$' AND (item->>'number')::int BETWEEN 25 AND 42
  AND (COALESCE(item->>'stem', item->>'text') ~ '(^|\s)[SMPLEBNK](\s|$)|[a-z,;]\s+A\s+[a-z]'
       OR (item->'options')::text ~ '\s[SMPLEBNK](\s|")|[a-z,;]\s+A\s+[a-z]')
ORDER BY 1, 2;
