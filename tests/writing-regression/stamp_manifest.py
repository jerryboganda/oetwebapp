"""Write the committed regression manifest: one SHA-256 per (profession, letterType)
rendered prompt, plus the checker outcome. CI recomputes these hashes from the CURRENT
prompt source and fails the deploy gate if a prompt/rules/model change is not backed by
a fresh passing run recorded here.

Run AFTER check_regression.py passes:
  python stamp_manifest.py --report-last-run
"""
import argparse
import datetime
import hashlib
import json
import os
import subprocess
import sys

import prompt_builder as pb

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(HERE, "corpus")
RUNS = os.path.join(HERE, "runs")
MANIFEST = os.path.join(HERE, "manifest.json")
REPORT = os.path.join(HERE, "regression_report.md")


def git_sha():
    try:
        return subprocess.run(["git", "rev-parse", "HEAD"], capture_output=True, text=True,
                              cwd=os.path.join(HERE, "..", "..")).stdout.strip()
    except Exception:
        return "unknown"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--report-last-run", action="store_true",
                    help="assert every corpus letter has a passing run under the current prompt hash")
    args = ap.parse_args()

    pairs = {}
    for fn in sorted(os.listdir(CORPUS)):
        if fn.endswith(".json"):
            rec = json.loads(open(os.path.join(CORPUS, fn), encoding="utf-8").read())
            pairs[(rec["profession"], rec["letterType"])] = pb.prompt_sha256(rec["profession"], rec["letterType"])

    prompt_hashes = {"%s|%s" % k: v for k, v in sorted(pairs.items())}

    runs_ok = True
    per_letter = {}
    if args.report_last_run:
        for fn in sorted(os.listdir(CORPUS)):
            if not fn.endswith(".json"):
                continue
            lid = fn[:-5]
            rec = json.loads(open(os.path.join(CORPUS, fn), encoding="utf-8").read())
            want = pb.prompt_sha256(rec["profession"], rec["letterType"])
            rp = os.path.join(RUNS, lid + ".json")
            if not os.path.exists(rp):
                runs_ok = False
                per_letter[lid] = "no run"
                continue
            run = json.loads(open(rp, encoding="utf-8").read())
            got = run.get("promptSha256")
            good = run.get("parsed") is not None and got == want
            per_letter[lid] = "ok" if good else "stale-or-failed (sha %s)" % (got or "none")[:12]
            runs_ok = runs_ok and good

    manifest = {
        "version": "writing-regression.v1",
        "generatedAt": datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
        "gitSha": git_sha(),
        "houseStyleVersion": pb.HOUSE_VERSION,
        "model": "claude-opus-5-5",
        "effort": "high",
        "transport": "claude -p (Max subscription)",
        "promptSha256": prompt_hashes,
        "runsPass": runs_ok,
        "perLetter": per_letter,
    }
    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(manifest, fh, ensure_ascii=False, indent=1)

    print("manifest -> %s" % MANIFEST)
    for k, v in prompt_hashes.items():
        print("  %-34s %s" % (k, v[:16]))
    print("houseStyleVersion: %s   gitSha: %s" % (pb.HOUSE_VERSION, manifest["gitSha"][:12]))
    if args.report_last_run:
        print("runsPass: %s" % runs_ok)
        if not runs_ok:
            for lid, st in per_letter.items():
                if st != "ok":
                    print("  NOT-OK  %s  %s" % (lid, st))
            sys.exit(1)


if __name__ == "__main__":
    main()
