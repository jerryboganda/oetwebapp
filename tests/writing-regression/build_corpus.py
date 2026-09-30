"""Assemble the permanent regression corpus from the frozen 22-letter benchmark.

Copies the 7 defect-carrying letters + 4 clean controls into ./corpus, enriched
with scenario metadata (title/profession/letterType) pulled from the frozen
production snapshot, so the harness is self-contained and needs no live API.

Run once (idempotent):
  python build_corpus.py
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(HERE, "corpus")
BENCH = os.path.abspath(os.path.join(HERE, "..", "..", "..",
                                     "_writing-salvage-2026-09-21", "bench_v1"))
INPUTS = os.path.join(BENCH, "grader_inputs")
SNAP = os.path.abspath(os.path.join(BENCH, "..", "prod", "frozen",
                                    "production_writing_snapshot_2026-09-21.json"))
MANIFEST = os.path.join(BENCH, "_private", "manifest.csv")

# The 7 letters carrying the 9 confirmed Opus misses + 4 clean controls.
ORIGINAL_DEFECT_LETTERS = [
    "W5DAF6313D",  # OT    — R1 (no explicit employer request)
    "W6488AA2A5",  # Pharm — R2 (spica cast + removal date)
    "W7D5096080",  # Optom — R3 (uncomplicated surgery) + R6 (dry-eye scattered)
    "W896669994",  # Med   — R4 (regular medicines)
    "WC3A74B35F",  # Nurs  — R5 (excessive inpatient/nursing detail)
    "WE0629D703",  # Physio— R7 (outdated mobility detail)
    "WE359F80DA",  # Radio — R8 (compression/image detail) + R9 (misplaced symptom)
]
CLEAN_CONTROLS = [
    "WB81224E35",  # medicine LT-NM clean control
    "W39AD03DC9",  # medicine LT-RR clean control
    "W8A4E1D972",  # medicine LT-RR clean control
    "WAC14A663C",  # dentistry LT-RR clean control
]


def main():
    snap = {r["scenario"]["Id"]: r["scenario"]
            for r in json.loads(open(SNAP, encoding="utf-8").read())}
    # letterId -> scenarioId from the manifest
    import csv
    letter_to_scen = {r["letterId"]: r["scenarioId"]
                      for r in csv.DictReader(open(MANIFEST, encoding="utf-8"))}

    os.makedirs(CORPUS, exist_ok=True)
    written = 0
    for lid in ORIGINAL_DEFECT_LETTERS + CLEAN_CONTROLS:
        src = os.path.join(INPUTS, lid + ".json")
        inp = json.loads(open(src, encoding="utf-8").read())
        scen = snap[letter_to_scen[lid]]
        rec = {
            "letterId": lid,
            "profession": inp["profession"],
            "letterType": scen["LetterType"],
            "title": scen["Title"],
            "writingTask": inp["writingTask"],
            "todayDate": inp.get("todayDate"),
            "wordGuide": inp.get("wordGuide"),
            "caseNotes": inp["caseNotes"],
            "candidateLetter": inp["candidateLetter"],
            "isCleanControl": lid in CLEAN_CONTROLS,
        }
        with open(os.path.join(CORPUS, lid + ".json"), "w", encoding="utf-8", newline="\n") as fh:
            json.dump(rec, fh, ensure_ascii=False, indent=1)
        written += 1
        print("  wrote %s  (%s %s)  clean=%s" % (lid, rec["profession"], rec["letterType"], rec["isCleanControl"]))
    print("Corpus assembled: %d letters -> %s" % (written, CORPUS))


if __name__ == "__main__":
    main()
