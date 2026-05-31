#!/usr/bin/env bash
set -e
docker exec oet-postgres psql -U oet_learner -d oet_learner -tA -c "select count(*) from \"Users\";"
