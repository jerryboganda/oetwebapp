import { describe, expect, it } from 'vitest';
import { hasApprovedListeningConversion } from './listening-result-display';

describe('hasApprovedListeningConversion', () => {
  it('allows owner-table conversion for a complete 42-item paper', () => {
    expect(hasApprovedListeningConversion({
      maxRawScore: 42,
      scaledScore: 350,
      scoreConversionTableVersionKey: 'lr-v1',
      passed: true,
    })).toBe(true);
  });

  it('keeps subset practice attempts raw-only even when conversion fields leak in', () => {
    expect(hasApprovedListeningConversion({
      maxRawScore: 10,
      scaledScore: 350,
      scoreConversionTableVersionKey: 'lr-v1',
      passed: true,
    })).toBe(false);
  });

  it('fails closed for malformed full-paper conversion fields', () => {
    expect(hasApprovedListeningConversion({
      maxRawScore: 42,
      scaledScore: null,
      scoreConversionTableVersionKey: 'lr-v1',
      passed: true,
    })).toBe(false);
  });
});
