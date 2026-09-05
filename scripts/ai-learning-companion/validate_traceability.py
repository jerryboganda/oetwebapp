#!/usr/bin/env python3
"""Validate that F-001..F-184 exist exactly once in the machine/human inventories.

Installed from the AI Learning Companion pack. Paths are repo-relative:
traceability lives under docs/ai-learning-companion/traceability/ (repo rules
forbid working files at the repository root).
"""
from pathlib import Path
import csv, json, re, sys

ROOT = Path(__file__).resolve().parents[2]
TRACE = ROOT / 'docs' / 'ai-learning-companion' / 'traceability'

expected = [f'F-{i:03d}' for i in range(1, 185)]
errors = []

with (TRACE / 'features.json').open(encoding='utf-8') as f:
    data = json.load(f)
ids = [x.get('id') for x in data.get('features', [])]
if ids != expected:
    missing = [x for x in expected if x not in ids]
    dup = sorted({x for x in ids if ids.count(x) > 1})
    errors.append(f'JSON mismatch: count={len(ids)} missing={missing} duplicates={dup}')
if data.get('feature_count') != 184:
    errors.append(f"JSON feature_count={data.get('feature_count')} not 184")

with (TRACE / 'features.csv').open(encoding='utf-8-sig', newline='') as f:
    rows = list(csv.DictReader(f))
csv_ids = [r.get('id') for r in rows]
if csv_ids != expected:
    errors.append(f'CSV inventory/order mismatch; count={len(csv_ids)}')

md = (TRACE / 'FEATURE_TRACEABILITY_MATRIX.md').read_text(encoding='utf-8')
md_ids = re.findall(r'\|\s*(F-\d{3})\s*\|', md)
for fid in expected:
    if md_ids.count(fid) != 1:
        errors.append(f'Markdown matrix {fid} occurs {md_ids.count(fid)} times as a table ID')

if errors:
    print('TRACEABILITY VALIDATION: FAIL')
    for e in errors:
        print('-', e)
    sys.exit(1)
print('TRACEABILITY VALIDATION: PASS - F-001 through F-184 are present exactly once in JSON/CSV/Markdown matrix.')
