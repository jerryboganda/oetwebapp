import { describe, expect, it, vi } from 'vitest';

import { isPasteExempt } from './paste-exempt';

describe('isPasteExempt', () => {
  it('matches the exempt account, case-insensitively and trimmed', () => {
    expect(isPasteExempt('drahmedheshamuk2025@gmail.com')).toBe(true);
    expect(isPasteExempt('  DrAhmedHeshamUK2025@Gmail.com ')).toBe(true);
  });

  it('rejects every other account', () => {
    expect(isPasteExempt('learner@oet-prep.dev')).toBe(false);
    expect(isPasteExempt('xdrahmedheshamuk2025@gmail.com')).toBe(false);
    expect(isPasteExempt(null)).toBe(false);
    expect(isPasteExempt(undefined)).toBe(false);
    expect(isPasteExempt('')).toBe(false);
  });
});
