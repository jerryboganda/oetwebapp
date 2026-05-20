#!/bin/bash
docker exec -i oet-postgres psql -U oet_learner -d oet_learner <<'SQL'
\d "ListeningExtracts"
\d "ListeningParts"
SQL
