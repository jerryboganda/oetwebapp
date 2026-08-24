import { describe, expect, it } from 'vitest';

import { formatDate, formatDateTime, isDateRangeInvalid } from './datetime';

describe('datetime helpers', () => {
  it('formats a UTC date in en-GB Europe/London', () => {
    expect(formatDate('2026-08-13T12:00:00.000Z')).toBe('13 Aug 2026');
  });

  it('returns the fallback for empty or invalid values', () => {
    expect(formatDate(null)).toBe('—');
    expect(formatDate('not-a-date', 'n/a')).toBe('n/a');
    expect(formatDateTime(undefined)).toBe('Not recorded');
  });

  it('rejects an end date before the start date', () => {
    expect(isDateRangeInvalid('2026-08-13T10:00', '2026-08-12T10:00')).toBe(true);
    expect(isDateRangeInvalid('2026-08-13T10:00', '2026-08-13T11:00')).toBe(false);
    expect(isDateRangeInvalid('', '2026-08-13T11:00')).toBe(false);
  });
});
