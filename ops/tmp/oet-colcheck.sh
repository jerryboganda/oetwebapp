#!/usr/bin/env bash
set +e
echo '=== Users row count ==='
timeout 20 docker exec oet-postgres psql -U oet_learner -d oet_learner -tAc "select count(*) from \"Users\";" 2>&1 || echo userscount_failed
echo '=== RuntimeSettings row count ==='
timeout 20 docker exec oet-postgres psql -U oet_learner -d oet_learner -tAc "select count(*) from \"RuntimeSettings\";" 2>&1 || echo rscount_failed
echo '=== Does LiveClassesAiRecordingProcessingEnabled column exist? (empty = MISSING) ==='
timeout 20 docker exec oet-postgres psql -U oet_learner -d oet_learner -tAc "select column_name from information_schema.columns where table_name='RuntimeSettings' and column_name='LiveClassesAiRecordingProcessingEnabled';" 2>&1 || echo colcheck_failed
echo '=== DONE ==='
