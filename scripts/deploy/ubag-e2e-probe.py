#!/usr/bin/env python3
"""OET <-> UBAG integration end-to-end probe (runner-side).

Runs ENTIRELY on the GitHub Actions runner. Per the project's compute-isolation
policy, the production VPS performs no computation; it only forwards bytes for
an SSH local port-forward and answers ordinary HTTP requests on the gateway.

Usage:
    UBAG_BASE=http://127.0.0.1:18080 UBAG_PAT=... \
        python3 ubag-e2e-probe.py <core|providers|all> [targets] [budget_s]

Exit codes:
    0  contract held (pending operator sign-ins are surfaced, not fatal)
    1  core contract breached, or a genuine provider defect
    2  core held but every provider probe failed

Contract note (learned the hard way): the facade ALWAYS injects
options.provider_config._enabled=false (apps/gateway/internal/httpapi/
openai_facade.go). That marker makes the picker config "best effort": a drifted
provider model menu is skipped instead of failing the job. A raw POST /v1/jobs
that omits the marker is treated as REQUIRING the picker, and drift then
surfaces as UBAG-ADAPTER-DRIFT-014. This probe sends the marker so it
reproduces the facade contract rather than inventing a stricter one.

Why submit-all-then-poll-all: the live worker runs with
UBAG_WORKER_CONCURRENCY=1, so provider jobs are processed strictly one at a
time. Submitting a batch and then polling all of them lets the queue drain
serially while the probe waits on a single global budget, instead of each probe
burning its own timeout while stuck behind the queue.
"""

import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
import uuid

BASE = os.environ.get("UBAG_BASE", "http://127.0.0.1:18080").rstrip("/")
PAT = os.environ.get("UBAG_PAT", "")
MODE = sys.argv[1] if len(sys.argv) > 1 else "all"
TARGETS = [t.strip() for t in (sys.argv[2] if len(sys.argv) > 2 else "").split(",") if t.strip()]
BUDGET = int(sys.argv[3]) if len(sys.argv) > 3 else 1800

if not TARGETS:
    TARGETS = ["deepseek_web", "chatgpt_web", "claude_web", "gemini_web",
               "mistral_lechat", "perplexity_web", "duckai_web"]

results = []


def call(method, path, body=None, timeout=60, headers=None):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    req.add_header("Authorization", "Bearer " + PAT)
    req.add_header("Accept", "application/json")
    if data is not None:
        req.add_header("Content-Type", "application/json")
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.status, resp.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read().decode("utf-8", "replace")
    except Exception as exc:  # noqa: BLE001 - transport classification only
        return 0, "transport-error: %s: %s" % (type(exc).__name__, exc)


def record(name, verdict, detail):
    results.append((name, verdict, detail))
    print("[%-7s] %s -- %s" % (verdict, name, detail), flush=True)


def run_core():
    status, raw = call("GET", "/v1/openai/models")
    models = []
    if status == 200:
        try:
            models = [m.get("id") for m in json.loads(raw).get("data", [])]
        except Exception:  # noqa: BLE001
            models = []
    record("core facade GET /v1/openai/models",
           "PASS" if (status == 200 and models) else "FAIL",
           "http=%s models=%d" % (status, len(models)))

    status, raw = call(
        "POST", "/v1/openai/chat/completions",
        {"model": "mock",
         "messages": [{"role": "user", "content": "Reply with exactly PONG"}],
         "max_tokens": 16},
        timeout=180,
    )
    job_id = ""
    try:
        job_id = json.loads(raw).get("ubag_job_id", "")
    except Exception:  # noqa: BLE001
        pass
    # The mock target fabricates a canned answer, so this asserts the transport
    # contract (2xx + a real ubag_job_id), not the mock's wording.
    record("core facade POST /v1/openai/chat/completions (mock)",
           "PASS" if (status == 200 and job_id) else "FAIL",
           "http=%s ubag_job_id=%s" % (status, job_id or "-"))


def run_facade_spotcheck():
    """The real OET path, including a setting-pinned model id."""
    for model in ("deepseek_web", "chatgpt_web|GPT-5.6 Sol + Medium"):
        status, raw = call(
            "POST", "/v1/openai/chat/completions",
            {"model": model,
             "messages": [{"role": "user", "content": "Reply with exactly PONG"}],
             "max_tokens": 16},
            timeout=420,
        )
        content = ""
        jid = ""
        try:
            doc = json.loads(raw)
            jid = doc.get("ubag_job_id", "")
            content = ((doc.get("choices") or [{}])[0].get("message") or {}).get("content", "")
        except Exception:  # noqa: BLE001
            pass
        if status == 200 and str(content).strip().upper().startswith("PONG"):
            record("facade OET path model=%s" % model, "PASS",
                   "http=200 content=%r job=%s" % (str(content).strip()[:12], jid))
            continue
        m = re.search(r"(job_\d+)", raw)
        if m:
            verdict, detail = classify_job(m.group(1), time.time() + 600)
            record("facade OET path model=%s" % model, verdict, "%s (facade http=%s)" % (detail, status))
        else:
            record("facade OET path model=%s" % model, "FAIL",
                   "http=%s body=%s" % (status, raw[:200]))


def classify_job(job_id, deadline):
    state = "unknown"
    err = ""
    cls = ""
    manual = ""
    while time.time() < deadline:
        status, raw = call("GET", "/v1/jobs/%s" % job_id, timeout=30)
        if status == 200:
            try:
                doc = json.loads(raw)
            except Exception:  # noqa: BLE001
                doc = {}
            state = doc.get("status", "unknown")
            err = doc.get("error") or ""
            cls = doc.get("error_class") or ""
            manual = doc.get("manual_action") or ""
            if state == "completed":
                out = (doc.get("result") or {}).get("output") or {}
                text = (out.get("plain_text") or out.get("text") or "").replace("\n", " ")[:60]
                return "PASS", "job=%s completed output=%r" % (job_id, text)
            if state.startswith("failed") or state in ("cancelled", "expired", "dead"):
                break
        time.sleep(5)
    if cls == "provider_login_required" or manual:
        return "SIGNIN", "job=%s %s -- %s" % (job_id, state, (manual or err or "sign-in required")[:120])
    if state in ("queued", "assigned", "running", "session.opening", "unknown"):
        return "TIMEOUT", "job=%s still %s when the budget expired" % (job_id, state)
    return "FAIL", "job=%s %s error_class=%s error=%s" % (job_id, state, cls or "-", (err or "-")[:140])


def submit(target):
    body = {
        "api_version": "2026-05-22",
        "client": {"app_id": "oet-platform", "app_version": "1.0.0",
                   "sdk": {"name": "ubag-openai-facade", "version": "0.0.0-ci-probe"}},
        "job": {"target": target, "command_type": "chat.prompt",
                "input": {"prompt": "user: Reply with exactly PONG"},
                "options": {"return_mode": "final", "timeout_seconds": 900,
                            # Mirrors what openai_facade.go always injects.
                            "provider_config": {"_enabled": False}}},
    }
    # Raw job creation requires a client idempotency key
    # (^[A-Za-z0-9._:-]{16,128}$); the OpenAI facade generates its own.
    headers = {"Idempotency-Key": "oet-ci-%s-%s" % (target, uuid.uuid4())}
    status, raw = call("POST", "/v1/jobs", body, timeout=60, headers=headers)
    if status not in (200, 201, 202):
        record("provider %s" % target, "FAIL", "submit http=%s body=%s" % (status, raw[:180]))
        return None
    try:
        jid = json.loads(raw).get("job_id", "")
    except Exception:  # noqa: BLE001
        jid = ""
    if not jid:
        record("provider %s" % target, "FAIL", "submit http=%s no job_id" % status)
        return None
    print("[info  ] submitted %s -> %s" % (target, jid), flush=True)
    return jid


def run_providers():
    submitted = []
    for t in TARGETS:
        jid = submit(t)
        if jid:
            submitted.append((t, jid))

    deadline = time.time() + BUDGET
    pending = {jid: t for t, jid in submitted}
    while pending and time.time() < deadline:
        for jid in list(pending):
            status, raw = call("GET", "/v1/jobs/%s" % jid, timeout=30)
            if status != 200:
                continue
            try:
                doc = json.loads(raw)
            except Exception:  # noqa: BLE001
                continue
            state = doc.get("status", "unknown")
            verdict = None
            if state == "completed":
                out = (doc.get("result") or {}).get("output") or {}
                text = (out.get("plain_text") or out.get("text") or "").replace("\n", " ")[:60]
                verdict = ("PASS", "job=%s completed output=%r" % (jid, text))
            elif state.startswith("failed") or state in ("cancelled", "expired", "dead"):
                cls = doc.get("error_class") or ""
                err = doc.get("error") or ""
                manual = doc.get("manual_action") or ""
                if cls == "provider_login_required" or manual:
                    verdict = ("SIGNIN", "job=%s %s -- %s"
                               % (jid, state, (manual or err or "sign-in required")[:130]))
                else:
                    verdict = ("FAIL", "job=%s %s error_class=%s error=%s"
                               % (jid, state, cls or "-", (err or "-")[:140]))
            if verdict:
                t = pending.pop(jid)
                record("provider %s" % t, verdict[0], verdict[1])
        if pending:
            time.sleep(5)

    for jid, t in list(pending.items()):
        status, raw = call("GET", "/v1/jobs/%s" % jid, timeout=30)
        state = "unknown"
        try:
            state = json.loads(raw).get("status", "unknown")
        except Exception:  # noqa: BLE001
            pass
        record("provider %s" % t, "TIMEOUT", "job=%s still %s after %ss budget" % (jid, state, BUDGET))


def main():
    print("[info] base=%s mode=%s budget=%ss" % (BASE, MODE, BUDGET), flush=True)
    print("[info] targets=%s" % ",".join(TARGETS), flush=True)
    if not PAT:
        print("[FAIL] UBAG_PAT is not set in the runner environment")
        return 1
    print("[info] UBAG_PAT loaded: length=%d" % len(PAT), flush=True)

    if MODE in ("core", "all"):
        run_core()
    if MODE in ("providers", "all"):
        run_facade_spotcheck()
        run_providers()

    print("", flush=True)
    print("=== UBAG PROBE SUMMARY ===")
    for name, verdict, detail in results:
        print("%-7s | %s | %s" % (verdict, name, detail))
    print("=== END SUMMARY ===")

    core = [v for n, v, _ in results if n.startswith("core ")]
    prov = [(n, v) for n, v, _ in results if n.startswith("provider ") or "OET path" in n]
    passed = [n for n, v in prov if v == "PASS"]
    signin = [n for n, v in prov if v == "SIGNIN"]
    failed = [n for n, v in prov if v == "FAIL"]
    timeouts = [n for n, v in prov if v == "TIMEOUT"]

    print("")
    print("providers probed=%d completed=%d signin_required=%d failed=%d timeout=%d"
          % (len(prov), len(passed), len(signin), len(failed), len(timeouts)))

    if "FAIL" in core:
        return 1
    if prov and not passed and not signin:
        return 2
    if failed or timeouts:
        return 1
    if signin:
        print("OPERATOR-ACTION-REQUIRED: %s" % ", ".join(signin))
    return 0


if __name__ == "__main__":
    sys.exit(main())
