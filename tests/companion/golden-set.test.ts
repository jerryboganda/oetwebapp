import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * AI Learning Companion — evaluation harness (Stage 0, S0.5).
 *
 * This file is the *structural* half of the harness and always runs in CI: it
 * proves the golden set is well-formed and that every behaviour the source
 * specification treats as a release gate is actually covered by a case.
 *
 * The *behavioural* half — running each prompt against a live companion and
 * scoring the response — needs a provider and a seeded corpus, so it is opt-in
 * (see `docs/ai-learning-companion/QA_EVALUATION_AND_RELEASE_GATES.md`). It is
 * deliberately NOT asserted here, because pass thresholds are TO VERIFY
 * (TV-004 retrieval, TV-005 hallucination) and this program does not invent
 * numbers to make a build look green.
 */

const GOLDEN_DIR = join(process.cwd(), 'tests', 'companion', 'golden');

/**
 * Case classes the source requires a golden set to cover. Dropping one of these
 * silently is exactly the failure mode the traceability programme exists to
 * prevent, so the list is asserted rather than documented.
 */
const REQUIRED_CASE_CLASSES = [
  'grounded_answer',
  'insufficient_evidence',
  'authority_conflict',
  'profession_precedence',
  'locked_content_extraction',
  'multi_turn_reconstruction',
  'unentitled_retrieval',
  'navigation_action',
  'clinical_boundary',
  'exam_integrity',
  'learner_distress',
  'score_claim_gate',
  'prompt_injection',
  'entitlement_fabrication',
  'credit_transparency',
  'arabic_code_switch',
] as const;

/**
 * Behaviours with zero tolerance per the source QA gates. A single failure is a
 * release blocker, not a quality percentage — so each must be represented.
 */
const ZERO_TOLERANCE_CLASSES = [
  'unentitled_retrieval',
  'exam_integrity',
  'prompt_injection',
  'entitlement_fabrication',
] as const;

interface GoldenCase {
  id: string;
  class: string;
  prompt?: string;
  turns?: string[];
  expect: Record<string, unknown> & { notes?: string; zeroTolerance?: boolean };
}

interface GoldenSet {
  setId: string;
  description: string;
  cases: GoldenCase[];
}

function loadGoldenSets(): Array<{ file: string; set: GoldenSet }> {
  return readdirSync(GOLDEN_DIR)
    .filter((f) => f.endsWith('.golden.json'))
    .map((file) => ({
      file,
      set: JSON.parse(readFileSync(join(GOLDEN_DIR, file), 'utf8')) as GoldenSet,
    }));
}

describe('companion golden sets', () => {
  const sets = loadGoldenSets();

  it('finds at least one golden set', () => {
    expect(sets.length).toBeGreaterThan(0);
  });

  it.each(sets)('$file is structurally valid', ({ set }) => {
    expect(set.setId).toBeTruthy();
    expect(set.description).toBeTruthy();
    expect(Array.isArray(set.cases)).toBe(true);
    expect(set.cases.length).toBeGreaterThan(0);

    for (const c of set.cases) {
      expect(c.id, `case is missing an id in ${set.setId}`).toMatch(/^GC-\d{3}$/);
      expect(c.class, `${c.id} is missing a class`).toBeTruthy();
      expect(c.expect, `${c.id} is missing expectations`).toBeTruthy();

      // Every case must be runnable: either a single prompt or a turn sequence.
      const runnable = typeof c.prompt === 'string' || Array.isArray(c.turns);
      expect(runnable, `${c.id} has neither prompt nor turns`).toBe(true);

      // Expectations must be explained. An unexplained expectation cannot be
      // reviewed by a human, which defeats the purpose of a golden case.
      expect(typeof c.expect.notes, `${c.id} expectation has no notes`).toBe('string');
    }
  });

  it.each(sets)('$file has unique case ids', ({ set }) => {
    const ids = set.cases.map((c) => c.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('covers every required case class', () => {
    const covered = new Set(sets.flatMap(({ set }) => set.cases.map((c) => c.class)));
    const missing = REQUIRED_CASE_CLASSES.filter((cls) => !covered.has(cls));

    expect(
      missing,
      `Golden sets are missing required case classes: ${missing.join(', ')}`,
    ).toEqual([]);
  });

  it('marks every zero-tolerance behaviour as zero tolerance', () => {
    const allCases = sets.flatMap(({ set }) => set.cases);

    for (const cls of ZERO_TOLERANCE_CLASSES) {
      const cases = allCases.filter((c) => c.class === cls);
      expect(cases.length, `no golden case covers zero-tolerance class '${cls}'`).toBeGreaterThan(0);
      expect(
        cases.some((c) => c.expect.zeroTolerance === true),
        `class '${cls}' must have at least one case flagged zeroTolerance`,
      ).toBe(true);
    }
  });

  it('does not hard-code a pass threshold', () => {
    // TV-004 / TV-005 are unresolved. A numeric threshold appearing here would
    // mean someone invented one to make the gate look satisfiable.
    const raw = readdirSync(GOLDEN_DIR)
      .filter((f) => f.endsWith('.golden.json'))
      .map((f) => readFileSync(join(GOLDEN_DIR, f), 'utf8'))
      .join('\n');

    expect(raw).not.toMatch(/"(passThreshold|minAccuracy|minRecall|minPrecision)"/);
  });
});
