#!/usr/bin/env bash
# Removes ghcr.io/jerryboganda/oetwebapp-* image tags that no container
# (running or stopped) references once they are older than the retention
# window. Caps unbounded disk growth from per-SHA production deploys while
# keeping a rollback window of recent SHAs.
#
# NEVER prunes the Owner Agent Console images (oetwebapp-agent-console,
# -agent-console-egress, -agent-console-dockerproxy): a pending console update
# can sit pulled-but-unused for days while agent turns run, and
# .github/workflows/agent-console.yml keeps its own three-tag rollback window.
set -euo pipefail

RETENTION_HOURS="${1:-24}"
used="$(docker ps -a --format '{{.Image}}' | sort -u)"
removed=0

while read -r tag; do
  [ -z "$tag" ] && continue
  case ",$used," in *",$tag,"*) continue ;; esac
  created="$(docker inspect -f '{{.Created}}' "$tag" 2>/dev/null || true)"
  [ -z "$created" ] && continue
  age_hours=$(( ($(date +%s) - $(date -d "$created" +%s)) / 3600 ))
  if [ "$age_hours" -ge "$RETENTION_HOURS" ]; then
    if docker rmi "$tag" >/dev/null 2>&1; then
      removed=$((removed + 1))
    fi
  fi
done < <(docker images --format '{{.Repository}}:{{.Tag}}' \
  | grep '^ghcr\.io/jerryboganda/oetwebapp-' \
  | grep -v '^ghcr\.io/jerryboganda/oetwebapp-agent-console' \
  | grep -v ':latest$' || true)

docker image prune -f >/dev/null 2>&1 || true
echo "[prune-stale-images] removed $removed stale oetwebapp tag(s) older than ${RETENTION_HOURS}h"
