#!/bin/bash
USER=$(docker exec oet-postgres bash -c 'echo $POSTGRES_USER')
DB=$(docker exec oet-postgres bash -c 'echo $POSTGRES_DB')
docker exec oet-postgres psql -U "$USER" -d "$DB" -c "update \"ContentPapers\" set \"SourceProvenance\"='ai-curated-opus47' where \"SubtestCode\"='speaking' and \"Status\"=0 and length(\"SourceProvenance\")>32 returning \"Id\", \"SourceProvenance\";"
