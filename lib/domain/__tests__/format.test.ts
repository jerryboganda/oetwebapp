import { describe, it, expect } from 'vitest';
import {
  titleCase,
  minutesToLabel,
  scoreRangeDisplay,
  formatCurrency,
  normalizeWaveformPeaks,
  parseCriterionScore,
  scoreToGrade,
} from '../../domain/format';

describe('titleCase', () => {
  it('returns empty string for null/undefined/empty', () => {
    expect(titleCase(null)).toBe('');
    expect(titleCase(undefined)).toBe('');
    expect(titleCase('')).toBe('');
  });

  it('title-cases snake_case', () => {
    expect(titleCase('hello_world_foo')).toBe('Hello World Foo');
  });

  it('title-cases kebab-case', () => {
    expect(titleCase('hello-world')).toBe('Hello World');
  });

  it('handles mixed separators and extra spaces', () => {
    expect(titleCase('hello_world-foo bar')).toBe('Hello World Foo Bar');
    expect(titleCase('  multiple   spaces  ')).toBe('Multiple Spaces');
  });

  it('preserves casing of the rest of the word', () => {
    expect(titleCase('iOS_app')).toBe('IOS App');
  });
});

describe('minutesToLabel', () => {
  it('renders < 180 minutes in mins', () => {
    expect(minutesToLabel(0)).toBe('0 mins');
    expect(minutesToLabel(45)).toBe('45 mins');
    expect(minutesToLabel(179)).toBe('179 mins');
  });

  it('renders >= 180 minutes in hours, rounded', () => {
    expect(minutesToLabel(180)).toBe('3 hrs');
    expect(minutesToLabel(210)).toBe('4 hrs'); // 3.5 rounds up to 4
    expect(minutesToLabel(240)).toBe('4 hrs');
  });
});

describe('scoreRangeDisplay', () => {
  it('rewrites - as " to "', () => {
    expect(scoreRangeDisplay('3-4')).toBe('3 to 4');
    expect(scoreRangeDisplay('3 - 4')).toBe('3 to 4');
    expect(scoreRangeDisplay('3  -  4')).toBe('3 to 4');
  });

  it('returns empty string for null/undefined', () => {
    expect(scoreRangeDisplay(null)).toBe('');
    expect(scoreRangeDisplay(undefined)).toBe('');
  });

  it('leaves non-range strings unchanged', () => {
    expect(scoreRangeDisplay('5')).toBe('5');
  });
});

describe('formatCurrency', () => {
  it('formats AUD by default', () => {
    const out = formatCurrency(12.5);
    // Intl in en-AU returns "$12.50"
    expect(out).toMatch(/\$12\.50/);
  });

  it('respects an explicit currency code', () => {
    const out = formatCurrency(99, 'USD');
    expect(out).toMatch(/99\.00/);
    expect(out).toMatch(/US\$|USD/);
  });

  it('renders 0.00 for null/undefined/non-finite', () => {
    expect(formatCurrency(null)).toMatch(/0\.00/);
    expect(formatCurrency(undefined)).toMatch(/0\.00/);
    expect(formatCurrency(Number.NaN)).toMatch(/0\.00/);
    expect(formatCurrency(Number.POSITIVE_INFINITY)).toMatch(/0\.00/);
  });

  it('coerces numeric strings', () => {
    expect(formatCurrency('15.5')).toMatch(/15\.50/);
  });
});

describe('normalizeWaveformPeaks', () => {
  it('returns [] for non-array input', () => {
    expect(normalizeWaveformPeaks(null)).toEqual([]);
    expect(normalizeWaveformPeaks(undefined)).toEqual([]);
    expect(normalizeWaveformPeaks('foo')).toEqual([]);
    expect(normalizeWaveformPeaks({ length: 3 })).toEqual([]);
  });

  it('clamps values to the 6..100 band', () => {
    expect(normalizeWaveformPeaks([0, 5, 6, 50, 100, 200])).toEqual([6, 6, 6, 50, 100, 100]);
  });

  it('rounds fractional values', () => {
    expect(normalizeWaveformPeaks([10.4, 10.6])).toEqual([10, 11]);
  });

  it('drops non-finite entries (NaN / non-numeric strings)', () => {
    // Note: Number(null) === 0 and Number('') === 0, so those coerce and clamp
    // up to the 6..100 floor. Only entries that coerce to NaN are dropped.
    expect(normalizeWaveformPeaks([10, Number.NaN, 'foo', undefined, 20])).toEqual([10, 20]);
  });

  it('coerces falsy primitives via Number() and clamps to the floor', () => {
    // null -> 0 -> clamped to 6; '' -> 0 -> clamped to 6.
    expect(normalizeWaveformPeaks([null, ''])).toEqual([6, 6]);
  });
});

describe('parseCriterionScore', () => {
  it('returns 0 for null/undefined/empty', () => {
    expect(parseCriterionScore(null)).toBe(0);
    expect(parseCriterionScore(undefined)).toBe(0);
    expect(parseCriterionScore('')).toBe(0);
  });

  it('parses a single-number string', () => {
    expect(parseCriterionScore('3')).toBe(3);
  });

  it('parses a tight range and returns the rounded midpoint', () => {
    expect(parseCriterionScore('3-4')).toBe(4); // (3+4)/2 = 3.5 → 4
    expect(parseCriterionScore('2-4')).toBe(3);
    expect(parseCriterionScore('4-6')).toBe(5);
  });

  it('falls back to the leading number when the hyphen is space-separated', () => {
    // The regex requires the hyphen to immediately follow the first digit,
    // so '3 - 5' parses as just 3.
    expect(parseCriterionScore('3 - 5')).toBe(3);
  });

  it('returns 0 for unparseable input', () => {
    expect(parseCriterionScore('abc')).toBe(0);
  });
});

describe('scoreToGrade', () => {
  it.each([
    [6, 'B+'],
    [5, 'B+'],
    [4, 'B'],
    [3, 'C+'],
    [2, 'C'],
    [1, 'D'],
    [0, 'D'],
  ])('maps score %i to grade %s', (score, grade) => {
    expect(scoreToGrade(score)).toBe(grade);
  });

  it('handles fractional values via floor-style thresholds', () => {
    expect(scoreToGrade(4.9)).toBe('B'); // < 5
    expect(scoreToGrade(5.0)).toBe('B+');
  });
});
