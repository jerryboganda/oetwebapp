#!/usr/bin/env python3
"""Consolidate UAT execution records into the §17.2 register (Markdown).

Mechanical triage only: responses captured verbatim, obvious failures flagged
(busy/exhausted/error), persona/legacy-name checks applied where applicable.
The §17.1 PASS/PARTIAL PASS/FAIL judgement per scenario stays with the human
reviewer — this tool never grades.

Usage: python consolidate_uat.py [results-dir] [output.md]
"""
from pathlib import Path
import json, sys, re

root = Path(__file__).resolve().parents[2]
results_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else root / 'docs' / 'ai-learning-companion' / 'uat' / 'results'
out_path = Path(sys.argv[2]) if len(sys.argv) > 2 else results_dir / 'UAT-REGISTER.md'

files = sorted(results_dir.glob('uat-execution-*.json'))
# Latest record per test number wins (later runs supersede earlier ones).
latest = {}
for f in files:
    for rec in json.loads(f.read_text(encoding='utf-8')):
        key = rec['testNumber']
        if key not in latest or rec.get('threadId') and f.stat().st_mtime >= latest[key]['_file_mtime']:
            rec['_file_mtime'] = f.stat().st_mtime
            latest[key] = rec

rows = []
problems = 0
for key in sorted(latest):
    rec = latest[key]
    resp = rec.get('actualResponse') or ''
    flags = []
    if not resp:
        flags.append('NO RESPONSE')
        problems += 1
    if 'busy at the moment' in resp.lower():
        flags.append('PROVIDER BUSY (retest needed)')
        problems += 1
    if 'exhausted' in resp.lower() and 'daily ai credits exhausted' in resp.lower():
        flags.append('QUOTA EXHAUSTED (retest needed)')
        problems += 1
    if rec.get('error'):
        flags.append(f"ERROR {rec['error'][:60]}")
        problems += 1
    if rec.get('prompt', '').lower().startswith('hi. before we start, what is your name'):
        if re.search(r'\bjana\b', resp, re.IGNORECASE):
            flags.append('LEGACY PERSONA LEAK — CRITICAL')
            problems += 1
        if 'Sami' not in resp:
            flags.append('PERSONA NAME MISSING')
            problems += 1
    rows.append((key, rec, flags))

lines = [
    '# SAMI UAT Execution Register (§17.2)', '',
    f'> Consolidated from {len(files)} execution runs; the newest record per test number wins.',
    '> Status (§17.1) is NOT assigned here — the reviewer judges each record against',
    '> the pack PASS CHECK. Mechanical flags below are triage aids only.', '',
]
current_pack = None
for key, rec, flags in rows:
    pack = key.split(' - ')[0]
    if pack != current_pack:
        current_pack = pack
        lines += [f'## {pack}', '', '| Test | Tag | Latency | Flags | Response (verbatim, first 400 chars) |', '|---|---|---|---|---|']
    resp = (rec.get('actualResponse') or '(none)').replace('|', '\\|').replace('\n', ' ')[:400]
    lines.append(f"| {key} | {rec.get('tag','')} | {rec.get('latencyMs',0)/1000:.0f}s | {'; '.join(flags) if flags else '—'} | {resp} |")

lines += ['', f'**Mechanical triage: {len(rows)} records, {problems} flagged for attention.**', '']
out_path.write_text('\n'.join(lines), encoding='utf-8')
print(f'WROTE {out_path} — {len(rows)} records, {problems} flagged')
