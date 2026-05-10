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

describe('writing rulebook coverage matrix', () => {
  for (const profession of ALL_WRITING_PROFESSIONS) {
    it(`${profession} has a valid coverage row for every canonical rule`, () => {
      const issues = validateWritingRuleCoverageMatrix(profession);
      expect(issues).toEqual([]);

      const book = loadRulebook('writing', profession);
      const matrix = buildWritingRuleCoverageMatrix(profession);
      expect(matrix).toHaveLength(172);
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