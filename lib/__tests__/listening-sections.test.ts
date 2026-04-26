import { describe, it, expect } from 'vitest';
import {
  LISTENING_SECTION_SEQUENCE,
  LISTENING_REVIEW_SECONDS,
  LISTENING_SECTION_LABEL,
  LISTENING_SECTION_SHORT_LABEL,
  computeListeningSectionMap,
  groupQuestionsBySection,
  formatReviewSeconds,
  type ListeningSectionCode,
} from '../listening-sections';

type Q = { id: string; partCode: string; number: number };

describe('LISTENING_SECTION_SEQUENCE / labels / review windows', () => {
  it('sequence is A1 → A2 → B → C1 → C2 (forward-only)', () => {
    expect(LISTENING_SECTION_SEQUENCE).toEqual(['A1', 'A2', 'B', 'C1', 'C2']);
  });

  it('review windows match OET spec (B has no review)', () => {
    expect(LISTENING_REVIEW_SECONDS).toEqual({
      A1: 60,
      A2: 60,
      B: 0,
      C1: 30,
      C2: 120,
    });
  });

  it('labels include Part designation', () => {
    expect(LISTENING_SECTION_LABEL.A1).toContain('Part A');
    expect(LISTENING_SECTION_LABEL.B).toContain('Part B');
    expect(LISTENING_SECTION_LABEL.C2).toContain('Part C');
    expect(LISTENING_SECTION_SHORT_LABEL.A1).toBe('A1');
  });
});

describe('computeListeningSectionMap', () => {
  it('passes through canonical codes unchanged', () => {
    const qs: Q[] = [
      { id: 'q1', partCode: 'A1', number: 1 },
      { id: 'q2', partCode: 'A2', number: 2 },
      { id: 'q3', partCode: 'B', number: 3 },
      { id: 'q4', partCode: 'C1', number: 4 },
      { id: 'q5', partCode: 'C2', number: 5 },
    ];
    const map = computeListeningSectionMap(qs);
    expect(map.get('q1')).toBe('A1');
    expect(map.get('q2')).toBe('A2');
    expect(map.get('q3')).toBe('B');
    expect(map.get('q4')).toBe('C1');
    expect(map.get('q5')).toBe('C2');
  });

  it('is case-insensitive and whitespace-tolerant', () => {
    const qs: Q[] = [
      { id: 'q1', partCode: ' a1 ', number: 1 },
      { id: 'q2', partCode: 'c2', number: 2 },
    ];
    const map = computeListeningSectionMap(qs);
    expect(map.get('q1')).toBe('A1');
    expect(map.get('q2')).toBe('C2');
  });

  it('splits legacy "A" half-and-half by number into A1 / A2', () => {
    const qs: Q[] = Array.from({ length: 12 }, (_, i) => ({
      id: `qa-${i}`,
      partCode: 'A',
      number: i + 1,
    }));
    const map = computeListeningSectionMap(qs);
    // First half (numbers 1-6) → A1, second half (7-12) → A2.
    for (let i = 0; i < 6; i++) expect(map.get(`qa-${i}`)).toBe('A1');
    for (let i = 6; i < 12; i++) expect(map.get(`qa-${i}`)).toBe('A2');
  });

  it('splits legacy "C" half-and-half by number into C1 / C2', () => {
    const qs: Q[] = Array.from({ length: 16 }, (_, i) => ({
      id: `qc-${i}`,
      partCode: 'C',
      number: 16 - i, // reversed, to verify it sorts by number not insertion
    }));
    const map = computeListeningSectionMap(qs);
    // After sort by number ascending, first 8 → C1, last 8 → C2.
    const c1Numbers = qs.filter((q) => map.get(q.id) === 'C1').map((q) => q.number);
    const c2Numbers = qs.filter((q) => map.get(q.id) === 'C2').map((q) => q.number);
    expect(c1Numbers.sort((a, b) => a - b)).toEqual([1, 2, 3, 4, 5, 6, 7, 8]);
    expect(c2Numbers.sort((a, b) => a - b)).toEqual([9, 10, 11, 12, 13, 14, 15, 16]);
  });

  it('handles odd-count legacy A by giving A1 the larger half', () => {
    const qs: Q[] = [
      { id: '1', partCode: 'A', number: 1 },
      { id: '2', partCode: 'A', number: 2 },
      { id: '3', partCode: 'A', number: 3 },
    ];
    const map = computeListeningSectionMap(qs);
    // Math.ceil(3/2) = 2 → first 2 → A1, last 1 → A2.
    expect(map.get('1')).toBe('A1');
    expect(map.get('2')).toBe('A1');
    expect(map.get('3')).toBe('A2');
  });

  it('defaults unknown / empty / null partCode to Part B', () => {
    const qs: Q[] = [
      { id: 'q1', partCode: '', number: 1 },
      { id: 'q2', partCode: 'X', number: 2 },
      // @ts-expect-error — exercising tolerance to a null partCode at runtime
      { id: 'q3', partCode: null, number: 3 },
    ];
    const map = computeListeningSectionMap(qs);
    expect(map.get('q1')).toBe('B');
    expect(map.get('q2')).toBe('B');
    expect(map.get('q3')).toBe('B');
  });

  it('returns an empty map for empty input', () => {
    expect(computeListeningSectionMap([]).size).toBe(0);
  });
});

describe('groupQuestionsBySection', () => {
  it('returns all five section keys even when some are empty', () => {
    const groups = groupQuestionsBySection([
      { id: 'q1', partCode: 'A1', number: 1 },
    ]);
    expect(Object.keys(groups).sort()).toEqual(['A1', 'A2', 'B', 'C1', 'C2']);
    expect(groups.A1.map((q) => q.id)).toEqual(['q1']);
    expect(groups.A2).toEqual([]);
    expect(groups.B).toEqual([]);
    expect(groups.C1).toEqual([]);
    expect(groups.C2).toEqual([]);
  });

  it('sorts each section by question number ascending', () => {
    const groups = groupQuestionsBySection([
      { id: 'a-3', partCode: 'A1', number: 3 },
      { id: 'a-1', partCode: 'A1', number: 1 },
      { id: 'a-2', partCode: 'A1', number: 2 },
    ]);
    expect(groups.A1.map((q) => q.id)).toEqual(['a-1', 'a-2', 'a-3']);
  });

  it('routes legacy A items into A1/A2 buckets', () => {
    const groups = groupQuestionsBySection([
      { id: '1', partCode: 'A', number: 1 },
      { id: '2', partCode: 'A', number: 2 },
      { id: '3', partCode: 'A', number: 3 },
      { id: '4', partCode: 'A', number: 4 },
    ]);
    expect(groups.A1.map((q) => q.id)).toEqual(['1', '2']);
    expect(groups.A2.map((q) => q.id)).toEqual(['3', '4']);
  });
});

describe('formatReviewSeconds', () => {
  it.each([
    [0, '00:00'],
    [5, '00:05'],
    [59, '00:59'],
    [60, '01:00'],
    [125, '02:05'],
    [600, '10:00'],
  ])('formats %i → %s', (input, expected) => {
    expect(formatReviewSeconds(input)).toBe(expected);
  });

  it('clamps negative values to 00:00', () => {
    expect(formatReviewSeconds(-30)).toBe('00:00');
  });

  it('floors fractional seconds', () => {
    expect(formatReviewSeconds(59.9)).toBe('00:59');
    expect(formatReviewSeconds(60.4)).toBe('01:00');
  });
});

describe('type-level: ListeningSectionCode is exhaustive against constants', () => {
  it('every code in sequence appears in review-seconds and labels', () => {
    for (const code of LISTENING_SECTION_SEQUENCE) {
      const c: ListeningSectionCode = code;
      expect(LISTENING_REVIEW_SECONDS[c]).toBeTypeOf('number');
      expect(LISTENING_SECTION_LABEL[c]).toBeTypeOf('string');
      expect(LISTENING_SECTION_SHORT_LABEL[c]).toBeTypeOf('string');
    }
  });
});
