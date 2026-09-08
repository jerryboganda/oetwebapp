import { describe, expect, it } from 'vitest';
import {
  SPEAKING_PRIMARY_CATEGORIES,
  isSpeakingPrimaryCategory,
  speakingCategoryFilterOptions,
} from './category-taxonomy';

// FINAL 2026-09-09 — the §8B classification engine moved server-only (see
// SpeakingCardClassifierTests.cs for the PDF §8C example suite). This file
// now only covers the thin taxonomy constants/helpers still used by the UI.

describe('speakingCategoryFilterOptions', () => {
  it('returns one option per primary category, in display order', () => {
    const options = speakingCategoryFilterOptions();
    expect(options).toHaveLength(SPEAKING_PRIMARY_CATEGORIES.length);
    expect(options.map((o) => o.id)).toEqual([...SPEAKING_PRIMARY_CATEGORIES]);
    expect(options.every((o) => o.id === o.label)).toBe(true);
  });
});

describe('isSpeakingPrimaryCategory', () => {
  it('accepts every known category', () => {
    for (const category of SPEAKING_PRIMARY_CATEGORIES) {
      expect(isSpeakingPrimaryCategory(category)).toBe(true);
    }
  });

  it('rejects an unknown string', () => {
    expect(isSpeakingPrimaryCategory('Not A Real Category')).toBe(false);
  });
});
