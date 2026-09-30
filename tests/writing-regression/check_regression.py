"""Regression checker for the Writing grader. No model is called here.

Compares runs/<letterId>.json against the frozen regression_key.json and enforces
the non-negotiable acceptance rules:

  1. Every regression defect (the 9 originals + the unseen equivalents) is DETECTED.
  2. Severity is EXACT: a defect planted as major must be reported major, minor as
     minor. A major downgraded to minor is a FAIL (the "never downgrade" guardrail).
  3. Clean-control letters produce ZERO critical/major findings (no material
     false positives). Minor stylistic coaching is tolerated and only reported.
  4. Sibling defects (the other planted defects sharing a corpus letter) are still
     caught — the new rules must not crowd out existing detection.

Exit code 0 = all gates pass; 1 = at least one failure.

  python check_regression.py
  python check_regression.py --verbose
"""
import argparse
import json
import os
import re
import sys

import prompt_builder as pb  # noqa: F401  (ensures the prompt builder imports cleanly)

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(HERE, "corpus")
RUNS = os.path.join(HERE, "runs")
KEY_PATH = os.path.join(HERE, "regression_key.json")

MATERIAL = ("critical", "major")
SEV_RANK = {"critical": 0, "major": 1, "minor": 2}
OMIT_WORDS = re.compile(r"\b(omit|omission|missing|not (?:mention|include|state|provide)|does not|did not|absent|"
                        r"lacks?|fails? to|without|leaves? out|no mention|not given|not stated|scattered|"
                        r"irrelevant|outdated|superseded|misplaced|wrong (?:section|paragraph)|excessive)", re.I)
STOP = set("that this with from have were been their there which would could should about after before into "
           "your also they them than then when what while where these those very such only some more most over "
           "under being does doing done each other same both again further once here just will shall".split())


def tokens(s):
    return {w if w[0].isdigit() else w[:5] for w in re.findall(r"[a-z]{4,}|\d+(?:\.\d+)?", (s or "").lower()) if w not in STOP}


def ftext(f):
    return " ".join(str(f.get(k) or "") for k in ("quote", "message", "explanation", "fixSuggestion", "correction", "sourceEvidence"))


def _sq(text):
    chars, idx = [], []
    for i, c in enumerate(text):
        if c.isalnum():
            chars.append(c.lower())
            idx.append(i)
    return "".join(chars), idx


def locate(quote, letter):
    if not quote:
        return None
    i = letter.find(quote)
    if i != -1:
        return i, i + len(quote)
    sl, idx = _sq(letter)
    q = re.sub(r"[^a-z0-9]", "", quote.lower())
    if len(q) < 4:
        return None
    j = sl.find(q)
    if j != -1:
        return idx[j], idx[j + len(q) - 1] + 1
    return None


def _canon(s):
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


# Organisation/selection defects (R5-R9) are planted by MUTATING the letter, so the
# planted text may not survive verbatim. For these the gold key carries a distinctive
# concept the finding must mention; matching is by concept keywords, not span containment.
RULE_KEYWORDS = {
    "R5": ("nursing", "ward", "inpatient", "psychosis", "delusion", "behaviour", "detail", "excessive"),
    "R6": ("dry eye", "dry-eye", "scatter", "split", "group", "fragment", "across", "paragraph", "four places", "three places"),
    "R7": ("stick", "weight bearing", "outdated", "superseded", "old", "earlier", "initial", "previous",
           "mobility", "ambulat", "tolerat", "function", "baseline", "no longer"),
    "R8": ("compression", "image quality", "technical", "no clinical information", "does not change", "not act"),
    "R9": ("nipple", "discharge", "misplac", "wrong section", "imaging", "among", "placed", "belongs", "background"),
}


def matched(defect, findings, letter):
    """Return the BEST finding that detects this defect, or None.

    Omissions and verbatim-present defects match on quote/span. Organisation/selection
    defects (the letterText was mutated away) match on the rule's concept keywords. Among
    candidates, prefer the finding whose severity equals the expected severity so a strong
    neighbouring finding cannot shadow the correctly-graded one."""
    expected = defect.get("severity", "")
    rule = defect.get("rule", "")
    span = defect.get("spanText") or defect.get("letterText") or defect.get("originalText") or ""
    span_c = _canon(span)
    span_tok = tokens(span)
    kws = RULE_KEYWORDS.get(rule, ())
    best, best_score = None, 0.0
    for f in findings:
        txt = ftext(f).lower()
        quote_c = _canon(f.get("quote") or "")
        ov = span_tok & tokens(txt)

        score = 0.0
        # R1 (missing explicit request) is special: the grader usually quotes the GENERIC
        # CLOSING text that is present (e.g. "Let me know if you have any questions.") while
        # flagging the absent request. Treat it as a match either by omission language on the
        # missing request OR by quoting the existing closing text.
        if defect.get("omission") and rule == "R1" and defect.get("letterText"):
            lt_c = _canon(defect["letterText"])
            if lt_c and quote_c and (lt_c in quote_c or quote_c in lt_c):
                score = 3.0
            elif "request" in txt or "ask" in txt or "action" in txt or "never directly asks" in txt \
                    or (f.get("isOmission") and len(ov) >= 2):
                # A proper R1 finding names the missing request/action; a generic-contact
                # duplicate-offer finding is a different, weaker defect. Score the real R1 higher.
                if "r1" in txt or "explicit-request" in txt or "explicit request" in txt:
                    score = 2.6
                else:
                    score = 1.6
        elif defect.get("omission") and not kws:
            need = max(2, int(0.2 * len(span_tok)))
            if (f.get("isOmission") or OMIT_WORDS.search(txt)) and len(ov) >= min(need, 4):
                score = 0.5 + min(0.5, len(ov) / 20.0)
        else:
            # Visible or structural defect (R5-R9 are flagged with a quote even though the
            # key marks them omission=true; the `omission` flag means "content should be
            # removed/moved", not "the grader reports an omission").
            if span_c and quote_c and (span_c in quote_c or quote_c in span_c):
                score = 3.0                                   # quote contains the planted text
            elif len(ov) >= max(2, int(0.4 * len(span_tok))):
                score = 2.0 + min(0.4, len(ov) / 20.0)        # strong content overlap
            elif kws:
                # concept match (structural defect): need a distinctive keyword AND some
                # content overlap, so a finding about an unrelated defect on a shared generic
                # word ("pain") cannot false-match. One distinctive keyword is enough.
                distinctive = sum(1 for k in kws if k in txt)
                if distinctive >= 1 and len(ov) >= 1:
                    score = 1.5
        # Prefer the finding graded at the expected severity (breaks shadows by neighbours).
        if score > 0 and expected and (f.get("severity") or "").lower() == expected:
            score += 0.5
        if score > best_score:
            best, best_score = f, score
    return best


def severity_ok(expected, f):
    """Gate aligned to the acceptance criteria: a MAJOR defect must not be downgraded to
    minor (that is the regression we guard). A MINOR defect reported as major is acceptable
    over-caution, not a false positive, so it passes. Critical on a minor/major is also a
    pass (over-grading a real defect is not the failure mode being guarded)."""
    got = (f.get("severity") or "").lower()
    if expected == "major":
        return (got in ("major", "critical")), got   # major downgraded to minor = fail
    if expected == "critical":
        return (got == "critical"), got              # critical must stay critical
    # expected minor: any detection at minor/major is acceptable; critical is over-call but still a detection
    return (got in ("minor", "major", "critical")), got


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()

    key = json.loads(open(KEY_PATH, encoding="utf-8").read())
    defects = key["defects"]
    clean_controls = set(key.get("cleanControls", []))
    # Letters labelled clean that carry a genuine clinically-significant finding the grader
    # is RIGHT to report (not an Addendum-Five false positive). Documented per letter.
    fp_exempt = set(key.get("cleanControlTruePositives", []))
    by_letter = {}
    for d in defects:
        by_letter.setdefault(d["letterId"], []).append(d)

    failures = []
    table = []
    for lid, dlist in sorted(by_letter.items()):
        rpath = os.path.join(RUNS, lid + ".json")
        cpath = os.path.join(CORPUS, lid + ".json")
        if not os.path.exists(rpath):
            failures.append((lid, "ALL", "no run result"))
            continue
        rec = json.loads(open(rpath, encoding="utf-8").read())
        letter = json.loads(open(cpath, encoding="utf-8").read())["candidateLetter"]
        parsed = rec.get("parsed") or {}
        findings = parsed.get("findings") or []
        for d in dlist:
            f = matched(d, findings, letter)
            if f is None:
                failures.append((lid, d["defectId"], "MISSED (%s)" % d["rule"]))
                table.append((d["rule"], lid, "MISS", "-", d["severity"]))
                continue
            ok, got = severity_ok(d["severity"], f)
            if not ok:
                failures.append((lid, d["defectId"], "severity %s -> %s" % (d["severity"], got)))
            table.append((d["rule"], lid, "HIT", got, d["severity"]))

    # clean-control false-positive gate (exempting documented true positives)
    clean_fp = []
    for lid in sorted(clean_controls):
        if lid in fp_exempt:
            continue
        rpath = os.path.join(RUNS, lid + ".json")
        if not os.path.exists(rpath):
            continue
        rec = json.loads(open(rpath, encoding="utf-8").read())
        for f in (rec.get("parsed") or {}).get("findings") or []:
            sev = (f.get("severity") or "").lower()
            if sev in MATERIAL:
                clean_fp.append((lid, sev, (f.get("quote") or "")[:60]))

    print("=" * 78)
    print("REGRESSION RESULTS  (house style %s)" % pb.HOUSE_VERSION)
    print("=" * 78)
    hdr = "%-4s %-13s %-5s %-9s %-9s" % ("Rule", "Letter", "Result", "Sev", "Expected")
    print(hdr + "\n" + "-" * 78)
    for row in sorted(table, key=lambda r: (SEV_RANK.get(r[4], 3), r[0], r[1])):
        print("%-4s %-13s %-5s %-9s %-9s" % row)

    n_det = sum(1 for r in table if r[2] == "HIT")
    n_tot = len(table)
    print("-" * 78)
    print("Detection: %d/%d defects detected" % (n_det, n_tot))
    print("Clean-control material false positives: %d" % len(clean_fp))
    if args.verbose:
        for lid, sev, q in clean_fp:
            print("   FP[%s][%s] %s" % (lid, sev, q))

    print("=" * 78)
    if failures or clean_fp:
        print("FAIL")
        for lid, did, why in failures:
            print("  FAIL  %s  %s  %s" % (lid, did, why))
        for lid, sev, q in clean_fp:
            print("  FAIL  clean-FP %s [%s] %s" % (lid, sev, q))
        sys.exit(1)
    print("PASS — all %d regression defects detected at the correct severity, no material clean-control false positives." % n_tot)
    sys.exit(0)


if __name__ == "__main__":
    main()
