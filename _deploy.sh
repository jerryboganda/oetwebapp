#!/usr/bin/env bash
set -euo pipefail
cd /opt/oetwebapp
echo "=== current branch & dirty state ==="
git branch --show-current
git status --short | head -30
TS=$(date +%s)
BACKUP=/opt/oetwebapp-pre-deploy-backup-$TS
mkdir -p "$BACKUP"
echo "=== stashing modified tracked files ==="
git stash push -u -m "pre-deploy-$TS" -- \
  backend/src/OetLearner.Api/Domain/ContentPaperEntities.cs \
  backend/src/OetLearner.Api/Program.cs \
  backend/src/OetLearner.Api/Services/Content/ContentPaperService.cs \
  backend/src/OetLearner.Api/Services/Content/UploadSecurity.cs \
  backend/src/OetLearner.Api/Services/Grammar/GrammarDraftService.cs \
  backend/src/OetLearner.Api/Services/Listening/ListeningBackfillService.cs \
  backend/src/OetLearner.Api/Services/Rulebook/AiUsageRecorder.cs \
  scripts/deploy/nginx/api-bluegreen.conf.template || echo "(nothing to stash)"
echo "=== moving untracked files to $BACKUP ==="
for f in scripts/_fix_listening.sh scripts/admin/_lib.mjs scripts/admin/bulk-backfill.mjs scripts/admin/generate-conversation.mjs scripts/admin/generate-grammar.mjs scripts/admin/generate-listening.mjs scripts/admin/generate-mocks.mjs scripts/admin/generate-pronunciation.mjs scripts/admin/generate-reading.mjs scripts/admin/generate-speaking-assets.mjs scripts/admin/generate-speaking.mjs scripts/admin/launch-all-live.sh scripts/admin/publish-vocab.mjs scripts/admin/republish-listening-drafts.mjs scripts/admin/retry-listening-tts.mjs scripts/admin/run-bulk.sh scripts/admin/seed-rulebooks.mjs scripts/admin/status-all-live.sh; do
  if [ -f "$f" ]; then
    mkdir -p "$BACKUP/$(dirname $f)"
    mv "$f" "$BACKUP/$f"
  fi
done
echo "=== fetch + checkout ==="
git fetch origin
git checkout cleanup/remove-demo-dummy-seed-placeholder-data
git pull origin cleanup/remove-demo-dummy-seed-placeholder-data
echo "=== restoring stash ==="
git stash pop || echo "(nothing to pop)"
grep -q '^RateLimit__PerUserPermitLimit=' .env.production || echo 'RateLimit__PerUserPermitLimit=100000' >> .env.production
grep -q '^RateLimit__PerUserWritePermitLimit=' .env.production || echo 'RateLimit__PerUserWritePermitLimit=20000' >> .env.production
echo "=== git HEAD ==="
git log --oneline -3
echo "=== deploy ==="
DEPLOY_REF=$(git rev-parse HEAD) bash scripts/deploy/deploy-direct.sh 2>&1 | tail -60
echo "=== health ==="
sleep 8
curl -fsS -o /dev/null -w 'HTTP %{http_code}\n' https://api.oetwithdrhesham.co.uk/health || true
docker ps --format '{{.Names}}: {{.Status}}' | grep -E 'oet-(api|web)' || true
echo "BACKUP_DIR=$BACKUP"
