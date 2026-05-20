#!/usr/bin/env bash
docker exec -i oet-postgres psql -U oet_learner -d oet_learner -At -P pager=off <<'SQL'
SELECT jsonb_pretty(
  jsonb_build_object(
    'extract_count', jsonb_array_length(("ExtractedTextJson"::jsonb) -> 'listeningExtracts'),
    'first_keys', (SELECT string_agg(k, ',') FROM jsonb_object_keys((("ExtractedTextJson"::jsonb) -> 'listeningExtracts') -> 0) k),
    'first_transcript_len', length(((("ExtractedTextJson"::jsonb) -> 'listeningExtracts') -> 0 ->> 'transcript')),
    'first_part_code', ((("ExtractedTextJson"::jsonb) -> 'listeningExtracts') -> 0 ->> 'partCode'),
    'all_codes', (SELECT string_agg(p ->> 'partCode', ',') FROM jsonb_array_elements(("ExtractedTextJson"::jsonb) -> 'listeningExtracts') p)
  )
) FROM "ContentPapers" WHERE "Id"='b8e0e9def00a4dd192beb08a5121deb9';
SQL
