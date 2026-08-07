#!/usr/bin/env bash
# Apply an idempotent EF SQL stream to the production PostgreSQL container.
# The workflow generates the SQL on GitHub Actions; this wrapper performs only
# the data-local psql execution and never prints credentials or SQL contents.
set -euo pipefail

APP_DIR="${VPS_APP_DIR:-/opt/oetwebapp}"
ENV_FILE="$APP_DIR/.env.production"
if [ ! -f "$ENV_FILE" ]; then
  echo "[migration] missing production environment file: $ENV_FILE" >&2
  exit 1
fi

read_env_value() {
  local key="$1"
  local line value
  line=$(grep -E "^${key}=" "$ENV_FILE" | tail -n 1 || true)
  if [ -z "$line" ]; then
    return 1
  fi
  value="${line#*=}"
  value="${value%\"}"
  value="${value#\"}"
  value="${value%\'}"
  value="${value#\'}"
  printf '%s' "$value"
}

postgres_user="$(read_env_value POSTGRES_USER || true)"
postgres_db="$(read_env_value POSTGRES_DB || true)"
if [ -z "$postgres_user" ] || [ -z "$postgres_db" ]; then
  echo "[migration] POSTGRES_USER and POSTGRES_DB are required in .env.production." >&2
  exit 1
fi

sql_file="$(mktemp /tmp/oet-production-migrations.XXXXXX.sql)"
trap 'rm -f "$sql_file"' EXIT
cat > "$sql_file"
if [ ! -s "$sql_file" ]; then
  echo "[migration] refusing to apply an empty SQL stream." >&2
  exit 1
fi

docker exec -i oet-postgres \
  psql -v ON_ERROR_STOP=1 -U "$postgres_user" -d "$postgres_db" < "$sql_file"
