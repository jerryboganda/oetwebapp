#!/bin/bash
set -e
cd /opt/oetwebapp
BACKUP=/opt/oetwebapp-backups/scripts-admin-vps-pre-6ea79008
mkdir -p "$BACKUP"
# Back up and remove every untracked file in scripts/admin/ to allow checkout.
for f in $(git ls-files --others --exclude-standard scripts/admin/ 2>/dev/null); do
  mkdir -p "$BACKUP/$(dirname "$f")"
  cp -p "$f" "$BACKUP/$f" 2>/dev/null || true
  rm -f "$f"
done
echo "BACKED_UP_TO=$BACKUP"
ls "$BACKUP/scripts/admin/" 2>/dev/null | head -40 || true
# Discard the local Program.cs hotfix (already merged into 6ea79008)
git checkout -- backend/src/OetLearner.Api/Program.cs 2>/dev/null || true
git checkout 6ea79008
git log -1 --oneline
