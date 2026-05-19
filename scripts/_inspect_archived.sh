#!/usr/bin/env bash
docker exec oet-postgres psql -U oet_learner -d oet_learner -c "SELECT \"Id\",\"Title\",\"Status\",\"CreatedAt\",\"UpdatedAt\",\"ArchivedAt\" FROM \"ContentPapers\" WHERE \"SubtestCode\"='listening' AND \"Status\"=6"
