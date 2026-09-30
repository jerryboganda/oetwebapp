"""Self-test for check_regression.py: prove the gates work before trusting them.

Builds synthetic grader outputs in a temp runs/ dir and confirms:
  - an oracle that reports every defect at the right severity PASSES;
  - a blind grader (no findings) FAILS on detection;
  - a downgrader (majors reported as minor) FAILS the severity gate;
  - a clean-control over-caller FAILS the false-positive gate.

Run:  python selftest_checker.py
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
KEY = json.loads(open(os.path.join(HERE, "regression_key.json"), encoding="utf-8").read())


def oracle_findings(d, downgrade=False):
    sev = "minor" if (downgrade and d["severity"] == "major") else d["severity"]
    if d["omission"]:
        # Phrase the way a real grader does: name the omission and, for R1, that it is a
        # missing REQUEST (the checker's omission branch keys on that vocabulary).
        msg = "The letter omits required case-note content and never makes the request: " + (d["originalText"] or "")
        return {"quote": "", "isOmission": True, "severity": sev, "criterionCode": "content",
                "message": msg,
                "fixSuggestion": d["expectedCorrection"]}
    # visible defect: quote the planted letterText and use the rule language
    qt = d.get("letterText") or ""
    rule = d.get("rule", "")
    # The checker matches structural defects (R5-R9) on concept keywords, so the oracle's
    # message must name the defect in the vocabulary the rule actually uses.
    msg = {
        "R5": "Excessive inpatient nursing ward-process detail that the recipient does not need.",
        "R6": "The dry eye information is scattered and should be grouped in one paragraph.",
        "R7": "Outdated, superseded mobility detail from the initial assessment should be removed.",
        "R8": "Technical imaging detail that does not change management (compression/image quality).",
        "R9": "The presenting-symptom sentence is misplaced among the imaging findings.",
    }.get(rule, "Irrelevant / outdated / excessive / misplaced content that should be excluded or moved.")
    return {"quote": qt, "isOmission": False, "severity": sev, "criterionCode": "organisation_layout",
            "message": msg,
            "fixSuggestion": d["expectedCorrection"]}


def write_run(runs_dir, letter_id, findings):
    os.makedirs(runs_dir, exist_ok=True)
    rec = {"letterId": letter_id, "parsed": {"findings": findings,
           "criteriaScores": {k: 3 for k in ("purpose", "content", "conciseness_clarity", "genre_style", "organisation_layout", "language")}}}
    json.dump(rec, open(os.path.join(runs_dir, letter_id + ".json"), "w", encoding="utf-8"))


def run_checker(runs_dir):
    """Invoke check_regression.main() logic against a custom runs dir by monkeypatching RUNS."""
    import check_regression as cr
    cr.RUNS = runs_dir
    # capture sys.exit
    try:
        cr.main.__wrapped__  # not wrapped; call directly
    except AttributeError:
        pass
    import io
    buf = io.StringIO()
    old = sys.stdout
    sys.argv = ["check_regression.py"]
    sys.stdout = buf
    code = 0
    try:
        cr.main()
    except SystemExit as e:
        code = e.code
    finally:
        sys.stdout = old
    return code, buf.getvalue()


def scenario(name, downgrade=False, blind=False, overcall_clean=False):
    tmp = tempfile.mkdtemp()
    by_letter = {}
    for d in KEY["defects"]:
        by_letter.setdefault(d["letterId"], []).append(d)
    for lid, dlist in by_letter.items():
        fs = [] if blind else [oracle_findings(d, downgrade=downgrade) for d in dlist]
        write_run(tmp, lid, fs)
    for lid in KEY["cleanControls"]:
        fs = []
        if overcall_clean:
            fs = [{"quote": "some text", "severity": "major", "criterionCode": "language",
                   "message": "invented problem", "fixSuggestion": "x"}]
        write_run(tmp, lid, fs)
    code, out = run_checker(tmp)
    shutil.rmtree(tmp, ignore_errors=True)
    return code, out


def main():
    checks = [
        ("oracle (expect PASS)", dict(), 0),
        ("blind (expect FAIL)", dict(blind=True), 1),
        ("downgrader (expect FAIL)", dict(downgrade=True), 1),
        ("clean over-caller (expect FAIL)", dict(overcall_clean=True), 1),
    ]
    allok = True
    for name, kw, expect in checks:
        code, out = scenario(name, **kw)
        ok = code == expect
        allok = allok and ok
        print("%-34s -> exit %d  (expected %d)  %s" % (name, code, expect, "OK" if ok else "BAD"))
    print("\nSELF-TEST " + ("PASSED" if allok else "FAILED"))
    sys.exit(0 if allok else 1)


if __name__ == "__main__":
    main()
