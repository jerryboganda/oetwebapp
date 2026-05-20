#!/bin/bash
# Match each unhandled exception to its session id to see which sessions fail validator
docker logs --since 2h oet-api-green 2>&1 | awk '
/POST .v1.admin.uploads.[a-f0-9]+.complete/ {
  match($0, /uploads\/[a-f0-9]+\//);
  sid = substr($0, RSTART+8, RLENGTH-9);
  next
}
/Unhandled exception while processing POST.*uploads.*complete/ {
  match($0, /uploads\/[a-f0-9]+\/complete/);
  sid = substr($0, RSTART+8, 32);
}
/Upload rejected by content validator/ {
  match($0, /content validator: .*/);
  reason = substr($0, RSTART+19);
  print sid, "|", reason
}
' | sort -u | tail -20
echo
echo === recent successful uploads ===
docker logs --since 4h oet-api-green 2>&1 | grep -E 'deduplicated|MediaAsset' | tail -10
echo
echo === inspect deployed UploadSecurity binary for text-file branch ===
docker exec oet-api-green strings /app/OetLearner.Api.dll 2>&1 | grep -E 'Unrecognised|printable|text/plain|text/markdown' | head -10
echo
echo === check build time of dll ===
docker exec oet-api-green ls -la /app/OetLearner.Api.dll
docker exec oet-api-green stat /app/OetLearner.Api.dll 2>&1 | head -10
