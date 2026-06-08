import { describe, expect, it } from 'vitest';
import {
  lintReadingPaper,
  SUPPORTED_READING_CHECK_IDS,
  type AuthoredReadingPaper,
} from '../reading-rules';

function validPaper(): AuthoredReadingPaper {
  const items: AuthoredReadingPaper['items'] = [];
  for (let i = 0; i < 20; i++) items.push({ part: 'A' });
  for (let i = 0; i < 6; i++) items.push({ part: 'B', type: 'mcq', optionCount: 3 });
  for (let i = 0; i < 16; i++) items.push({ part: 'C', type: 'mcq', optionCount: 4 });
  return { partATextCount: 4, items };
}

describe('reading authoring detectors (non-blocking warnings)', () => {
  it('passes a canonical 20/6/16 paper with 4 Part A texts and 3/4-option MCQs', () => {
    expect(lintReadingPaper(validPaper())).toEqual([]);
  });

  it('flags a 4-option Part B item (R03.1 — Part B is 3-option)', () => {
    const p = validPaper();
    p.items.find((i) => i.part === 'B')!.optionCount = 4;
    expect(lintReadingPaper(p).some((f) => f.ruleId === 'R03.1')).toBe(true);
  });

  it('flags a 3-option Part C item (R04.1 — Part C is 4-option)', () => {
    const p = validPaper();
    p.items.find((i) => i.part === 'C')!.optionCount = 3;
    expect(lintReadingPaper(p).some((f) => f.ruleId === 'R04.1')).toBe(true);
  });

  it('flags Part A that does not have exactly 4 texts (R02.1)', () => {
    const p = validPaper();
    p.partATextCount = 3;
    expect(lintReadingPaper(p).some((f) => f.ruleId === 'R02.1')).toBe(true);
  });

  it('flags a non-canonical item count (R01.1)', () => {
    const p = validPaper();
    p.items.pop();
    expect(lintReadingPaper(p).some((f) => f.ruleId === 'R01.1')).toBe(true);
  });

  it('exposes the supported reading check-ids', () => {
    expect(SUPPORTED_READING_CHECK_IDS).toContain('reading_part_c_four_options');
    expect(SUPPORTED_READING_CHECK_IDS).toContain('reading_shape_42');
  });
});
