#!/usr/bin/env bash
# Install host-level protection for OET production data.
# Safe to re-run. Does not recreate containers. Does not touch volume contents.
#
# Blocks: docker volume rm/prune of OET volumes, docker system prune --volumes,
# docker compose down -v. Application content is deleted only via the admin UI.
set -euo pipefail

REAL_DOCKER="${REAL_DOCKER:-/usr/bin/docker}"
WRAPPER="${WRAPPER:-/usr/local/bin/docker}"
LIST_DIR=/etc/oet
LIST_FILE="$LIST_DIR/protected-volumes"
LOG_FILE=/var/log/oet-volume-protect.log
PROFILE_FILE=/etc/profile.d/oet-protect-data.sh

PROTECTED_VOLUMES=(
  oetwebsite_oet_postgres_data
  oetwebsite_oet_learner_storage
  oetwebsite_oet_db_backups
  oetwebsite_oet_clamav_data
  oetwebsite_oet_with_dr_hesham_storage
)

if [ ! -x "$REAL_DOCKER" ]; then
  echo "protect-production-data: $REAL_DOCKER not found" >&2
  exit 1
fi

mkdir -p "$LIST_DIR"
printf '%s\n' "${PROTECTED_VOLUMES[@]}" > "$LIST_FILE"
chmod 644 "$LIST_FILE"

for vol in "${PROTECTED_VOLUMES[@]}"; do
  if "$REAL_DOCKER" volume inspect "$vol" >/dev/null 2>&1; then
    "$REAL_DOCKER" volume inspect "$vol" >/dev/null
  fi
done

cat > "$WRAPPER" <<'WRAP'
#!/usr/bin/env bash
# OET production docker wrapper. Delegates to /usr/bin/docker except when the
# command would delete protected named volumes.
set -u
REAL=/usr/bin/docker
LIST=/etc/oet/protected-volumes
LOG=/var/log/oet-volume-protect.log

log_block() {
  local msg="$1"
  printf '%s BLOCKED %s -- %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" "$msg" >> "$LOG" 2>/dev/null || true
  echo "BLOCKED: $msg" >&2
  echo "OET production data is independent of containers. Delete content from the admin panel only." >&2
}

is_protected() {
  local name="$1"
  [ -z "$name" ] && return 1
  [ -f "$LIST" ] || return 1
  grep -qxF "$name" "$LIST"
}

args=("$@")
cmd="${1:-}"
sub="${2:-}"

if [ "$cmd" = "volume" ] && [ "$sub" = "prune" ]; then
  log_block "docker volume prune is forbidden on this host"
  exit 99
fi

if [ "$cmd" = "volume" ] && [ "$sub" = "rm" ]; then
  for a in "${args[@]}"; do
    case "$a" in
      -*|volume|rm) continue ;;
    esac
    if is_protected "$a"; then
      log_block "refusing to delete protected volume $a"
      exit 99
    fi
  done
fi

if [ "$cmd" = "system" ] && [ "$sub" = "prune" ]; then
  for a in "${args[@]}"; do
    if [ "$a" = "--volumes" ]; then
      log_block "docker system prune --volumes is forbidden on this host"
      exit 99
    fi
  done
fi

if [ "$cmd" = "compose" ]; then
  is_down=0
  has_vol=0
  for a in "${args[@]}"; do
    [ "$a" = "down" ] && is_down=1
    if [ "$a" = "-v" ] || [ "$a" = "--volumes" ]; then
      has_vol=1
    fi
  done
  if [ "$is_down" -eq 1 ] && [ "$has_vol" -eq 1 ]; then
    log_block "docker compose down -v is forbidden (OET volumes stay)"
    exit 99
  fi
fi

exec "$REAL" "$@"
WRAP
chmod 755 "$WRAPPER"

cat > "$PROFILE_FILE" <<'EOF'
# OET: never delete named volumes. Content is removed from the admin UI only.
alias docker-compose-down-v='echo BLOCKED: compose down -v is forbidden; false'
EOF
chmod 644 "$PROFILE_FILE"

touch "$LOG_FILE"
chmod 644 "$LOG_FILE"

echo "protect-production-data: wrapper $WRAPPER -> $REAL_DOCKER"
echo "protect-production-data: list $LIST_FILE"
echo "protect-production-data: OET volumes cannot be removed by rebuild, prune, or compose down -v"
