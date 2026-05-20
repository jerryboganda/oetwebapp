#!/bin/bash
echo "=== Sweep #2 status ==="
ps -p 1626613 -o pid,etime,cmd 2>/dev/null || echo "sweep finished"
echo
echo "=== Tail sweep log ==="
tail -20 /opt/oetwebapp/sweep-puredo.log 2>/dev/null || echo "no log"
echo
echo "=== Orphan zero-text Drafts check ==="
docker exec oet-postgres bash -c '
psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
SELECT \"Id\", \"Slug\", \"Status\", LENGTH(COALESCE(\"ExtractedTextJson\",'\'''\''))  AS text_len
FROM \"ContentPapers\"
WHERE \"Id\" IN ('\''06ed32dd'\'','\''16203e2a'\'','\''51900b72'\'','\''b8e0e9de'\'')
   OR \"Id\" LIKE '\''06ed32dd%'\''
   OR \"Id\" LIKE '\''16203e2a%'\''
   OR \"Id\" LIKE '\''51900b72%'\''
   OR \"Id\" LIKE '\''b8e0e9de%'\'';
"
'
