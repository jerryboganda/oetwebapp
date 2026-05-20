#!/usr/bin/env bash
echo ===TOTAL-LISTENING===
docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<'SQL'
SELECT COUNT(*) AS total FROM "ContentPapers" WHERE "SubtestCode"='listening';
SELECT "Status", COUNT(*) FROM "ContentPapers" WHERE "SubtestCode"='listening' GROUP BY "Status" ORDER BY "Status";
SQL

echo ===SWEEP-PAPER-STATUS===
docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<'SQL'
SELECT cp."Slug", cp."Id", cp."Status",
  (SELECT COUNT(*) FROM "ContentPaperAssets" WHERE "PaperId"=cp."Id" AND "Role"=0) AS audio_rows
FROM "ContentPapers" cp
WHERE cp."Id" IN (
  '729fb5f093354b8388cf56ba24a472c1',
  '8d48332f661b40f7beb8af86077ee6c9',
  '01ca161ecbfd4c8c949ccc928856dc6d',
  'cd17cff04b9d40e486035bf2f90411ea',
  'a15fa71d25da4978af1f9e43b970c11e',
  '708aac3272c04cbfad4ef8cf0ce684ea',
  '10606c9cc66a43c5ac31d9bef7c415eb',
  '01b7124837604925aac0b0d8e3dedc75',
  '5ac4716e2a2b4defb5fd8667d55d3023',
  'd7ab6b4a4ecc45b28d7cb3186b36842f',
  'dabaf1c3067542168080c04587086a16'
)
ORDER BY cp."Slug";
SQL

echo ===LAST-20-SWEEP-LOG===
tail -20 /opt/oetwebapp/sweep.log

echo ===EXIT-CAUSE-LINES===
grep -nE 'fatal|FATAL|abort|Error:|ECONNRESET|TypeError|ReferenceError|throw' /opt/oetwebapp/sweep.log | tail -10
