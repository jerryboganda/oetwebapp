#!/usr/bin/env bash
# Post-deploy verification — run on the VPS
set -euo pipefail

APP_DIR="${VPS_APP_DIR:-/opt/oetwebapp}"
cd "$APP_DIR"

read_env_value() {
  local key="$1"
  local line value
  line=$(grep -E "^${key}=" .env.production | tail -n 1)
  value="${line#*=}"
  value="${value%\"}"
  value="${value#\"}"
  value="${value%\'}"
  value="${value#\'}"
  printf '%s' "$value"
}

POSTGRES_USER=$(read_env_value POSTGRES_USER)
POSTGRES_DB=$(read_env_value POSTGRES_DB)
APP_PUBLIC_URL="${APP_PUBLIC_URL:-https://app.oetwithdrhesham.co.uk}"
API_PUBLIC_URL="${API_PUBLIC_URL:-https://api.oetwithdrhesham.co.uk}"
export POSTGRES_USER POSTGRES_DB

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

echo "--- API readiness (internal direct 2xx) ---"
docker exec oet-web wget -qO- --timeout=5 http://learner-api:8080/health/ready | head -3
echo ""

echo "--- Web /api/health (internal direct 2xx) ---"
docker exec oet-web wget -qO- --timeout=5 http://localhost:3000/api/health 2>&1 | head -3
echo ""

echo "--- Last 30 lines of api logs (post-startup) ---"
docker logs oet-api --tail 30 2>&1 | grep -vE 'Information|Debug' | head -40 || true
echo ""

echo "--- Public reachability (via Nginx Proxy Manager) ---"
curl --fail --show-error --silent --max-time 15 -o /dev/null "${APP_PUBLIC_URL%/}/api/health"
curl --fail --show-error --silent --max-time 15 -o /dev/null "${API_PUBLIC_URL%/}/health/ready"
echo ""

echo "=== VERIFICATION_DONE ==="
