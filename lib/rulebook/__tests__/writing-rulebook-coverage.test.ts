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
  medicine: 268,
  nursing: 275,
  dentistry: 275,
  pharmacy: 278,
  physiotherapy: 278,
  radiography: 275,
};
const LEGACY_RULE_COUNT = 172 + 38;

describe('writing rulebook coverage matrix', () => {
  for (const profession of ALL_WRITING_PROFESSIONS) {
    it(`${profession} has a valid coverage row for every canonical rule`, () => {
      const issues = validateWritingRuleCoverageMatrix(profession);
      expect(issues).toEqual([]);

      const book = loadRulebook('writing', profession);
      const matrix = buildWritingRuleCoverageMatrix(profession);
      expect(matrix).toHaveLength(CANONICAL_PROFESSION_COUNTS[profession] ?? LEGACY_RULE_COUNT);
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