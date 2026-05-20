#!/usr/bin/env bash
set -uo pipefail
cd /opt/oetwebapp
set +u
source /opt/oetwebapp/scripts/admin/.envrc
set -u
node scripts/admin/retry-listening-tts.mjs --dry-run --paper-id bd6c09a4ae7e4e20ba441e7fdb178750 2>&1 | tail -50
