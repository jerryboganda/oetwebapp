import re, json, pathlib, sys

src = pathlib.Path(r"C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\_extracted\recalls-2023-2025.txt")
dst = pathlib.Path(r"C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\backend\src\OetLearner.Api\Data\SeedData\recall-sets\recalls-2023-2025.json")

text = src.read_text(encoding="utf-8")

# Skip headers / page markers / copyright lines
SKIP_PATTERNS = [
    re.compile(r"^----- PAGE \d+ -----$"),
    re.compile(r"^RECENT UPDATED LISTENING Recalls", re.IGNORECASE),
    re.compile(r"^N\.B[: ]"),
    re.compile(r"^All Copyrights", re.IGNORECASE),
    re.compile(r"^The recalls are not", re.IGNORECASE),
]

def is_skip(line: str) -> bool:
    for p in SKIP_PATTERNS:
        if p.search(line):
            return True
    return False

# Strip leading list numbering like "1-", "12 -", "23 -", "(1)", "1." etc.
LEADING_NUMBER = re.compile(r"^\s*\(?\d+\)?\s*[-.\)]\s*")

terms = []
seen = set()
for raw in text.splitlines():
    line = raw.strip()
    if not line or is_skip(line):
        continue
    # Drop leading numbering
    line = LEADING_NUMBER.sub("", line).strip()
    if not line:
        continue
    # Strip trailing punctuation (keep internal punctuation like parens for context)
    cleaned = re.sub(r"[\u2013\u2014]", "-", line)  # en/em dash -> hyphen
    cleaned = cleaned.strip(" \t-:.,;")
    if not cleaned or len(cleaned) < 2:
        continue
    # Lowercase canonical key for dedup
    key = re.sub(r"\s+", " ", cleaned).strip().lower()
    if key in seen:
        continue
    seen.add(key)
    terms.append(key)

terms.sort()

pack = {
    "schemaVersion": 1,
    "recallSetCode": "2023-2025",
    "examTypeCode": "OET",
    "displayName": "January 2023 \u2014 End of 2025",
    "sourceProvenance": "Extracted verbatim from 'RECENT UPDATED LISTENING Recalls From JANUARY 2023 Till The End of 2025.pdf' (164 pages). Numbering prefixes stripped, deduped lowercase. Some entries are short sentences/phrases captured as recall items; admin can refine via Content Hub.",
    "terms": terms,
}

dst.write_text(json.dumps(pack, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"WROTE {len(terms)} unique terms -> {dst}")
