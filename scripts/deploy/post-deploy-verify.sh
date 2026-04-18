#!/usr/bin/env bash
# Post-deploy verification — run on the VPS
set -euo pipefail

cd /root/oetwebsite

# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

echo "=== POST_DEPLOY_VERIFICATION ==="
echo ""

echo "--- HEAD now at ---"
git rev-parse HEAD
git log --oneline -1
echo ""

echo "--- Migration count AFTER ---"
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At \
  -c 'SELECT COUNT(*) FROM "__EFMigrationsHistory";'
echo ""

echo "--- Last 10 migrations applied (newest first) ---"
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At \
  -c 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 10;'
echo ""

echo "--- New AI usage tables present? ---"
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At \
  -c "SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename ~ '^(AiUsageRecords|AiQuotaPlans|AiQuotaCounters|AiUserQuotaOverrides|AiGlobalPolicies|UserAiCredentials|UserAiPreferences|AiProviders|AiCreditLedger)$' ORDER BY tablename;"
echo ""

echo "--- New Content Upload tables present? ---"
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At \
  -c "SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename ~ '^(ContentPapers|ContentPaperAssets|AdminUploadSessions)$' ORDER BY tablename;"
echo ""

echo "--- New Reading tables present? ---"
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At \
  -c "SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename ~ '^(ReadingParts|ReadingTexts|ReadingQuestions|ReadingAttempts|ReadingAnswers|ReadingPolicies|ReadingUserPolicyOverrides)$' ORDER BY tablename;"
echo ""

echo "--- Seeded singletons? ---"
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At \
  -c 'SELECT (SELECT COUNT(*) FROM "AiQuotaPlans") AS "ai_plans", (SELECT COUNT(*) FROM "AiGlobalPolicies") AS "ai_global", (SELECT COUNT(*) FROM "AiProviders") AS "ai_providers";'
echo ""

echo "--- MediaAssets columns added (Sha256, MediaKind)? ---"
docker exec oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At \
  -c "SELECT column_name FROM information_schema.columns WHERE table_name='MediaAssets' AND column_name IN ('Sha256','MediaKind') ORDER BY column_name;"
echo ""

echo "--- API health (internal) ---"
docker exec oet-web wget -qO- --timeout=5 http://oet-api:8080/health 2>&1 | head -3 || echo "(api /health endpoint not present — checking root)"
echo ""

echo "--- Web /api/health (the docker healthcheck endpoint) ---"
docker exec oet-web wget -qO- --timeout=5 http://localhost:3000/api/health 2>&1 | head -3
echo ""

echo "--- Last 30 lines of api logs (post-startup) ---"
docker logs oet-api --tail 30 2>&1 | grep -vE 'Information|Debug' | head -40 || true
echo ""

echo "--- Public reachability (via Nginx Proxy Manager) ---"
curl -s -o /dev/null -w 'app status: %{http_code} | api status: %{http_code}\n' https://app.oetwithdrhesham.co.uk/api/health
curl -s -o /dev/null -w 'api  status: %{http_code}\n' https://api.oetwithdrhesham.co.uk/health 2>&1 || echo "(api domain probe failed)"
echo ""

echo "=== VERIFICATION_DONE ==="
