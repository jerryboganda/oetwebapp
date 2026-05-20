#!/bin/bash
set -e
docker exec oet-postgres bash -c '
  psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
    SELECT \"Id\", \"Slug\", \"Status\", \"SubtestCode\"
    FROM \"ContentPapers\"
    WHERE \"Slug\" ILIKE '\''%dietetics%024%'\''
    LIMIT 10;
  "
'
