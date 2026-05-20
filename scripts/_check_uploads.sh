#!/bin/bash
set -e
echo === recent admin upload sessions ===
docker exec -e PGPASSWORD="$PGPASS" oet-postgres psql -U oet_learner -d oet_learner -c "SELECT \"Id\",\"State\",\"PartsReceived\",\"TotalParts\",\"ReceivedBytes\",\"DeclaredSizeBytes\",substring(\"OriginalFilename\",1,40) as fn,substring(\"AdminUserId\",1,20) as admin,\"CreatedAt\" FROM \"AdminUploadSessions\" ORDER BY \"CreatedAt\" DESC LIMIT 10;"
echo
echo === staging dir tree ===
docker exec oet-api-green find /var/opt/oet-learner/storage/uploads/staging/ -ls 2>&1 | head -30
echo
echo === api recent errors ===
docker logs --tail 800 oet-api-green 2>&1 | grep -E 'staged|Upload|quarantin|scanner|Magic|extension' | tail -30
echo
echo === clamav status ===
docker exec oet-api-green env | grep -iE 'upload|scan|clamav' 2>&1 | head -20
