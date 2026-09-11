/**
 * OET Listening section model.
 *
 * The real OET Listening exam has five forward-only sections:
 *   A1 → A2 → B → C1 → C2
 *
 * Audio integrity (owner directive 2026-06-27): in EVERY mode the audio is
 * non-pausable — once a section's audio starts it plays start-to-end with no
 * pause / scrub / replay, then the player auto-advances straight to the next
 * section. There is therefore no post-audio review window (all values 0); the
 * only per-section time grant is the pre-audio reading window below.
 *
 * Forward-only lock rule:
 *   Once a section's audio ends the player advances and that section is
 *   permanently locked. There is no way to return.
 *
 * Part B audio authoring rule:
 *   Each of the six Part B extracts is ~40 seconds (NOT 1 minute).
 */

export type ListeningSectionCode = 'A1' | 'A2' | 'B' | 'C1' | 'C2';

export const LISTENING_SECTION_SEQUENCE: ListeningSectionCode[] = ['A1', 'A2', 'B', 'C1', 'C2'];

// Post-audio review windows are disabled in every mode — audio plays once and
// the player auto-advances on `ended`. Kept as a map (all zero) so existing
// consumers (`currentSectionReviewSeconds`, strict-resume hydration) keep
// working without branching.
export const LISTENING_REVIEW_SECONDS: Record<ListeningSectionCode, number> = {
  A1: 0,
  A2: 0,
  B: 0,
  C1: 0,
  C2: 0,
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
 * (`A`, `C`, `B`). Legacy `A` and `C` items use their authoritative question
 * number when available, with an even split only as a compatibility fallback.
 */
export function computeListeningSectionMap<T extends { id: string; partCode: string; number: number }>(
  questions: readonly T[],
): Map<string, ListeningSectionCode> {
  const map = new Map<string, ListeningSectionCode>();
  const legacyA: T[] = [];
  const legacyC: T[] = [];

  const sectionForQuestionNumber = (number: number): ListeningSectionCode | undefined => {
    if (number >= 1 && number <= 12) return 'A1';
    if (number >= 13 && number <= 24) return 'A2';
    if (number >= 25 && number <= 30) return 'B';
    if (number >= 31 && number <= 36) return 'C1';
    if (number >= 37 && number <= 42) return 'C2';
    return undefined;
  };

  for (const q of questions) {
    const raw = (q.partCode ?? '').toString().toUpperCase().trim();
    if (raw === 'A1' || raw === 'A2' || raw === 'B' || raw === 'C1' || raw === 'C2') {
      map.set(q.id, raw);
      continue;
    }
    if (raw.startsWith('A')) {
      const numbered = sectionForQuestionNumber(q.number);
      if (numbered === 'A1' || numbered === 'A2') map.set(q.id, numbered);
      else legacyA.push(q);
      continue;
    }
    if (raw.startsWith('C')) {
      const numbered = sectionForQuestionNumber(q.number);
      if (numbered === 'C1' || numbered === 'C2') map.set(q.id, numbered);
      else legacyC.push(q);
      continue;
    }
    // Missing/unknown legacy codes can still be recovered from the official
    // question-number ranges. Keep the historical Part B fallback only when
    // the number itself cannot identify a Listening section.
    map.set(q.id, sectionForQuestionNumber(q.number) ?? 'B');
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

/**
 * Part C is two sequential audio extracts (C1 = Q31–36, C2 = Q37–42) presented
 * to the candidate as ONE Q31–Q42 workspace, so the visible card list spans more
 * than the extract whose audio is currently playing.
 *
 * Returns the index within `workspace` of the LAST question owned by the active
 * sub-section — the card the audio boundary sits on. That card is the furthest
 * one reachable with a plain "Next Question" and the one that must offer the
 * sub-section transition instead; any card past it belongs to a later extract and
 * cannot be shown without switching the audio first.
 *
 * Falls back to the final workspace index when every card belongs to the active
 * section, which keeps non-Part-C sections behaving exactly as before.
 */
export function workspaceBoundaryIndex(
  workspace: readonly { id: string }[],
  ownedIds: ReadonlySet<string>,
): number {
  for (let index = workspace.length - 1; index >= 0; index -= 1) {
    if (ownedIds.has(workspace[index].id)) return index;
  }
  return workspace.length - 1;
}

export function formatReviewSeconds(remaining: number): string {
  const safe = Math.max(0, Math.floor(remaining));
  const m = Math.floor(safe / 60);
  const s = safe % 60;
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
}
