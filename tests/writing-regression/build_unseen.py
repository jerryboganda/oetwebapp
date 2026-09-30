"""Author the 27 unseen-equivalent regression cases (3 per defect type R1-R9).

Each case takes a CLEAN control letter, plants ONE defect of the target type in a
different clinical context via a deterministic string edit, writes the mutated letter to
corpus/<host>-unseen-<rule>-<n>.json, and appends a matching entry to regression_key.json.
Re-running regenerates identical files (idempotent: unseen entries are cleared first).

This proves the Addendum Five rules detect each defect TYPE in letters they have never
seen — not merely the original benchmark letters. Hosts are the 4 clean controls so the
only planted defect is the one under test.

Run once:  python build_unseen.py
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(HERE, "corpus")
KEY_PATH = os.path.join(HERE, "regression_key.json")

SEV = {"R1": "major", "R2": "major", "R3": "major", "R4": "major", "R5": "major",
       "R6": "minor", "R7": "minor", "R8": "minor", "R9": "minor"}

HOSTS = ["WB81224E35", "W39AD03DC9", "W8A4E1D972", "WAC14A663C"]


def load(lid):
    return json.loads(open(os.path.join(CORPUS, lid + ".json"), encoding="utf-8").read())


def emit(key, host, rule, idx, new_text, subcategory, original, letter_text, expected, omission, extra_notes=None):
    nid = "%s-unseen-%s-%d" % (host["letterId"], rule.lower(), idx)
    rec = {k: host[k] for k in ("profession", "letterType", "title", "writingTask", "todayDate", "wordGuide", "caseNotes")}
    rec["caseNotes"] = list(host["caseNotes"]) + (extra_notes or [])
    rec["letterId"] = nid
    rec["candidateLetter"] = new_text
    rec["isCleanControl"] = False
    with open(os.path.join(CORPUS, nid + ".json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(rec, fh, ensure_ascii=False, indent=1)
    key["defects"].append({
        "defectId": "unseen-%s-%d" % (rule.lower(), idx), "rule": rule, "letterId": nid,
        "severity": SEV[rule], "omission": omission, "subcategory": subcategory,
        "originalText": original, "letterText": letter_text, "expectedCorrection": expected,
        "unseenEquivalent": True})


def remove_request(letter, request_substr, generic):
    """R1: drop the explicit 'I would be grateful ...' request sentence; add a generic closing."""
    out = [ln for ln in letter.split("\n") if request_substr.lower() not in ln.lower()]
    text = "\n".join(out)
    marker = "Should there be any queries"
    i = text.find(marker)
    if i == -1:
        return text.rstrip() + "\n\n" + generic + "\n"
    return text[:i].rstrip() + "\n\n" + generic + "\n\n" + text[i:]


def main():
    key = json.loads(open(KEY_PATH, encoding="utf-8").read())
    key["defects"] = [d for d in key["defects"] if not d.get("unseenEquivalent")]
    for fn in list(os.listdir(CORPUS)):
        if "-unseen-" in fn:
            os.remove(os.path.join(CORPUS, fn))
    hosts = {lid: load(lid) for lid in HOSTS}
    n = 0

    # R1 — missing explicit request
    r1 = [
        ("WB81224E35", "provide wound dressing", "Please let me know if you need anything further."),
        ("W39AD03DC9", "arrange an MRI", "I hope this information is helpful."),
        ("W8A4E1D972", "advise on encouraging lifestyle changes", "Do contact me if you have any concerns."),
    ]
    for i, (hid, req, gen) in enumerate(r1, 1):
        h = hosts[hid]
        emit(key, h, "R1", i, remove_request(h["candidateLetter"], req, gen),
             "missing explicit request (%s)" % req, "an explicit request to " + req, gen,
             "an explicit request to " + req, True)
        n += 1

    # R2 — omitted device/treatment + planned date (device added to notes, omitted from letter)
    r2 = [
        ("WB81224E35", "Ms White also has a removable wrist splint, due for removal at her review on 30 May 2019."),
        ("W8A4E1D972", "Ms Bennet was fitted with a Holter monitor, to be removed at her appointment on 22 May 2021."),
        ("W39AD03DC9", "Mr Poulos was fitted with a lumbar brace, due for review and removal on 19 July 2014."),
    ]
    for i, (hid, note) in enumerate(r2, 1):
        h = hosts[hid]
        emit(key, h, "R2", i, h["candidateLetter"], "omitted device/treatment + planned date",
             note, None, note, True, extra_notes=[note])
        n += 1

    # R3 — omitted procedure outcome
    r3 = [
        ("WB81224E35", "The skin graft procedure was uncomplicated, with no operative complications."),
        ("W8A4E1D972", "The stent insertion was uncomplicated and tolerated well."),
        ("WAC14A663C", "The extraction was completed without complications."),
    ]
    for i, (hid, note) in enumerate(r3, 1):
        h = hosts[hid]
        emit(key, h, "R3", i, h["candidateLetter"], "omitted procedure outcome",
             note, None, note, True, extra_notes=[note])
        n += 1

    # R4 — omitted regular medicines
    r4 = [
        ("WB81224E35", "Her regular medicines also include metformin 500 mg twice daily and ramipril 5 mg daily."),
        ("W39AD03DC9", "His regular medicines are naproxen 500 mg twice daily and omeprazole 20 mg daily."),
        ("W8A4E1D972", "Her regular medicines also include aspirin 75 mg daily and atorvastatin 20 mg at night."),
    ]
    for i, (hid, note) in enumerate(r4, 1):
        h = hosts[hid]
        emit(key, h, "R4", i, h["candidateLetter"], "omitted regular medicines",
             note, None, note, True, extra_notes=[note])
        n += 1

    # R5 — excessive inpatient/nursing minutiae
    r5frag = (" On the ward the nurses checked her observations every four hours, helped her with washing "
              "and dressing, walked with her to the toilet, monitored her food intake and repositioned her "
              "every two hours to prevent pressure areas.")
    for i, hid in enumerate(["WB81224E35", "W8A4E1D972", "W39AD03DC9"], 1):
        h = hosts[hid]
        t = h["candidateLetter"]
        j = t.find("I would be grateful")
        txt = (t[:j].rstrip() + "." + r5frag + "\n\n" + t[j:]) if j != -1 else (t.rstrip() + r5frag)
        emit(key, h, "R5", i, txt, "excessive inpatient/nursing minutiae",
             "(exclude routine ward-process detail)", r5frag.strip(), "(exclude the ward-process detail)", False)
        n += 1

    # R6 — same topic scattered across paragraphs. Use a topic the notes ALREADY support
    # (hypertension) and split it across FOUR separated paragraphs so the fragmentation is
    # unambiguous. Content is note-grounded; only the organisation is wrong.
    r6 = [
        ("WB81224E35", "hypertension"),
        ("W39AD03DC9", "hypertension"),
        ("W8A4E1D972", "hypertension"),
    ]
    for i, (hid, topic) in enumerate(r6, 1):
        h = hosts[hid]
        paras = h["candidateLetter"].split("\n\n")
        if len(paras) >= 6:
            paras[1] = paras[1].rstrip() + " Her blood pressure has been well controlled."
            paras[2] = paras[2].rstrip() + " Her blood pressure was last checked last month."
            paras[3] = paras[3].rstrip() + " Her blood pressure remains stable today."
            paras[-2] = paras[-2] + " Please continue to monitor her blood pressure."
        emit(key, h, "R6", i, "\n\n".join(paras), "same topic (blood pressure) scattered across four paragraphs",
             "group the %s detail in one place" % topic, None,
             "group the %s detail in one place" % topic, True,
             extra_notes=["Her blood pressure is well controlled and stable; it was last checked last month and should be monitored."])
        n += 1

    # R7 — superseded/outdated functional detail
    r7stale = " At her first assessment she was walking with a frame and could manage only a few steps."
    for i, hid in enumerate(["WB81224E35", "W39AD03DC9", "W8A4E1D972"], 1):
        h = hosts[hid]
        notes = ["At the first assessment six weeks ago she was walking with a frame and could manage only a few steps.",
                 "She now walks independently without any aid."]
        paras = h["candidateLetter"].split("\n\n")
        if len(paras) >= 3:
            paras[2] = paras[2].rstrip() + r7stale
        emit(key, h, "R7", i, "\n\n".join(paras), "outdated functional detail (frame-walking, now superseded)",
             "the superseded frame-walking detail", r7stale.strip(), "(delete the superseded detail)", False,
             extra_notes=notes)
        n += 1

    # R8 — recipient-useless technical detail
    r8frag = " The image quality was adequate and the exposure parameters were within normal limits."
    for i, hid in enumerate(["W39AD03DC9", "WB81224E35", "WAC14A663C"], 1):
        h = hosts[hid]
        paras = h["candidateLetter"].split("\n\n")
        if len(paras) >= 3:
            paras[2] = paras[2].rstrip() + r8frag
        emit(key, h, "R8", i, "\n\n".join(paras), "irrelevant technical detail (image quality/exposure)",
             "(delete)", r8frag.strip(), "(delete)", False)
        n += 1

    # R9 — presenting-symptom sentence placed inside the findings section. Use a symptom the
    # notes ALREADY record so it is a placement error, not an invented fact.
    r9 = [
        ("W39AD03DC9", "He has no chest pain, breathlessness or palpitations.",
         "No chest pain, breathlessness or palpitations."),
        ("W8A4E1D972", "She has no chest pain, breathlessness or palpitations.",
         "No chest pain, breathlessness or palpitations."),
        ("WB81224E35", "She has no chest pain, breathlessness or palpitations.",
         "No chest pain, breathlessness or palpitations."),
    ]
    for i, (hid, sentence, note) in enumerate(r9, 1):
        h = hosts[hid]
        paras = h["candidateLetter"].split("\n\n")
        if len(paras) >= 4:
            paras[2] = paras[2].rstrip() + " " + sentence
        emit(key, h, "R9", i, "\n\n".join(paras), "presenting-symptom sentence placed inside the findings section",
             "move the symptom history to the presenting/background paragraph", sentence,
             "move the symptom history to the presenting/background paragraph", False,
             extra_notes=[note])
        n += 1

    json.dump(key, open(KEY_PATH, "w", encoding="utf-8", newline="\n"), ensure_ascii=False, indent=1)
    print("authored %d unseen equivalents -> corpus/ + regression_key.json" % n)


if __name__ == "__main__":
    main()
