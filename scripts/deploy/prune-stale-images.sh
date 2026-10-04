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
used="$(docker ps -aq | xargs -r docker inspect -f '{{.Image}}' | sort -u)"
removed=0

while read -r tag image_id; do
  [ -z "$tag" ] && continue
  # Serving containers use immutable digest refs, not the per-release aliases.
  if printf '%s\n' "$used" | grep -Fxq "$image_id"; then continue; fi
  created="$(docker image inspect -f '{{.Created}}' "$image_id" 2>/dev/null || true)"
  [ -z "$created" ] && continue
  age_hours=$(( ($(date +%s) - $(date -d "$created" +%s)) / 3600 ))
  if [ "$age_hours" -ge "$RETENTION_HOURS" ]; then
    if docker rmi "$tag" >/dev/null 2>&1; then
      removed=$((removed + 1))
    fi
  fi
done < <(docker images --no-trunc --format '{{.Repository}}:{{.Tag}} {{.ID}}' \
  | grep '^ghcr\.io/jerryboganda/oetwebapp-' \
  | grep -v '^ghcr\.io/jerryboganda/oetwebapp-agent-console' \
  | grep -v ':latest ' || true)

echo "[prune-stale-images] removed $removed stale oetwebapp tag(s) older than ${RETENTION_HOURS}h"
