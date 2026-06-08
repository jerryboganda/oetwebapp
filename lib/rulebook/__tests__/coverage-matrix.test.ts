import { describe, expect, it } from 'vitest';
import {
  buildRuleCoverageMatrix,
  validateRuleCoverageMatrix,
  coverageModeFor,
} from '../coverage-matrix';
import { WRITING_CHECK_IDS, SPEAKING_CHECK_IDS } from '../check-ids';
import { loadRulebook } from '../loader';
import type { Rule } from '../types';

function rule(partial: Partial<Rule>): Rule {
  return {
    id: partial.id ?? 'R00.0',
    section: partial.section ?? '00',
    title: partial.title ?? 'test',
    body: partial.body ?? 'test body',
    severity: partial.severity ?? 'major',
    ...partial,
  };
}

describe('generic rule coverage matrix', () => {
  it('builds one row per rule, in canonical order, for writing/medicine', () => {
    const book = loadRulebook('writing', 'medicine');
    const matrix = buildRuleCoverageMatrix('writing', 'medicine');
    expect(matrix).toHaveLength(book.rules.length);
    expect(matrix.map((r) => r.ruleId)).toEqual(book.rules.map((r) => r.id));
  });

  it('reports no issues for the canonical writing/medicine rulebook', () => {
    expect(validateRuleCoverageMatrix('writing', 'medicine')).toEqual([]);
  });

  it('builds a structurally valid matrix for a different kind (speaking/medicine)', () => {
    const book = loadRulebook('speaking', 'medicine');
    const matrix = buildRuleCoverageMatrix('speaking', 'medicine');
    expect(matrix.map((r) => r.ruleId)).toEqual(book.rules.map((r) => r.id));
    // every speaking rule whose checkId is backed by a detector is deterministic
    for (const row of matrix) {
      if (row.checkId && SPEAKING_CHECK_IDS.has(row.checkId)) {
        expect(row.coverageMode).toBe('deterministic-detector');
      }
    }
  });

  it('surfaces the known dead speaking CBT checkIds (and nothing else) — Phase-2/D7 cleanup', () => {
    // RULE_59/60/62 reference `speaking_cbt_*` checkIds that have NO backing
    // detector; the validator must catch them. No OTHER validity issue should
    // exist in the speaking matrix today. Phase 2 (deviation D7) reclassifies
    // these to ai-grounded/human-review-only, at which point this list empties.
    const issues = validateRuleCoverageMatrix('speaking', 'medicine');
    expect(issues.every((i) => /unsupported checkId speaking_cbt_/.test(i))).toBe(true);
  });

  it('classifies a rule with a supported checkId as deterministic', () => {
    const known = [...WRITING_CHECK_IDS][0];
    expect(coverageModeFor(rule({ checkId: known }), 'writing')).toBe('deterministic-detector');
  });

  it('does NOT classify a rule with an unsupported checkId as deterministic', () => {
    expect(coverageModeFor(rule({ checkId: '__no_detector__' }), 'writing')).not.toBe(
      'deterministic-detector',
    );
  });

  it('classifies forbiddenPatterns, human-review, and untagged rules', () => {
    expect(coverageModeFor(rule({ forbiddenPatterns: ['x'] }), 'writing')).toBe(
      'forbidden-pattern-detector',
    );
    expect(coverageModeFor(rule({ enforcement: 'human-review-only' }), 'speaking')).toBe(
      'human-review-only',
    );
    expect(coverageModeFor(rule({ enforcement: 'ai-grounded' }), 'writing')).toBe(
      'structured-ai-adjudication',
    );
    expect(coverageModeFor(rule({}), 'writing')).toBe('structured-ai-adjudication');
  });
});
