/**
 * OET Listening section model.
 *
 * The real OET Listening exam has five forward-only sections:
 *   A1 → A2 → B → C1 → C2
 *
 * Review windows (with editable answer boxes) run AFTER the audio of each
 * section, except Part B:
 *   - A1: 60s review (A1 only)
 *   - A2: 60s review (A2 only)
 *   - B:  no review window (auto-advance once last B question confirmed)
 *   - C1: 30s review (C1 only)
 *   - C2: 120s review (C2 only) — final window of the exam
 *
 * Forward-only lock rule:
 *   Once a section's review window ends OR the learner confirms Next,
 *   that section is permanently locked. There is no way to return.
 *
 * Part B audio authoring rule:
 *   Each of the six Part B extracts is ~40 seconds (NOT 1 minute).
 */

export type ListeningSectionCode = 'A1' | 'A2' | 'B' | 'C1' | 'C2';

export const LISTENING_SECTION_SEQUENCE: ListeningSectionCode[] = ['A1', 'A2', 'B', 'C1', 'C2'];

export const LISTENING_REVIEW_SECONDS: Record<ListeningSectionCode, number> = {
  A1: 60,
  A2: 60,
  B: 0,
  C1: 30,
  C2: 120,
};

/**
 * CBLA pre-audio reading window (in seconds) granted before the audio of each
 * section starts playing. Candidates may mark answers in advance during this
 * window. Per CBLA timing:
 *   - A1 / A2: 30s each (Part A consultations)
 *   - B:       30s for the whole Part B section (six 15s extract reads
 *               compressed into one global pre-roll, since the player
 *               currently treats Part B as a single section)
 *   - C1 / C2: 90s each (Part C presentations)
 */
export const LISTENING_PREVIEW_SECONDS: Record<ListeningSectionCode, number> = {
  A1: 30,
  A2: 30,
  B: 30,
  C1: 90,
  C2: 90,
};

export const LISTENING_PREVIEW_LABEL = 'Reading time';

export const LISTENING_SECTION_LABEL: Record<ListeningSectionCode, string> = {
  A1: 'Part A — Extract 1',
  A2: 'Part A — Extract 2',
  B: 'Part B — Workplace extracts',
  C1: 'Part C — Extract 1',
  C2: 'Part C — Extract 2',
};

export const LISTENING_SECTION_SHORT_LABEL: Record<ListeningSectionCode, string> = {
  A1: 'A1',
  A2: 'A2',
  B: 'B',
  C1: 'C1',
  C2: 'C2',
};

/**
 * Normalise an authored `partCode` to one of the 5 canonical section codes.
 * Accepts both granular codes (`A1`, `A2`, `C1`, `C2`) and legacy wide codes
 * (`A`, `C`, `B`). Legacy `A` and `C` items are split half-and-half by number.
 */
export function computeListeningSectionMap<T extends { id: string; partCode: string; number: number }>(
  questions: readonly T[],
): Map<string, ListeningSectionCode> {
  const map = new Map<string, ListeningSectionCode>();
  const legacyA: T[] = [];
  const legacyC: T[] = [];

  for (const q of questions) {
    const raw = (q.partCode ?? '').toString().toUpperCase().trim();
    if (raw === 'A1' || raw === 'A2' || raw === 'B' || raw === 'C1' || raw === 'C2') {
      map.set(q.id, raw);
      continue;
    }
    if (raw.startsWith('A')) {
      legacyA.push(q);
      continue;
    }
    if (raw.startsWith('C')) {
      legacyC.push(q);
      continue;
    }
    // Default: treat as Part B.
    map.set(q.id, 'B');
  }

  const splitAndAssign = (items: T[], first: ListeningSectionCode, second: ListeningSectionCode) => {
    const sorted = [...items].sort((a, b) => a.number - b.number);
    const mid = Math.ceil(sorted.length / 2);
    sorted.forEach((q, idx) => map.set(q.id, idx < mid ? first : second));
  };

  splitAndAssign(legacyA, 'A1', 'A2');
  splitAndAssign(legacyC, 'C1', 'C2');

  return map;
}

export function groupQuestionsBySection<T extends { id: string; partCode: string; number: number }>(
  questions: readonly T[],
): Record<ListeningSectionCode, T[]> {
  const sectionMap = computeListeningSectionMap(questions);
  const groups: Record<ListeningSectionCode, T[]> = {
    A1: [],
    A2: [],
    B: [],
    C1: [],
    C2: [],
  };
  for (const q of questions) {
    const code = sectionMap.get(q.id) ?? 'B';
    groups[code].push(q);
  }
  for (const code of LISTENING_SECTION_SEQUENCE) {
    groups[code].sort((a, b) => a.number - b.number);
  }
  return groups;
}

export function formatReviewSeconds(remaining: number): string {
  const safe = Math.max(0, Math.floor(remaining));
  const m = Math.floor(safe / 60);
  const s = safe % 60;
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
}
