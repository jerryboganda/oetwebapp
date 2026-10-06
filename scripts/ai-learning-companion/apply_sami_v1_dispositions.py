#!/usr/bin/env python3
"""Apply the 2026-10-07 owner decisions (DECISION_LOG D-002/D-003/D-006) to the
traceability register. Idempotent: safe to run again. Validates after writing."""
from pathlib import Path
import csv, json, subprocess, sys

ROOT = Path(__file__).resolve().parents[2]
TRACE = ROOT / 'docs' / 'ai-learning-companion' / 'traceability'

D6 = 'OWNER D-006 2026-10-07: in scope (Wave 6) — "literally everything" supersedes FUTURE/not-a-blocker sequencing.'
NA = 'OWNER D-006 2026-10-07: NOT_APPLICABLE_WITH_REASON lifted; reopened as MISSING. Prior reason preserved in repo_gap.'
PDF_POST_BETA = 'SAMI PDF §16: POST-BETA for controlled beta; still built per D-006, sequenced after beta scope (Wave 6).'
PDF_EQUIV = 'SAMI PDF §16: EQUIVALENT ACCEPTABLE — desktop web/PWA satisfies the current handover.'

# id -> (new_status, note_append)
EDITS = {}
for fid in [f'F-{i}' for i in range(168, 185)]:
    EDITS[fid] = (None, D6)
for fid in ['F-174', 'F-175', 'F-176', 'F-180']:
    EDITS[fid] = ('MISSING', NA)
for fid in ['F-084', 'F-117', 'F-118', 'F-120', 'F-121', 'F-122']:
    EDITS[fid] = (None, PDF_POST_BETA)
EDITS['F-164'] = (None, PDF_EQUIV)
EDITS['F-165'] = (None, PDF_EQUIV)

with (TRACE / 'features.json').open(encoding='utf-8') as f:
    data = json.load(f)

changed = 0
for feat in data['features']:
    fid = feat['id']
    if fid in EDITS:
        new_status, note = EDITS[fid]
        if new_status and feat.get('implementation_status') != new_status:
            feat['implementation_status'] = new_status
        if note not in (feat.get('notes') or ''):
            feat['notes'] = ((feat.get('notes') or '') + ' | ' + note).strip(' |')
            changed += 1

(TRACE / 'features.json').write_text(
    json.dumps(data, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')

with (TRACE / 'features.csv').open(encoding='utf-8-sig', newline='') as f:
    reader = csv.DictReader(f)
    fieldnames = reader.fieldnames
    rows = list(reader)

for row in rows:
    fid = row.get('id')
    if fid in EDITS:
        new_status, note = EDITS[fid]
        if new_status and row['implementation_status'] != new_status:
            row['implementation_status'] = new_status
        if note not in (row.get('notes') or ''):
            row['notes'] = ((row.get('notes') or '') + ' | ' + note).strip(' |')

with (TRACE / 'features.csv').open('w', encoding='utf-8', newline='') as f:
    writer = csv.DictWriter(f, fieldnames=fieldnames)
    writer.writeheader()
    writer.writerows(rows)

result = subprocess.run([sys.executable, str(ROOT / 'scripts' / 'ai-learning-companion' / 'validate_traceability.py')],
                        capture_output=True, text=True)
print(f'notes touched on {changed} rows')
print(result.stdout, result.stderr)
sys.exit(result.returncode)
