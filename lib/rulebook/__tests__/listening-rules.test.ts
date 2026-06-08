import { describe, expect, it } from 'vitest';
import {
  lintListeningPaper,
  SUPPORTED_LISTENING_CHECK_IDS,
  type AuthoredListeningPaper,
} from '../listening-rules';

function validPaper(): AuthoredListeningPaper {
  const items: AuthoredListeningPaper['items'] = [];
  const mk = (part: AuthoredListeningPaper['items'][number]['part'], mcq: boolean) => ({
    part,
    type: mcq ? 'mcq' : 'short_answer',
    optionCount: mcq ? 3 : undefined,
    transcriptExcerpt: 'evidence',
    speakerAttitude: part === 'C1' || part === 'C2' ? 'concerned' : null,
    distractors: mcq ? [{ category: 'too_strong' }, { category: 'opposite_meaning' }] : [],
  });
  for (let i = 0; i < 12; i++) items.push(mk('A1', false));
  for (let i = 0; i < 12; i++) items.push(mk('A2', false));
  for (let i = 0; i < 6; i++) items.push(mk('B', true));
  for (let i = 0; i < 6; i++) items.push(mk('C1', true));
  for (let i = 0; i < 6; i++) items.push(mk('C2', true));
  return { items };
}

describe('listening authoring detectors (non-blocking warnings)', () => {
  it('passes a canonical 24/6/12 paper', () => {
    expect(lintListeningPaper(validPaper())).toEqual([]);
  });

  it('flags a 4-option Part B item (L03.1 — Part B is 3-option)', () => {
    const p = validPaper();
    p.items.find((i) => i.part === 'B')!.optionCount = 4;
    expect(lintListeningPaper(p).some((f) => f.ruleId === 'L03.1')).toBe(true);
  });

  it('flags an MCQ in Part A (L02.1 — Part A is short-answer only)', () => {
    const p = validPaper();
    const a = p.items.find((i) => i.part === 'A1')!;
    a.type = 'mcq';
    a.optionCount = 3;
    expect(lintListeningPaper(p).some((f) => f.ruleId === 'L02.1')).toBe(true);
  });

  it('flags an invalid distractor category (L05.1)', () => {
    const p = validPaper();
    p.items.find((i) => i.part === 'B')!.distractors = [{ category: 'made_up' }];
    expect(lintListeningPaper(p).some((f) => f.ruleId === 'L05.1')).toBe(true);
  });

  it('flags a missing transcript excerpt (L06.1)', () => {
    const p = validPaper();
    p.items[0].transcriptExcerpt = null;
    expect(lintListeningPaper(p).some((f) => f.ruleId === 'L06.1')).toBe(true);
  });

  it('flags speaker attitude on a Part A item (L07.1 — Part C only)', () => {
    const p = validPaper();
    p.items[0].speakerAttitude = 'concerned';
    expect(lintListeningPaper(p).some((f) => f.ruleId === 'L07.1')).toBe(true);
  });

  it('flags an out-of-enum speaker attitude (L07.2)', () => {
    const p = validPaper();
    p.items.find((i) => i.part === 'C1')!.speakerAttitude = 'angry';
    expect(lintListeningPaper(p).some((f) => f.ruleId === 'L07.2')).toBe(true);
  });

  it('flags a non-canonical part split (L01.2)', () => {
    const p = validPaper();
    p.items = p.items.filter((i) => i.part !== 'C2'); // drop 6 → C=6
    expect(lintListeningPaper(p).some((f) => f.ruleId === 'L01.2')).toBe(true);
  });

  it('exposes the supported listening check-ids', () => {
    expect(SUPPORTED_LISTENING_CHECK_IDS).toContain('listening_part_b_three_options');
    expect(SUPPORTED_LISTENING_CHECK_IDS).toContain('listening_shape_42');
  });
});
