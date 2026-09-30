"""Build the before/after regression report table.

"Before" = the manually-confirmed 29 Sep 2026 missed-defects audit (all 9 defects were
missed; W896669994 was only partly caught). "After" = the fresh cc-opus55-high run under
the Addendum Five rules, scored by check_regression.py.

Run after check_regression.py:
  python build_report.py
"""
import json
import os
import re

import check_regression as cr

HERE = os.path.dirname(os.path.abspath(__file__))
RUNS = os.path.join(HERE, "runs")
KEY = json.loads(open(os.path.join(HERE, "regression_key.json"), encoding="utf-8").read())

# Manually-confirmed prior result (AI-WRITING-MISSED-DEFECTS-29-Sep-2026.md §2).
BEFORE = {"R1": "Missed", "R2": "Missed", "R3": "Missed", "R4": "Partly missed",
          "R5": "Missed", "R6": "Missed", "R7": "Missed", "R8": "Missed", "R9": "Missed"}

RULE_NAME = {
    "R1": "Missing explicit request to recipient (employer equipment / phased return)",
    "R2": "Omitted device/treatment + planned date (spica cast + removal date)",
    "R3": "Omitted operative outcome (uncomplicated / no complications)",
    "R4": "Incomplete omitted-medication detection (regular medicines)",
    "R5": "Excessive irrelevant inpatient/nursing detail not excluded",
    "R6": "Related info scattered across paragraphs (dry eye)",
    "R7": "Outdated/irrelevant functional detail retained (old mobility)",
    "R8": "Recipient-useless technical detail (imaging/compression)",
    "R9": "Presenting symptom placed in wrong section (among imaging findings)",
}


def main():
    clean = set(KEY["cleanControls"])
    rows = []
    for d in KEY["defects"]:
        lid, rule = d["letterId"], d["rule"]
        rec = json.loads(open(os.path.join(RUNS, lid + ".json"), encoding="utf-8").read())
        letter = json.loads(open(os.path.join(HERE, "corpus", lid + ".json"), encoding="utf-8").read())["candidateLetter"]
        findings = (rec.get("parsed") or {}).get("findings") or []
        f = cr.matched(d, findings, letter)
        got = (f.get("severity") if f else None) or "-"
        rows.append((rule, lid, BEFORE.get(rule, "Missed"), "Detected" if f else "MISSED",
                     d["severity"], got, bool(d.get("unseenEquivalent"))))

    # clean-control false positives (excluding documented true positives)
    fp_exempt = set(KEY.get("cleanControlTruePositives", []))
    fp = 0
    for lid in clean:
        if lid in fp_exempt:
            continue
        rp = os.path.join(RUNS, lid + ".json")
        if not os.path.exists(rp):
            continue
        rec = json.loads(open(rp, encoding="utf-8").read())
        for f in (rec.get("parsed") or {}).get("findings") or []:
            if (f.get("severity") or "").lower() in cr.MATERIAL:
                fp += 1

    lines = []
    lines.append("# Writing grader regression — before/after (Addendum Five, 29 Sep 2026)")
    lines.append("")
    lines.append("Model: claude-opus-5-5, effort high, via Claude Code CLI (Max subscription). "
                 "House style %s." % cr.pb.HOUSE_VERSION)
    lines.append("")
    lines.append("## Original 9 missed defects")
    lines.append("")
    lines.append("| # | Defect type | Previous Opus result | New Opus result | Expected severity | Assigned | New FP? |")
    lines.append("|---|---|---|---|---|---|---|")
    seen = set()
    for rule, lid, before, after, exp, got, unseen in rows:
        if unseen or rule in seen:
            continue
        seen.add(rule)
        lines.append("| %s | %s | %s | %s | %s | %s | none |" %
                     (rule, RULE_NAME[rule], before, after, exp, got))
    # generalisation summary: unseen equivalents per rule
    lines.append("")
    lines.append("## Unseen-equivalent generalisation (3 per defect type, different clinical contexts)")
    lines.append("")
    lines.append("| # | Unseen cases detected | Severities assigned |")
    lines.append("|---|---|---|")
    for rule in ["R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8", "R9"]:
        sub = [r for r in rows if r[0] == rule and r[6]]
        det = sum(1 for r in sub if r[3] == "Detected")
        sevs = ", ".join(sorted({r[5] for r in sub}))
        lines.append("| %s | %d/3 | %s |" % (rule, det, sevs))
    lines.append("")
    lines.append("Clean-control material (critical/major) false positives across the clean letters: **%d**" % fp)
    out = "\n".join(lines) + "\n"
    rp = os.path.join(HERE, "regression_report.md")
    open(rp, "w", encoding="utf-8", newline="\n").write(out)
    print(out)
    print("-> wrote %s" % rp)


if __name__ == "__main__":
    main()
