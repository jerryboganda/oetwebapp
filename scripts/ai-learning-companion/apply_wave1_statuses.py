#!/usr/bin/env python3
"""Wave 0+1 (2026-10-07) status updates to the traceability register. Idempotent."""
from pathlib import Path
import csv, json, subprocess, sys

ROOT = Path(__file__).resolve().parents[2]
TRACE = ROOT / 'docs' / 'ai-learning-companion' / 'traceability'

W1 = 'SAMI Wave 1 2026-10-07: '
EDITS = {
    # id: (new_status or None, note)
    'F-009': ('EXISTS', W1 + 'CompanionAvailability (weekday minutes, night/long shifts, travel) + composer context + plan shaper.'),
    'F-034': ('EXISTS', W1 + 'templates sami-emergency-7d (1-2wk), sami-final-3d (1wk), sami-intensive-14d (2wk) seeded idempotently; NBA drives near-exam behaviour.'),
    'F-035': ('EXISTS', W1 + 'sami-exam-eve template (final 24h, no new content) + NBA exam<=3d rehearsal branch.'),
    'F-036': ('EXISTS', W1 + 'StudyPlanAvailabilityShaper moves overdue/not-started items to the next day with capacity, capped; never stacks a backlog.'),
    'F-037': ('EXISTS', W1 + 'night-shift days = 0 minutes, long-day shifts halve capacity; shaper + composer carry the shift model.'),
    'F-038': ('EXISTS', W1 + 'travel mode drops new-content slots, keeps spaced reviews at travel minutes/day.'),
    'F-039': ('EXISTS', W1 + 'companion_next_best_action tool; deterministic evidence-first engine.'),
    'F-040': ('EXISTS', W1 + 'NextBestActionService: exam proximity > due Error DNA reviews > plan item > weakness drill > honest diagnostic.'),
    'F-043': ('EXISTS', W1 + 'CompanionJourneys + journey memory layer; start_journey preserves history.'),
    'F-012': ('EXISTS', W1 + 'multiple journeys supported; previous journeys remain readable for comparison.'),
    'F-044': ('PARTIAL', W1 + 'ErrorDnaService (upsert-by-pattern, mastery, review ladder) + train_mistakes tool live; automated evidence feeders from graded attempts land with the tutoring waves.'),
    'F-046': ('PARTIAL', W1 + 'expanding-interval review ladder + due-review scans wired into NBA; practice-loop review outcomes pending.'),
    'F-047': ('EXISTS', W1 + 'scoped entry delete (DELETE /v1/companion/memory/entries/{id}), full export, notes/bookmarks unchanged.'),
    'F-071': ('EXISTS', W1 + 'companion_why_score_change explains only from recorded trend + error evidence; refuses to guess.'),
    'F-008': ('PARTIAL', W1 + 'confirm-gated score recording path live (record->confirm later turn); image extraction lands with Wave 2 multimodal.'),
    'F-090': ('PARTIAL', W1 + 'same confirm-before-save path; document/image understanding in Wave 2.'),
}

with (TRACE / 'features.json').open(encoding='utf-8') as f:
    data = json.load(f)
for feat in data['features']:
    if feat['id'] in EDITS:
        status, note = EDITS[feat['id']]
        if status and feat.get('implementation_status') != status:
            feat['implementation_status'] = status
        if note not in (feat.get('notes') or ''):
            feat['notes'] = ((feat.get('notes') or '') + ' | ' + note).strip(' |')
(TRACE / 'features.json').write_text(json.dumps(data, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')

with (TRACE / 'features.csv').open(encoding='utf-8-sig', newline='') as f:
    reader = csv.DictReader(f)
    fieldnames = reader.fieldnames
    rows = list(reader)
for row in rows:
    if row.get('id') in EDITS:
        status, note = EDITS[row['id']]
        if status and row['implementation_status'] != status:
            row['implementation_status'] = status
        if note not in (row.get('notes') or ''):
            row['notes'] = ((row.get('notes') or '') + ' | ' + note).strip(' |')
with (TRACE / 'features.csv').open('w', encoding='utf-8', newline='') as f:
    writer = csv.DictWriter(f, fieldnames=fieldnames)
    writer.writeheader()
    writer.writerows(rows)

result = subprocess.run([sys.executable, str(ROOT / 'scripts' / 'ai-learning-companion' / 'validate_traceability.py')],
                        capture_output=True, text=True)
print(result.stdout, result.stderr)
sys.exit(result.returncode)
