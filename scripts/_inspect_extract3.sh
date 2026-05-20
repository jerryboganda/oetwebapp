#!/usr/bin/env bash
# Compare extract shapes between problem paper and a good one
for ID in b8e0e9def00a4dd192beb08a5121deb9 bd6c09a4ae7e4e20ba441e7fdb178750; do
  echo "=== $ID ==="
  docker exec -i oet-postgres psql -U oet_learner -d oet_learner -At -P pager=off <<SQL
SELECT jsonb_pretty(
  jsonb_build_object(
    'all_keys', (SELECT string_agg(k, ',') FROM jsonb_object_keys((("ExtractedTextJson"::jsonb) -> 'listeningExtracts') -> 0) k),
    'first_transcript_len', length(((("ExtractedTextJson"::jsonb) -> 'listeningExtracts') -> 0 ->> 'transcript')),
    'first_content_len', length(((("ExtractedTextJson"::jsonb) -> 'listeningExtracts') -> 0 ->> 'content')),
    'first_text_len', length(((("ExtractedTextJson"::jsonb) -> 'listeningExtracts') -> 0 ->> 'text'))
  )
) FROM "ContentPapers" WHERE "Id"='$ID';
SQL
done
