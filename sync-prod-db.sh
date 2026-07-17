#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# sync-prod-db.sh — Fetch the latest PRODUCTION database and load it into the
# local Podman postgres container. Used both for one-off runs and the hourly
# scheduled task.
#
#   Production:  root@185.252.233.186 (ssh alias "vps", MAIN prod) -> docker
#                container "oet-postgres" (db oet_with_dr_hesham, user oet_with_dr_hesham).
#                (Was OLD 68.183.32.122/"oet-dev" before the 2026-06-05 cutover.)
#   Local:       podman container "oet-hotreload-postgres"
#                (db oet_with_dr_hesham, user oet_user)
#
# SAFETY: aborts before touching the local DB if the prod dump is missing or
# suspiciously small, so a failed/empty dump can never wipe local data.
#
# WARNING: this REPLACES the local oet_with_dr_hesham database every run. Any local
# changes are lost. The dump contains REAL PRODUCTION DATA (PII) — the .db-sync/
# folder is gitignored; never commit it.
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

# Git Bash mangles leading-slash args to native .exe (e.g. /tmp -> C:\...\Temp).
# We pipe via stdin and use no container-path args, but disable conversion to be safe.
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL="*"

REMOTE="${REMOTE:-vps}"
REMOTE_CONTAINER="${REMOTE_CONTAINER:-oet-postgres}"
LOCAL_CONTAINER="${LOCAL_CONTAINER:-oet-hotreload-postgres}"
LOCAL_DB="${LOCAL_DB:-oet_with_dr_hesham}"
LOCAL_USER="${LOCAL_USER:-oet_user}"
KEEP_DUMPS="${KEEP_DUMPS:-5}"
MIN_DUMP_BYTES="${MIN_DUMP_BYTES:-500000}"   # prod compressed dump is ~5.7MB; guard well below

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SYNC_DIR="$SCRIPT_DIR/.db-sync"
LOG="$SYNC_DIR/sync.log"
mkdir -p "$SYNC_DIR"

log(){ echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" | tee -a "$LOG"; }
fail(){ log "ERROR: $*"; exit 1; }

TS="$(date '+%Y%m%d_%H%M%S')"
DUMP="$SYNC_DIR/prod_${TS}.dump"

log "════════ Sync start (${TS}) ════════"

# 0. Preconditions
command -v podman >/dev/null 2>&1 || fail "podman not on PATH"
command -v ssh    >/dev/null 2>&1 || fail "ssh not on PATH"
podman ps --format '{{.Names}}' 2>/dev/null | grep -qx "$LOCAL_CONTAINER" \
  || fail "local container '$LOCAL_CONTAINER' not running (start Podman + the stack first)"

# 1. Dump prod (custom format) straight to a local file (bash pipe is binary-safe)
log "Dumping prod '$REMOTE_CONTAINER' via ssh '$REMOTE'..."
ssh -o ConnectTimeout=15 -o BatchMode=yes "$REMOTE" \
  'docker exec '"$REMOTE_CONTAINER"' sh -c "PGPASSWORD=\$POSTGRES_PASSWORD pg_dump -U \$POSTGRES_USER -d \$POSTGRES_DB -Fc"' \
  > "$DUMP" 2>>"$LOG"
RC=$?
[ $RC -eq 0 ] || fail "pg_dump/ssh failed (rc=$RC); local DB untouched"

SZ=$(wc -c < "$DUMP" 2>/dev/null || echo 0)
[ "$SZ" -ge "$MIN_DUMP_BYTES" ] || fail "dump too small ($SZ bytes < $MIN_DUMP_BYTES); local DB untouched"
log "Dump OK: $SZ bytes -> $(basename "$DUMP")"

# 2. (dump stays on host; we stream it into the container via stdin in step 4 —
#     this avoids passing any container-side path, sidestepping Git Bash path
#     mangling and keeping the transfer binary-safe.)

# 3. Terminate live connections, drop + recreate the DB (atomic-ish swap)
log "Recreating local DB '$LOCAL_DB'..."
podman exec "$LOCAL_CONTAINER" psql -U "$LOCAL_USER" -d postgres -v ON_ERROR_STOP=1 -c \
  "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='${LOCAL_DB}' AND pid<>pg_backend_pid();" >/dev/null 2>>"$LOG" \
  || log "WARN: terminate-connections returned nonzero (continuing)"
podman exec "$LOCAL_CONTAINER" psql -U "$LOCAL_USER" -d postgres -v ON_ERROR_STOP=1 -c \
  "DROP DATABASE IF EXISTS ${LOCAL_DB};" >/dev/null 2>>"$LOG" \
  || fail "DROP DATABASE failed (local DB may still be intact)"
podman exec "$LOCAL_CONTAINER" psql -U "$LOCAL_USER" -d postgres -v ON_ERROR_STOP=1 -c \
  "CREATE DATABASE ${LOCAL_DB} OWNER ${LOCAL_USER};" >/dev/null 2>>"$LOG" \
  || fail "CREATE DATABASE failed"

# 4. Restore by streaming the dump into the container via stdin (binary-safe).
log "Restoring dump into '$LOCAL_DB'..."
podman exec -i "$LOCAL_CONTAINER" pg_restore --no-owner --no-acl \
  -U "$LOCAL_USER" -d "$LOCAL_DB" < "$DUMP" >>"$LOG" 2>&1
RRC=$?
if [ $RRC -ne 0 ]; then
  log "WARN: pg_restore exited $RRC (often benign for extensions/comments); verifying..."
fi

# 5. Verify row/table presence
TABLES=$(podman exec "$LOCAL_CONTAINER" psql -U "$LOCAL_USER" -d "$LOCAL_DB" -tAc \
  "SELECT count(*) FROM information_schema.tables WHERE table_schema='public';" 2>>"$LOG" | tr -d '[:space:]')
[ -n "$TABLES" ] && [ "$TABLES" -gt 0 ] 2>/dev/null \
  || fail "verification failed: no public tables after restore"
log "Verified: $TABLES public tables present."

# 6. Retention (keep newest KEEP_DUMPS dumps for rollback)
ls -1t "$SYNC_DIR"/prod_*.dump 2>/dev/null | tail -n +"$((KEEP_DUMPS+1))" | xargs -r rm -f
log "════════ Sync OK — local '$LOCAL_DB' now matches production (kept last $KEEP_DUMPS dumps) ════════"
