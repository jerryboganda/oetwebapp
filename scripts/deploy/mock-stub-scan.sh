#!/usr/bin/env bash
# Fail closed when production config resolves to mock/stub/noop placeholders.
set -euo pipefail

ENV_FILE="${1:-.env.production}"
if [ ! -f "$ENV_FILE" ]; then
  echo "[mock-scan] missing $ENV_FILE" >&2
  exit 1
fi

failed=0
if awk -F= '
  /^[[:space:]]*($|#)/ { next }
  /^[A-Za-z_][A-Za-z0-9_]*=/ {
    value = $0
    sub(/^[^=]*=/, "", value)
    lower = tolower(value)
    if (lower ~ /(^|[^a-z0-9])(mock|stub|noop|__placeholder__)([^a-z0-9]|$)/) print FNR ":" $1
  }
' "$ENV_FILE" >/tmp/oet-mock-stub-env.txt && [ -s /tmp/oet-mock-stub-env.txt ]; then
  echo "[mock-scan] production env contains mock/stub/noop placeholder values:" >&2
  sed 's/^/[mock-scan]   line /' /tmp/oet-mock-stub-env.txt >&2
  failed=1
fi
rm -f /tmp/oet-mock-stub-env.txt

if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  if docker compose --env-file "$ENV_FILE" -f docker-compose.production.yml config >/tmp/oet-compose-production-config.yml 2>/tmp/oet-compose-production-config.err; then
    if grep -inE '(^|[^[:alnum:]_])(mock|stub|noop|__placeholder__)([^[:alnum:]_]|$)' /tmp/oet-compose-production-config.yml >/tmp/oet-compose-mock-stub.txt; then
      echo "[mock-scan] rendered production compose config contains mock/stub/noop markers:" >&2
      sed 's/^/[mock-scan]   /' /tmp/oet-compose-mock-stub.txt >&2
      failed=1
    fi
  else
    echo "[mock-scan] could not render docker-compose.production.yml:" >&2
    sed 's/^/[mock-scan]   /' /tmp/oet-compose-production-config.err >&2 || true
    failed=1
  fi
fi
rm -f /tmp/oet-compose-production-config.yml /tmp/oet-compose-production-config.err /tmp/oet-compose-mock-stub.txt

if [ "$failed" -ne 0 ]; then
  exit 1
fi

echo "[mock-scan] production config contains no mock/stub/noop markers"