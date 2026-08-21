#!/usr/bin/env bash
# Upload a staged native release directory to the production VPS and activate it.
set -euo pipefail

: "${SSH_KEY:?SSH_KEY is required}"
: "${CHANNEL:?CHANNEL is required}"
: "${VERSION:?VERSION is required}"
: "${SRC_DIR:?SRC_DIR is required}"

VPS_HOST="${VPS_HOST:-185.252.233.186}"
VPS_USER="${VPS_USER:-root}"
VPS_PORT="${VPS_PORT:-22}"
RELEASES_ROOT="${RELEASES_ROOT:-/var/opt/oet-learner/releases}"
REMOTE_DIR="/tmp/oet-native-${CHANNEL}-${VERSION}-${GITHUB_RUN_ID:-local}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

mkdir -p ~/.ssh
printf '%s\n' "$SSH_KEY" > ~/.ssh/deploy_key
chmod 600 ~/.ssh/deploy_key
ssh-keyscan -p "$VPS_PORT" "$VPS_HOST" >> ~/.ssh/known_hosts 2>/dev/null || true
ssh_opts=(
  -i ~/.ssh/deploy_key -p "$VPS_PORT"
  -o IdentitiesOnly=yes -o PreferredAuthentications=publickey
  -o PasswordAuthentication=no -o BatchMode=yes
  -o StrictHostKeyChecking=accept-new -o ServerAliveInterval=30
  -o ServerAliveCountMax=20
)

cleanup() {
  ssh "${ssh_opts[@]}" "$VPS_USER@$VPS_HOST" "rm -rf '$REMOTE_DIR'" >/dev/null 2>&1 || true
  rm -f ~/.ssh/deploy_key
}
trap cleanup EXIT

ssh "${ssh_opts[@]}" "$VPS_USER@$VPS_HOST" "umask 077; mkdir -p '$REMOTE_DIR/files' '$RELEASES_ROOT'"
scp "${ssh_opts[@]}" "$SCRIPT_DIR/deploy/publish-native-release.sh" "$VPS_USER@$VPS_HOST:$REMOTE_DIR/publish-native-release.sh"
# Copy staged files file-by-file so spaces/newlines in names cannot break the upload.
while IFS= read -r -d '' file; do
  name="$(basename "$file")"
  scp "${ssh_opts[@]}" "$file" "$VPS_USER@$VPS_HOST:$REMOTE_DIR/files/$name"
done < <(find "$SRC_DIR" -type f -print0)

ssh "${ssh_opts[@]}" "$VPS_USER@$VPS_HOST" \
  "chmod 700 '$REMOTE_DIR/publish-native-release.sh'; \
   CHANNEL='$CHANNEL' VERSION='$VERSION' SRC_DIR='$REMOTE_DIR/files' \
   RELEASES_ROOT='$RELEASES_ROOT' bash '$REMOTE_DIR/publish-native-release.sh'"
