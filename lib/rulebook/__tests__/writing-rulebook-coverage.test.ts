import { describe, expect, it } from 'vitest';
import {
  buildWritingRuleCoverageMatrix,
  validateWritingRuleCoverageMatrix,
} from '../writing-coverage';
import { loadRulebook } from '../loader';
import type { ExamProfession } from '../types';

const ALL_WRITING_PROFESSIONS: ExamProfession[] = [
  'medicine',
  'nursing',
  'dentistry',
  'physiotherapy',
  'pharmacy',
  'dietetics',
  'occupational-therapy',
  'optometry',
  'podiatry',
  'radiography',
  'speech-pathology',
  'veterinary',
  'other-allied-health',
];

/**
 * Migrated to the canonical registry (docs/canonical-rules/README.md); rest stay
 * on legacy 172. Every book also carries the 38 owner Rev8 rules (OWN-W-001..038).
 */
const CANONICAL_PROFESSION_COUNTS: Partial<Record<ExamProfession, number>> = {
  // + the 5 OA3 rows (OA3-01..OA3-05, 15 Sep 2026); keep in lockstep with
  // writing-rulebook-baseline.test.ts and the registry rule counts.
  // + the 3 OA4 rows and the 38 Senior Assessor Release Audit rows
  // (OA5-01..OA5-38, 16 Sep 2026), also globally scoped.
  // + OA6-01..OA6-02 (cross-model audit, 17 Sep 2026), also globally scoped.
  medicine: 351,
  nursing: 358,
  dentistry: 358,
  pharmacy: 361,
  physiotherapy: 361,
  radiography: 358,
};
// The legacy packs each carry 172 base + 38 owner Rev8 rules, PLUS the
// derived operational modules the ULTIMATE FINAL round added (PRD-*), which is
// why they no longer share one number. Pinned per profession so drift is
// visible instead of collapsing into a single stale constant.
const LEGACY_RULE_COUNT = 172 + 38;
const LEGACY_PROFESSION_COUNTS: Partial<Record<ExamProfession, number>> = {
  dietetics: 219,
  'occupational-therapy': 218,
  optometry: 218,
  podiatry: 218,
  'speech-pathology': 218,
  veterinary: 211,
};

describe('writing rulebook coverage matrix', () => {
  for (const profession of ALL_WRITING_PROFESSIONS) {
    it(`${profession} has a valid coverage row for every canonical rule`, () => {
      const issues = validateWritingRuleCoverageMatrix(profession);
      expect(issues).toEqual([]);

      const book = loadRulebook('writing', profession);
      const matrix = buildWritingRuleCoverageMatrix(profession);
      expect(matrix).toHaveLength(CANONICAL_PROFESSION_COUNTS[profession] ?? LEGACY_PROFESSION_COUNTS[profession] ?? LEGACY_RULE_COUNT);
      expect(matrix.map((row) => row.ruleId)).toEqual(book.rules.map((rule) => rule.id));
    });
  }

  it('does not leave any critical writing rule display-only or unimplemented', () => {
    const matrix = buildWritingRuleCoverageMatrix('medicine');
    const uncoveredCritical = matrix.filter(
      (row) => row.severity === 'critical'
        && (row.coverageMode === 'display-only' || row.coverageMode === 'not-implemented')
        && !row.waiverReason,
    );

    expect(uncoveredCritical).toEqual([]);
  });
});