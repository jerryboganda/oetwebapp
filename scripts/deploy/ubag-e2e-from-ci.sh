#!/usr/bin/env bash
# OET <-> UBAG integration end-to-end probe.
#
# Executed ON the production VPS, fed to `bash -s` over SSH by
# .github/workflows/ubag-integration-e2e.yml. Compute stays on GitHub runners;
# this script only performs HTTP calls, so the shared host is not loaded.
#
# The HTTP client runs in a throwaway container attached to the OET internal
# network, which exercises the exact production path the API uses
# (oet-api -> ubag-vps-gateway-1) instead of a public ingress.
#
# Usage: bash ubag-e2e-from-ci.sh <core|browser|all> [targets] [timeout_s]
# Exit codes: 0 = contract held, 1 = core facade contract breached,
#             2 = core held but every browser target probe failed.
set -uo pipefail

MODE="${1:-core}"
TARGETS="${2:-chatgpt_web,deepseek_web}"
TIMEOUT_S="${3:-300}"

ENV_FILE="${OET_ENV_FILE:-/opt/oetwebapp/.env.production}"
NET="${OET_INTERNAL_NETWORK:-oetwebsite_internal}"
GATEWAY_CONTAINER="${UBAG_GATEWAY_CONTAINER:-ubag-vps-gateway-1}"
GATEWAY_ADDR="${UBAG_GATEWAY_ADDR:-ubag-vps-gateway-1:8080}"
PY_IMAGE="${UBAG_PROBE_PY_IMAGE:-python:3.12-alpine}"

echo "[info] mode=${MODE} targets=${TARGETS} timeout=${TIMEOUT_S}s"
echo "[info] env_file=${ENV_FILE} network=${NET} gateway=${GATEWAY_ADDR}"

if [ ! -r "$ENV_FILE" ]; then
  echo "[FAIL] cannot read ${ENV_FILE}"
  exit 1
fi

PAT="$(grep -m1 '^UBAG_OET_PAT=' "$ENV_FILE" | sed -e 's/^UBAG_OET_PAT=//' -e 's/\r$//' -e 's/^"//' -e 's/"$//')"
if [ -z "$PAT" ]; then
  echo "[FAIL] UBAG_OET_PAT is not set in ${ENV_FILE}"
  exit 1
fi
echo "[info] UBAG_OET_PAT loaded: prefix=${PAT:0:9}... length=${#PAT}"

# --- read-only topology checks -------------------------------------------
if ! docker inspect "$GATEWAY_CONTAINER" >/dev/null 2>&1; then
  echo "[FAIL] container ${GATEWAY_CONTAINER} does not exist"
  exit 1
fi
GATEWAY_STATE="$(docker inspect "$GATEWAY_CONTAINER" --format '{{.State.Status}}' 2>/dev/null)"
echo "[info] ${GATEWAY_CONTAINER} state=${GATEWAY_STATE}"

NET_MEMBERS="$(docker network inspect "$NET" --format '{{range .Containers}}{{.Name}} {{end}}' 2>/dev/null)"
if printf '%s' "$NET_MEMBERS" | grep -qw "$GATEWAY_CONTAINER"; then
  echo "[PASS] ${GATEWAY_CONTAINER} is attached to ${NET}"
else
  echo "[FAIL] ${GATEWAY_CONTAINER} is NOT attached to ${NET}"
  echo "[info] members: ${NET_MEMBERS}"
  exit 1
fi

# --- live HTTP probes (inside the OET network) ---------------------------
export UBAG_PAT="$PAT"
export UBAG_BASE="http://${GATEWAY_ADDR}"
export UBAG_PROBE_MODE="$MODE"
export UBAG_PROBE_TARGETS="$TARGETS"
export UBAG_PROBE_TIMEOUT="$TIMEOUT_S"

docker run --rm -i \
  --network "$NET" \
  -e UBAG_PAT -e UBAG_BASE -e UBAG_PROBE_MODE -e UBAG_PROBE_TARGETS -e UBAG_PROBE_TIMEOUT \
  "$PY_IMAGE" python3 - <<'PYPROBE'
import json
import os
import sys
import time
import urllib.error
import urllib.request

BASE = os.environ["UBAG_BASE"].rstrip("/")
PAT = os.environ["UBAG_PAT"]
MODE = os.environ.get("UBAG_PROBE_MODE", "core")
TARGETS = [t.strip() for t in os.environ.get("UBAG_PROBE_TARGETS", "").split(",") if t.strip()]
TIMEOUT = int(os.environ.get("UBAG_PROBE_TIMEOUT", "300"))

results = []


def call(method, path, body=None, timeout=60):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    req.add_header("Authorization", "Bearer " + PAT)
    req.add_header("Accept", "application/json")
    if data is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.status, resp.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read().decode("utf-8", "replace")
    except Exception as exc:  # noqa: BLE001 - transport classification only
        return 0, "transport-error: %s: %s" % (type(exc).__name__, exc)


def record(name, ok, detail):
    results.append((name, ok, detail))
    print("[%s] %s -- %s" % ("PASS" if ok else "FAIL", name, detail))


def run_core():
    status, raw = call("GET", "/v1/openai/models")
    models = []
    if status == 200:
        try:
            models = [m.get("id") for m in json.loads(raw).get("data", [])]
        except Exception:  # noqa: BLE001
            models = []
    record(
        "facade GET /v1/openai/models",
        status == 200 and len(models) > 0,
        "http=%s models=%d sample=%s" % (status, len(models), ",".join(m for m in models[:6] if m)),
    )

    status, raw = call(
        "POST",
        "/v1/openai/chat/completions",
        {
            "model": "mock",
            "messages": [{"role": "user", "content": "Reply with exactly PONG"}],
            "max_tokens": 16,
        },
        timeout=180,
    )
    job_id = ""
    try:
        job_id = json.loads(raw).get("ubag_job_id", "")
    except Exception:  # noqa: BLE001
        job_id = ""
    if status == 200 and job_id:
        record("facade POST /v1/openai/chat/completions (mock)", True, "http=200 ubag_job_id=%s" % job_id)
    else:
        record(
            "facade POST /v1/openai/chat/completions (mock)",
            False,
            "http=%s ubag_job_id=%s body=%s" % (status, job_id or "-", raw[:300]),
        )


def run_target(target):
    body = {
        "api_version": "2026-05-22",
        "client": {
            "app_id": "oet-platform",
            "app_version": "1.0.0",
            "sdk": {"name": "ubag-openai-facade", "version": "0.0.0-ci-probe"},
        },
        "job": {
            "target": target,
            "command_type": "chat.prompt",
            "input": {"prompt": "user: Reply with exactly PONG"},
            "options": {
                "return_mode": "final",
                "timeout_seconds": max(60, min(TIMEOUT, 900)),
            },
        },
    }
    status, raw = call("POST", "/v1/jobs", body, timeout=60)
    job_id = ""
    try:
        job_id = json.loads(raw).get("job_id", "")
    except Exception:  # noqa: BLE001
        job_id = ""

    if status not in (200, 201, 202) or not job_id:
        record("target %s: submit" % target, False, "http=%s body=%s" % (status, raw[:240]))
        return False

    print("[info] target %s: submitted %s" % (target, job_id))
    deadline = time.time() + TIMEOUT
    state = "unknown"
    text = ""
    while time.time() < deadline:
        time.sleep(5)
        s2, r2 = call("GET", "/v1/jobs/%s" % job_id, timeout=30)
        if s2 != 200:
            continue
        try:
            doc = json.loads(r2)
        except Exception:  # noqa: BLE001
            continue
        state = doc.get("status", "unknown")
        if state in ("completed", "failed", "cancelled", "expired", "dead"):
            output = (doc.get("result") or {}).get("output") or {}
            text = (output.get("plain_text") or output.get("text") or "").replace("\n", " ")[:160]
            break

    ok = state == "completed"
    record(
        "target %s: terminal status" % target,
        ok,
        "job=%s status=%s elapsed<=%ss output=%r" % (job_id, state, TIMEOUT, text),
    )
    return ok


if MODE in ("core", "all"):
    run_core()

target_ok = {}
if MODE in ("browser", "all"):
    for target in TARGETS:
        target_ok[target] = run_target(target)

print("")
print("=== UBAG PROBE SUMMARY ===")
for name, ok, detail in results:
    print("%-4s | %s | %s" % ("PASS" if ok else "FAIL", name, detail))
print("=== END SUMMARY ===")

core_failed = any(not ok for name, ok, _ in results if name.startswith("facade GET") or "chat/completions" in name)
if core_failed:
    sys.exit(1)
if target_ok and not any(target_ok.values()):
    sys.exit(2)
sys.exit(0)
PYPROBE

rc=$?
echo "[info] probe exit code: ${rc}"
exit "$rc"
