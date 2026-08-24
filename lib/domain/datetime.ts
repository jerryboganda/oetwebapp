/**
 * Shared date/time formatters — FE-015.
 *
 * Ad-hoc `toLocaleDateString()` / `toLocaleString()` calls pick up the
 * browser locale and timezone, so the same ISO timestamp rendered as
 * "13 Aug 2026" for one learner and "8/13/2026" for another. Keep
 * display dates on a single en-GB + explicit timezone contract.
 */

export const DISPLAY_LOCALE = 'en-GB';
export const DISPLAY_TIME_ZONE = 'Europe/London';

const DATE_OPTIONS: Intl.DateTimeFormatOptions = {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  timeZone: DISPLAY_TIME_ZONE,
};

const DATETIME_OPTIONS: Intl.DateTimeFormatOptions = {
  ...DATE_OPTIONS,
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
};

const TIME_OPTIONS: Intl.DateTimeFormatOptions = {
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
  timeZone: DISPLAY_TIME_ZONE,
};

function toValidDate(value: string | number | Date | null | undefined): Date | null {
  if (value == null || value === '') return null;
  const date = value instanceof Date ? value : new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

/** Format a date as `13 Aug 2026` (en-GB, Europe/London). */
export function formatDate(
  value: string | number | Date | null | undefined,
  fallback = '—',
): string {
  const date = toValidDate(value);
  return date ? new Intl.DateTimeFormat(DISPLAY_LOCALE, DATE_OPTIONS).format(date) : fallback;
}

/** Format a date+time as `13 Aug 2026, 14:30` (en-GB, Europe/London). */
export function formatDateTime(
  value: string | number | Date | null | undefined,
  fallback = 'Not recorded',
): string {
  const date = toValidDate(value);
  return date ? new Intl.DateTimeFormat(DISPLAY_LOCALE, DATETIME_OPTIONS).format(date) : fallback;
}

/** Format a time as `14:30` (en-GB, Europe/London). */
export function formatTime(
  value: string | number | Date | null | undefined,
  fallback = '—',
): string {
  const date = toValidDate(value);
  return date ? new Intl.DateTimeFormat(DISPLAY_LOCALE, TIME_OPTIONS).format(date) : fallback;
}

/** True when both bounds are set and the end is before the start. */
export function isDateRangeInvalid(
  startsAt: string | number | Date | null | undefined,
  endsAt: string | number | Date | null | undefined,
): boolean {
  const start = toValidDate(startsAt);
  const end = toValidDate(endsAt);
  if (!start || !end) return false;
  return end.getTime() < start.getTime();
}
