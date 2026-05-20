#!/usr/bin/env bash
set -e
docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<'SQL'
\d "ContentPaperAssets"
\d "ListeningExtractionDrafts"
SQL
