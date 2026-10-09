#!/usr/bin/env python3
"""Regenerate features.csv and FEATURE_TRACEABILITY_MATRIX.md from features.json.

Installed for the F-015/F-016/F-017 supersession (owner directive 2026-10-09).
The JSON is the single source of truth; the CSV and Markdown matrix are derived
views. Before this script the two were hand-maintained alongside the JSON, which
is how a status could disagree between them.

Stdlib only. Repo-relative paths, per the repo rule that working files do not
live at the repository root.
"""
import csv
import json
from pathlib import Path

TRACE = Path(__file__).resolve().parents[2] / 'docs' / 'ai-learning-companion' / 'traceability'

COLUMNS = [
    'id', 'requirement', 'category', 'source_section', 'source_decision',
    'implementation_intent', 'owner', 'estimate_days', 'phase', 'dependencies',
    'implementation_status', 'notes', 'repo_evidence', 'repo_gap',
    'repo_stage', 'risk',
]


def main() -> int:
    with (TRACE / 'features.json').open(encoding='utf-8') as f:
        doc = json.load(f)
    features = doc['features']

    # ---- CSV -------------------------------------------------------------
    # utf-8-sig and CRLF to match the existing file, which the validator reads
    # back with encoding='utf-8-sig'.
    with (TRACE / 'features.csv').open('w', encoding='utf-8-sig', newline='') as f:
        w = csv.DictWriter(f, fieldnames=COLUMNS, lineterminator='\r\n')
        w.writeheader()
        for feat in features:
            w.writerow({c: feat.get(c, '') for c in COLUMNS})

    # ---- Markdown matrix -------------------------------------------------
    lines = [
        '# Feature Traceability Matrix — F-001..F-184',
        '',
        'Generated from `features.json`. Edits belong in the JSON, then re-run',
        '`scripts/ai-learning-companion/render_traceability.py`; do not hand-edit this file.',
        '',
        '| ID | Requirement | Status | Phase | Owner | Dependencies | Evidence | Notes / gap |',
        '| --- | --- | --- | --- | --- | --- | --- | --- |',
    ]
    for feat in features:
        notes_gap = feat.get('notes', '')
        if feat.get('repo_gap'):
            notes_gap = f"{notes_gap} · {feat['repo_gap']}" if notes_gap else feat['repo_gap']
        cells = [
            feat.get('id', ''),
            feat.get('requirement', ''),
            feat.get('implementation_status', ''),
            feat.get('phase', ''),
            feat.get('owner', ''),
            feat.get('dependencies', ''),
            feat.get('repo_evidence', ''),
            notes_gap,
        ]
        # Escape pipes so a value cannot break the table, and flatten newlines.
        safe = [str(c).replace('|', r'\|').replace('\n', ' ').strip() for c in cells]
        lines.append('| ' + ' | '.join(safe) + ' |')

    (TRACE / 'FEATURE_TRACEABILITY_MATRIX.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')

    print(f"regenerated features.csv and FEATURE_TRACEABILITY_MATRIX.md from {len(features)} features")
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
