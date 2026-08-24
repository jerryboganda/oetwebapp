"""Golden-set parity harness: Antigravity gateway vs baseline provider.

Phase 3c gate (docs/antigravity/roadmap.md): before promoting any AI feature
route to the Antigravity agent, prove on a fixed golden set that the gateway
matches or beats the incumbent provider and never breaks the product's
parse contract.

Usage (repo root):
  agent-gateway\\.venv\\Scripts\\python.exe scripts\\antigravity\\parity\\run_parity.py ^
      --gateway-url http://localhost:8305 --gateway-token <internal-token> ^
      [--baseline-url https://api.anthropic.com/v1 --baseline-key sk-ant-... ^
       --baseline-model claude-sonnet-4-5] ^
      [--suite both|writing|speaking] [--sample N] [--report parity-report.md]

Baseline is optional; without it the gate checks structural conformance only
(100% required). Exit code 0 = gate passed.

Stdlib only - no third-party imports, safe to run from CI or the venv.
"""
from __future__ import annotations

import argparse
import json
import re
import statistics
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

HERE = Path(__file__).parent
GOLDEN = HERE / "golden"

WRITING_SYSTEM = (
    "You are an OET writing examiner for the OET with Dr. Hesham platform. "
    "Score the letter task strictly against the OET writing rulebook. "
    'Respond with ONLY a JSON array of exactly six objects, each shaped as '
    '{"criterionCode": str, "score": int, "maxScore": int, "rationale": str, '
    '"evidenceQuotes": [str]} where criterionCode is one of purpose, content, '
    "conciseness, genre, organization, language; maxScore is 3 for purpose and "
    "7 for every other criterion; score is between 0 and maxScore. No prose, no "
    "markdown fences."
)

SPEAKING_SYSTEM = (
    'Respond with ONLY a JSON object {"text": str, "severity": "low"|"medium"|"high"} '
    "where text is the patient's next role-play turn (stays in character, medicine "
    "domain) and severity is the clinical urgency hint. No prose, no markdown fences."
)


def _post_chat(url: str, token: str | None, model: str, system: str, user: str, timeout: float) -> tuple[str, float]:
    body = {
        "model": model,
        "messages": [
            {"role": "system", "content": system},
            {"role": "user", "content": user},
        ],
        "stream": False,
    }
    req = urllib.request.Request(
        url.rstrip("/") + "/v1/chat/completions",
        data=json.dumps(body).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    started = time.perf_counter()
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        payload = json.loads(resp.read().decode("utf-8"))
    elapsed = time.perf_counter() - started
    return payload["choices"][0]["message"]["content"], elapsed


def _strip_fences(text: str) -> str:
    text = text.strip()
    fence = re.match(r"^```(?:json)?\s*(.*?)\s*```$", text, re.DOTALL)
    if fence:
        return fence.group(1)
    return text


def _parse_json(text: str):
    try:
        return json.loads(_strip_fences(text))
    except (json.JSONDecodeError, ValueError):
        return None


def eval_writing(item: dict, raw: str) -> dict:
    checks_total = 6
    checks_passed = 0
    data = _parse_json(raw)
    if not isinstance(data, list):
        return {"structural": 0.0, "structuralPass": False, "coverage": 0.0, "detail": "not a JSON array"}
    by_code = {}
    ok_shape = True
    expected_max = item["expected"]["maxScores"]
    for obj in data:
        if not isinstance(obj, dict):
            ok_shape = False
            continue
        code = obj.get("criterionCode")
        by_code[code] = obj
    codes_ok = (
        sorted(k for k in by_code if k)
        == sorted(item["expected"]["criterionCodes"])
        and len(data) == 6
    )
    if codes_ok:
        checks_passed += 1
    scores_ok = True
    total = 0
    for code, obj in by_code.items():
        cap = expected_max.get(code)
        score = obj.get("score")
        if cap is None or not isinstance(score, int) or isinstance(score, bool) or not (0 <= score <= cap):
            scores_ok = False
        elif obj.get("maxScore") != cap:
            scores_ok = False
        else:
            total += score
    if scores_ok and codes_ok:
        checks_passed += 2
    blob = json.dumps(data).lower()
    facts = item["expected"]["mustCoverFacts"]
    covered = sum(1 for fact in facts if _keywords_hit(fact, blob))
    coverage = covered / len(facts)
    min_total = item["expected"]["minTotalScore"]
    plausible = total >= min_total if codes_ok and scores_ok else False
    structural = (checks_passed / checks_total) * (1.0 if plausible else 0.9)
    return {
        "structural": round(structural, 4),
        "structuralPass": checks_passed == checks_total and plausible,
        "coverage": round(coverage, 4),
        "totalScore": total,
        "detail": f"codes_ok={codes_ok} scores_ok={scores_ok} plausible={plausible}",
    }


def _keywords_hit(fact: str, blob: str) -> bool:
    """Case-insensitive containment of the fact's informative tokens."""
    stop = {"the", "a", "an", "of", "and", "or", "to", "for", "in", "on", "with", "is", "are", "if"}
    tokens = [t for t in re.split(r"[^a-z0-9]+", fact.lower()) if t and t not in stop]
    if not tokens:
        return False
    hits = sum(1 for t in tokens if t in blob)
    return hits / len(tokens) >= 0.6


def eval_speaking(item: dict, raw: str) -> dict:
    data = _parse_json(raw)
    exp = item["expected"]
    if not isinstance(data, dict) or not isinstance(data.get("text"), str):
        return {"structural": 0.0, "structuralPass": False, "coverage": 0.0, "detail": "not utterance JSON"}
    checks = 0
    if data.get("severity") in ("low", "medium", "high"):
        checks += 1
    length = len(data["text"])
    if exp["minChars"] <= length <= exp["maxChars"]:
        checks += 1
    in_character = not re.search(r"\b(as an ai|i cannot|i'm sorry, but)\b", data["text"], re.I)
    if in_character:
        checks += 1
    blob = data["text"].lower()
    elems = exp["roleplayElements"]
    covered = sum(1 for e in elems if _keywords_hit(e, blob))
    coverage = covered / len(elems)
    structural = checks / 3
    return {
        "structural": round(structural, 4),
        "structuralPass": checks == 3,
        "coverage": round(coverage, 4),
        "severity": data.get("severity"),
        "detail": f"len={length} severity={data.get('severity')} in_character={in_character}",
    }


def run_suite(suite_file: Path, args, limit: int | None) -> list[dict]:
    cfg = json.loads(suite_file.read_text(encoding="utf-8"))
    agent = cfg["suite"]
    items = cfg["items"][:limit] if limit else cfg["items"]
    results: list[dict] = []
    for item in items:
        if agent == "writing-examiner":
            system = WRITING_SYSTEM
            user = item["taskNotes"]
            evaluator = eval_writing
            baseline_model = args.baseline_model or ""
        else:
            system = SPEAKING_SYSTEM
            user = item["candidatePrompt"]
            evaluator = eval_speaking
            baseline_model = args.baseline_model or ""
        row: dict = {"id": item["id"]}
        try:
            raw, latency = _post_chat(args.gateway_url, args.gateway_token, f"agent:{agent}", system, user, args.timeout)
            row["gateway"] = {**evaluator(item, raw), "latencyS": round(latency, 2)}
        except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, KeyError) as exc:
            row["gateway"] = {"structural": 0.0, "structuralPass": False, "coverage": 0.0, "error": repr(exc)}
        if args.baseline_url and args.baseline_key:
            try:
                raw_b, latency_b = _post_chat(
                    args.baseline_url, args.baseline_key, baseline_model, system, user, args.timeout
                )
                row["baseline"] = {**evaluator(item, raw_b), "latencyS": round(latency_b, 2)}
            except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, KeyError) as exc:
                row["baseline"] = {"structural": 0.0, "structuralPass": False, "coverage": 0.0, "error": repr(exc)}
        results.append(row)
        status = "PASS" if row.get("gateway", {}).get("structuralPass") else "FAIL"
        print(f"[{agent}] {item['id']}: {status} gw={row['gateway'].get('structural')} cov={row['gateway'].get('coverage')}")
    return results


def summarize(name: str, rows: list[dict], has_baseline: bool) -> dict:
    gw = [r["gateway"] for r in rows]
    summary = {
        "suite": name,
        "items": len(rows),
        "gateway_structural_pass_rate": round(sum(1 for g in gw if g.get("structuralPass")) / len(rows), 4),
        "gateway_mean_score": round(statistics.fmean(g["structural"] * 0.6 + g.get("coverage", 0) * 0.4 for g in gw), 4),
        "gateway_p95_latency_s": round(sorted(g.get("latencyS", 0) for g in gw)[int(0.95 * (len(gw) - 1))], 2) if gw else None,
    }
    if has_baseline:
        bl = [r.get("baseline") for r in rows if r.get("baseline")]
        if bl:
            summary["baseline_mean_score"] = round(
                statistics.fmean(b["structural"] * 0.6 + b.get("coverage", 0) * 0.4 for b in bl), 4
            )
    return summary


def write_report(path: Path, summaries: list[dict], rows_by_suite: dict[str, list[dict]], gate: tuple[bool, str]) -> None:
    lines = ["# Antigravity Parity Report", "", f"_Generated {time.strftime('%Y-%m-%d %H:%M:%S')}_", ""]
    for s in summaries:
        lines += [f"## {s['suite']}", "", "| metric | value |", "|---|---|"]
        for k, v in s.items():
            if k != "suite":
                lines.append(f"| {k} | {v} |")
        lines.append("")
    ok, reason = gate
    lines += ["## Gate", "", f"- **{'PASS' if ok else 'FAIL'}**: {reason}", ""]
    lines += ["## Item detail", ""]
    for suite, rows in rows_by_suite.items():
        lines += [f"### {suite}", "", "| id | gw struct | gw pass | gw cov | bl struct | note |", "|---|---|---|---|---|---|"]
        for r in rows:
            gw = r["gateway"]
            bl = r.get("baseline") or {}
            lines.append(
                f"| {r['id']} | {gw.get('structural')} | {'Y' if gw.get('structuralPass') else 'N'} "
                f"| {gw.get('coverage')} | {bl.get('structural', '-')} | {gw.get('detail', gw.get('error', ''))[:60]} |"
            )
        lines.append("")
    path.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--gateway-url", default="http://localhost:8305")
    ap.add_argument("--gateway-token", default=None)
    ap.add_argument("--baseline-url", default=None)
    ap.add_argument("--baseline-key", default=None)
    ap.add_argument("--baseline-model", default=None)
    ap.add_argument("--suite", choices=["both", "writing", "speaking"], default="both")
    ap.add_argument("--sample", type=int, default=None, help="limit items per suite")
    ap.add_argument("--timeout", type=float, default=90.0)
    ap.add_argument("--report", default=str(HERE / "parity-report.md"))
    args = ap.parse_args()

    suites = []
    if args.suite in ("both", "writing"):
        suites.append(GOLDEN / "writing.json")
    if args.suite in ("both", "speaking"):
        suites.append(GOLDEN / "speaking.json")

    rows_by_suite: dict[str, list[dict]] = {}
    for sf in suites:
        cfg = json.loads(sf.read_text(encoding="utf-8"))
        rows_by_suite[cfg["suite"]] = run_suite(sf, args, args.sample)

    has_baseline = bool(args.baseline_url and args.baseline_key)
    summaries = [summarize(name, rows, has_baseline) for name, rows in rows_by_suite.items()]

    failures = []
    for s in summaries:
        if s["gateway_structural_pass_rate"] < 1.0:
            failures.append(f"{s['suite']}: structural pass rate {s['gateway_structural_pass_rate']} < 1.0")
        if has_baseline and "baseline_mean_score" in s:
            delta = s["gateway_mean_score"] - s["baseline_mean_score"]
            if delta < -0.05:
                failures.append(f"{s['suite']}: mean score {s['gateway_mean_score']} trails baseline {s['baseline_mean_score']} by more than 0.05")
    gate_ok = not failures
    reason = "all suites meet structural + parity thresholds" if gate_ok else "; ".join(failures)
    write_report(Path(args.report), summaries, rows_by_suite, (gate_ok, reason))

    print(json.dumps(summaries, indent=1))
    print(f"GATE {'PASS' if gate_ok else 'FAIL'}: {reason}")
    return 0 if gate_ok else 1


if __name__ == "__main__":
    sys.exit(main())
