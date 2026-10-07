#!/bin/sh
# Container entrypoint for oet-agent-console. Runs as uid 0 = the control plane (CONTRACT.md §2),
# under docker's init (compose `init: true`, see Dockerfile), on every start:
#
#   1. prepares the agent / workspace / control-state directories on the named volumes;
#   2. (re)installs the engines' policy files from the image, root-owned, so any tampering
#      by the agent uid is reverted on restart:
#        /etc/claude-code/managed-settings.json   Claude Code managed policy
#        /etc/codex/managed_config.toml           Codex managed defaults (re-applied per start)
#        /etc/codex/requirements.toml             Codex hard requirements (login, approvals)
#        $CODEX_HOME/{config.toml,AGENTS.md,AGENTS.override.md,rules/oet.rules}
#   3. execs the control server.
#
# $CODEX_HOME and the agent home are root:agent 1770 (sticky, group-writable): the agent can
# create its own state files (auth.json, sessions/, ...) but cannot replace or rename the
# root-owned policy files. src/engines/codex.ts verifies this layout before starting Codex.
set -eu

APP_DIR=/app
ETC_DIR="$APP_DIR/etc"
AGENT_UID=10002
AGENT_GID=10002
AGENT_HOME=/home/agent
CLAUDE_DIR="$AGENT_HOME/.claude"
CODEX_DIR="$AGENT_HOME/.codex"
WORKSPACE=/workspace
DATA_DIR=/var/lib/oet-agent
DOCKER_CONFIG_ROOT=/run/oet-agent/docker
SERVER_ENTRY="$APP_DIR/dist/server.js"

log() { printf '%s oet-agent-entrypoint: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" >&2; }
die() {
  log "FATAL: $*"
  exit 1
}

[ "$(id -u)" = 0 ] || die "must start as uid 0 (control plane); see CONTRACT.md §2"
for f in managed-settings.json codex-config.toml codex-requirements.toml oet.rules MANUAL.md; do
  [ -f "$ETC_DIR/$f" ] || die "image is missing $ETC_DIR/$f"
done
[ -f "$SERVER_ENTRY" ] || die "image is missing $SERVER_ENTRY"

umask 022

# dir <path> <uid:gid> <mode> — create if absent, then force owner and mode (not recursive).
dir() {
  if [ -L "$1" ]; then rm -f "$1"; fi
  mkdir -p "$1"
  chown "$2" "$1"
  chmod "$3" "$1"
}

# replace_file <src> <dest> <mode> — root-owned copy, atomic; clears a planted dir/symlink first.
replace_file() {
  if [ -L "$2" ] || { [ -e "$2" ] && [ ! -f "$2" ]; }; then rm -rf "$2"; fi
  tmp="$(mktemp "$2.XXXXXX")"
  cat "$1" >"$tmp"
  chown 0:0 "$tmp"
  chmod "$3" "$tmp"
  mv -f "$tmp" "$2"
}

# ── 1. Directories ──────────────────────────────────────────────────────────────────────────

dir "$AGENT_HOME" "0:$AGENT_GID" 1770
dir "$CLAUDE_DIR" "$AGENT_UID:$AGENT_GID" 0700
dir "$AGENT_HOME/.config" "$AGENT_UID:$AGENT_GID" 0700
dir "$AGENT_HOME/.cache" "$AGENT_UID:$AGENT_GID" 0700
dir "$AGENT_HOME/.local" "$AGENT_UID:$AGENT_GID" 0700
dir "$AGENT_HOME/.local/share" "$AGENT_UID:$AGENT_GID" 0700
dir "$CODEX_DIR" "0:$AGENT_GID" 1770

dir "$WORKSPACE" "$AGENT_UID:$AGENT_GID" 0750
dir "$WORKSPACE/sessions" "$AGENT_UID:$AGENT_GID" 0750

dir "$DATA_DIR" 0:0 0700
dir "$DATA_DIR/sessions" 0:0 0700
dir "$(dirname "$DOCKER_CONFIG_ROOT")" 0:0 0755
dir "$DOCKER_CONFIG_ROOT" 0:0 0755

# ── 2a. Claude Code managed policy ──────────────────────────────────────────────────────────

dir /etc/claude-code 0:0 0755
replace_file "$ETC_DIR/managed-settings.json" /etc/claude-code/managed-settings.json 0644

# ── 2b. Codex policy ────────────────────────────────────────────────────────────────────────

# The ChatGPT Business workspace id pins Codex sign-in (forced_chatgpt_workspace_id /
# allowed_chatgpt_workspaces). OWNER_AGENT__CODEXWORKSPACEID is the .env.production name.
WORKSPACE_ID="${OWNER_AGENT_CODEX_WORKSPACE_ID:-${OWNER_AGENT__CODEXWORKSPACEID:-}}"
if [ -n "$WORKSPACE_ID" ]; then
  printf '%s' "$WORKSPACE_ID" | grep -Eq '^[A-Za-z0-9_-]{1,128}$' ||
    die "OWNER_AGENT_CODEX_WORKSPACE_ID has an unexpected format (expected the workspace UUID)"
else
  log "WARN: no Codex workspace id configured; ChatGPT sign-in is not pinned to one workspace"
fi

# render <template> <dest> <mode> — substitute the workspace id, or drop the pinning lines.
render() {
  if [ -L "$2" ] || { [ -e "$2" ] && [ ! -f "$2" ]; }; then rm -rf "$2"; fi
  tmp="$(mktemp "$2.XXXXXX")"
  if [ -n "$WORKSPACE_ID" ]; then
    sed "s/__OET_CODEX_WORKSPACE_ID__/$WORKSPACE_ID/g" "$1" >"$tmp"
  else
    sed '/__OET_CODEX_WORKSPACE_ID__/d' "$1" >"$tmp"
  fi
  chown 0:0 "$tmp"
  chmod "$3" "$tmp"
  mv -f "$tmp" "$2"
}

dir /etc/codex 0:0 0755
render "$ETC_DIR/codex-config.toml" /etc/codex/managed_config.toml 0644
render "$ETC_DIR/codex-requirements.toml" /etc/codex/requirements.toml 0644
render "$ETC_DIR/codex-config.toml" "$CODEX_DIR/config.toml" 0644

# Operating manual as Codex's global instructions (the override name too, so the agent cannot
# shadow the manual by creating AGENTS.override.md in the group-writable CODEX_HOME).
replace_file "$ETC_DIR/MANUAL.md" "$CODEX_DIR/AGENTS.md" 0444
replace_file "$ETC_DIR/MANUAL.md" "$CODEX_DIR/AGENTS.override.md" 0444

# Execpolicy: a root-owned rules/ holding only the forbidden backstop. Anything else found here
# (e.g. an allow rule written by an approval amendment) is removed.
if [ -L "$CODEX_DIR/rules" ] || { [ -e "$CODEX_DIR/rules" ] && [ ! -d "$CODEX_DIR/rules" ]; }; then rm -rf "$CODEX_DIR/rules"; fi
dir "$CODEX_DIR/rules" 0:0 0755
find "$CODEX_DIR/rules" -mindepth 1 -maxdepth 1 ! -name oet.rules -exec rm -rf {} +
replace_file "$ETC_DIR/oet.rules" "$CODEX_DIR/rules/oet.rules" 0444

# Instruction sources the console does not use stay root-owned and empty.
for d in skills prompts; do
  if [ -L "$CODEX_DIR/$d" ] || { [ -e "$CODEX_DIR/$d" ] && [ ! -d "$CODEX_DIR/$d" ]; }; then rm -rf "$CODEX_DIR/$d"; fi
  dir "$CODEX_DIR/$d" 0:0 0755
  find "$CODEX_DIR/$d" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
done

log "policy files installed (claude managed settings, codex managed config/requirements/rules, manual)"

# ── 3. Control server ───────────────────────────────────────────────────────────────────────

cd "$APP_DIR"
exec node "$SERVER_ENTRY"

