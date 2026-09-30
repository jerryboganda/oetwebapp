"""Regression runner for the Writing grader — Opus 5.5 High via the Claude Code CLI
on the owner's Max subscription (the same transport the original cc-opus55-high
benchmark used). NO Anthropic API key / paid quota is used.

Each corpus letter is graded once. Results land in runs/<letterId>.json.

Usage:
  python run_regression.py                 # dry run: build prompts, print sizes, no model call
  python run_regression.py --execute       # grade every corpus letter via the CLI
  python run_regression.py --execute --only W5DAF6313D W6488AA2A5
"""
import argparse
import hashlib
import json
import os
import subprocess
import sys
import time

import prompt_builder as pb

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(HERE, "corpus")
RUNS = os.path.join(HERE, "runs")
SCHEMA_PATH = os.path.join(HERE, "schema.json")

MODEL = "claude-opus-5-5"
EFFORT = "high"

# JSON schema for the grading reply (mirrors the bench-v1 enforced schema; kept flat,
# no numeric bounds, because strict modes reject them).
FINDING = {"type": "object", "additionalProperties": False,
           "properties": {"ruleId": {"type": ["string", "null"]},
                          "severity": {"type": "string", "enum": ["critical", "major", "minor"]},
                          "quote": {"type": "string"}, "message": {"type": "string"},
                          "fixSuggestion": {"type": "string"},
                          "criterionCode": {"type": "string", "enum": ["purpose", "content", "conciseness_clarity",
                                                                       "genre_style", "organisation_layout", "language"]},
                          "isOmission": {"type": "boolean"}, "sourceEvidence": {"type": ["string", "null"]}},
           "required": ["ruleId", "severity", "quote", "message", "fixSuggestion", "criterionCode", "isOmission", "sourceEvidence"]}
CRIT = ["purpose", "content", "conciseness_clarity", "genre_style", "organisation_layout", "language"]
SCHEMA = {"type": "object", "additionalProperties": False,
          "properties": {"findings": {"type": "array", "items": FINDING},
                         "criteriaScores": {"type": "object", "additionalProperties": False,
                                            "properties": {k: {"type": "integer"} for k in CRIT}, "required": CRIT},
                         "estimatedScaledScore": {"type": "integer"}, "estimatedGrade": {"type": "string"},
                         "passed": {"type": "boolean"}, "overallConfidence": {"type": "string", "enum": ["high", "medium", "low"]},
                         "advisory": {"type": "string"}},
          "required": ["findings", "criteriaScores", "estimatedScaledScore", "estimatedGrade", "passed", "overallConfidence", "advisory"]}


def load_corpus(only):
    ids = []
    for fn in sorted(os.listdir(CORPUS)):
        if fn.endswith(".json"):
            lid = fn[:-5]
            if not only or lid in only:
                ids.append(lid)
    return [json.loads(open(os.path.join(CORPUS, lid + ".json"), encoding="utf-8").read()) for lid in ids]


def normalise(parsed):
    if not isinstance(parsed, dict):
        return None
    for f in parsed.get("findings") or []:
        if isinstance(f, dict):
            f.setdefault("explanation", f.get("message"))
            f.setdefault("correction", f.get("fixSuggestion"))
            f.setdefault("criterion", f.get("criterionCode"))
            if isinstance(f.get("severity"), str):
                f["severity"] = f["severity"].lower()
    return parsed


def grade(inp):
    sysp = pb.system_prompt(inp["profession"], inp["letterType"])
    userp = pb.user_message(inp)
    sysfile = os.path.join(HERE, "_sys.txt")
    with open(sysfile, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(sysp + "\n" + pb.BENCH_EXTENSION)
    # Windows claude.cmd shim: an empty-string value after --setting-sources / --tools is
    # swallowed and mis-parses the NEXT flag, so pass them only with real values omitted.
    cmd = ["claude", "-p", "--model", MODEL, "--effort", EFFORT, "--system-prompt-file", sysfile,
           "--json-schema", json.dumps(SCHEMA), "--output-format", "json",
           "--strict-mcp-config", "--no-session-persistence"]
    t0 = time.time()
    # Windows: `claude` is a .ps1/.cmd shim, not an .exe — CreateProcess cannot exec it
    # directly (WinError 2), so route through the shell. Encode the prompt as UTF-8 bytes:
    # text=True would use the cp1252 console codec and crash on case-note chars (e.g. \u25cf).
    r = subprocess.run(cmd, input=userp.encode("utf-8"), capture_output=True, timeout=3600, shell=True)
    r_stdout = r.stdout.decode("utf-8", "replace") if isinstance(r.stdout, bytes) else r.stdout
    r_stderr = r.stderr.decode("utf-8", "replace") if isinstance(r.stderr, bytes) else r.stderr
    r = type("R", (), {"returncode": r.returncode, "stdout": r_stdout, "stderr": r_stderr})
    latency = int((time.time() - t0) * 1000)
    rec = {"letterId": inp["letterId"], "provider": "claude-code-subscription", "model": MODEL, "effort": EFFORT,
           "request": {"effort": EFFORT, "transport": "claude -p (Max subscription)"},
           "promptMode": "regression-v1", "promptSha256": pb.prompt_sha256(inp["profession"], inp["letterType"]),
           "houseStyleVersion": pb.HOUSE_VERSION, "latencyMs": latency, "exitCode": r.returncode}
    text = ""
    parsed = None
    try:
        env = json.loads(r.stdout)
        rec["cli"] = {k: env.get(k) for k in ("subtype", "is_error", "total_cost_usd", "usage")}
        text = env.get("result") or ""
    except Exception:
        rec["stdout"] = r.stdout[-3000:]
    if r.stderr:
        rec["stderr"] = r.stderr[-1500:]
    if text:
        m = text
        if "```" in m:
            import re as _re
            g = _re.search(r"```(?:json)?\s*(\{.*\})\s*```", m, _re.S)
            if g:
                m = g.group(1)
        try:
            parsed = normalise(json.loads(m))
        except Exception:
            i, j = m.find("{"), m.rfind("}")
            if i != -1 and j > i:
                try:
                    parsed = normalise(json.loads(m[i:j + 1]))
                except Exception:
                    parsed = None
    rec["parsed"] = parsed
    rec["error"] = None if parsed else ("no-parse" if not rec.get("stdout") else "cli-error")
    return rec


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--execute", action="store_true")
    ap.add_argument("--only", nargs="*")
    args = ap.parse_args()

    corpus = load_corpus(args.only)
    if not args.execute:
        print("DRY RUN — %d letters, model %s (%s) via claude -p (Max subscription)\n" % (len(corpus), MODEL, EFFORT))
        for inp in corpus:
            s = pb.system_prompt(inp["profession"], inp["letterType"])
            u = pb.user_message(inp)
            print("  %-12s %-22s %-6s system=%5d ch  user=%5d ch  promptSha=%s" % (
                inp["letterId"], inp["profession"], inp["letterType"], len(s), len(u),
                pb.prompt_sha256(inp["profession"], inp["letterType"])[:12]))
        print("\nNo model called. Re-run with --execute to grade.")
        return

    os.makedirs(RUNS, exist_ok=True)
    with open(SCHEMA_PATH, "w", encoding="utf-8") as fh:
        json.dump(SCHEMA, fh, indent=1)
    ok = err = 0
    for inp in corpus:
        dest = os.path.join(RUNS, inp["letterId"] + ".json")
        # resumable: a letter with a parsed result under the CURRENT prompt hash is skipped
        if os.path.exists(dest):
            prev = json.loads(open(dest, encoding="utf-8").read())
            if prev.get("parsed") is not None and prev.get("promptSha256") == pb.prompt_sha256(inp["profession"], inp["letterType"]):
                print("%-12s  SKIP (already graded under current prompt)" % inp["letterId"], flush=True)
                ok += 1
                continue
        rec = grade(inp)
        with open(dest, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(rec, fh, ensure_ascii=False, indent=1)
        good = rec.get("parsed") is not None
        ok += int(good)
        err += int(not good)
        print("%-12s %6.1fs  %s  findings=%s" % (
            inp["letterId"], rec["latencyMs"] / 1000.0, "ok" if good else "ERROR",
            len((rec.get("parsed") or {}).get("findings") or [])), flush=True)
    print("\nDone. %d ok, %d failed. Results in %s" % (ok, err, RUNS))
    if err:
        sys.exit(1)


if __name__ == "__main__":
    main()
