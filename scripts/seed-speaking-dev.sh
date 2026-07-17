#!/usr/bin/env bash
# Seed the Speaking module locally for development.
# Idempotent: re-running is safe.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

echo "[seed-speaking-dev] ensuring Postgres is reachable..."
if ! pg_isready -h 127.0.0.1 -p 5432 -U postgres >/dev/null 2>&1; then
  echo "  → not reachable. Start Postgres (docker compose up postgres) and retry." >&2
  exit 1
fi

echo "[seed-speaking-dev] applying EF Core migrations..."
dotnet ef database update --project backend/src/OetWithDrHesham.Api --no-build

echo "[seed-speaking-dev] running the API once to trigger seeders..."
# Seeders register on startup. We start the API in the background and shut it
# down after the health endpoint reports ready.
ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --project backend/src/OetWithDrHesham.Api --no-build --no-launch-profile -- \
    --urls "http://localhost:5199" \
    > /tmp/oet-seed-api.log 2>&1 &
api_pid=$!

trap 'kill "$api_pid" 2>/dev/null || true' EXIT

for _ in $(seq 1 60); do
  if curl -fsS http://localhost:5199/health >/dev/null 2>&1; then
    echo "[seed-speaking-dev] API ready — seeders done."
    break
  fi
  sleep 1
done

# TODO: when Agent H lands the explicit `--seed-speaking` CLI flag, swap the
# warm-start above for a one-shot seed CLI invocation so we do not need to
# keep the API process alive.

kill "$api_pid" 2>/dev/null || true
echo "[seed-speaking-dev] done."
