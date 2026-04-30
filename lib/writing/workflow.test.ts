import {
  DEFAULT_WRITING_TARGET_WORD_RANGE,
  getWritingWordCountStatus,
  normalizeWritingPracticeMode,
  WRITING_CRITERIA,
  WRITING_READING_WINDOW_SECONDS,
  WRITING_WINDOW_SECONDS,
} from './workflow';

describe('writing workflow helpers', () => {
  it('normalizes only explicit learning mode away from strict exam mode', () => {
    expect(normalizeWritingPracticeMode(undefined)).toBe('exam');
    expect(normalizeWritingPracticeMode(null)).toBe('exam');
    expect(normalizeWritingPracticeMode('exam')).toBe('exam');
    expect(normalizeWritingPracticeMode('timed')).toBe('exam');
    expect(normalizeWritingPracticeMode('learning')).toBe('learning');
    expect(normalizeWritingPracticeMode('Learning')).toBe('learning');
  });

  it('keeps the official writing timing split explicit', () => {
    expect(WRITING_READING_WINDOW_SECONDS).toBe(300);
    expect(WRITING_WINDOW_SECONDS).toBe(2400);
  });

  it('classifies body-word guidance without making word count an automatic fail', () => {
    expect(getWritingWordCountStatus(0).state).toBe('empty');
    expect(getWritingWordCountStatus(DEFAULT_WRITING_TARGET_WORD_RANGE.warningMin - 1).state).toBe('under-warning');
    expect(getWritingWordCountStatus(DEFAULT_WRITING_TARGET_WORD_RANGE.min - 1).state).toBe('near-target');
    expect(getWritingWordCountStatus(DEFAULT_WRITING_TARGET_WORD_RANGE.min).state).toBe('target');
    expect(getWritingWordCountStatus(DEFAULT_WRITING_TARGET_WORD_RANGE.max).state).toBe('target');
    expect(getWritingWordCountStatus(DEFAULT_WRITING_TARGET_WORD_RANGE.warningMax + 1).state).toBe('over-warning');
    expect(getWritingWordCountStatus(DEFAULT_WRITING_TARGET_WORD_RANGE.warningMax + 1).message).toMatch(/not mark down by count alone/i);
  });

  it('exposes the six OET Writing criteria with canonical max scores', () => {
    expect(WRITING_CRITERIA.map((criterion) => criterion.code)).toEqual([
      'purpose',
      'content',
      'conciseness_clarity',
      'genre_style',
      'organisation_layout',
      'language',
    ]);
    expect(WRITING_CRITERIA.find((criterion) => criterion.code === 'purpose')?.maxScore).toBe(3);
    expect(WRITING_CRITERIA.filter((criterion) => criterion.code !== 'purpose').every((criterion) => criterion.maxScore === 7)).toBe(true);
  });
});
