import { describe, it, expect } from 'vitest';
import { formatMoney, formatMoneyWhole } from '@/lib/money';

describe('formatMoney', () => {
  it('formats AUD with the en-AU locale by default', () => {
    const out = formatMoney(49.5);
    // en-AU AUD renders as "$49.50" (currency symbol + amount).
    expect(out).toMatch(/\$\s?49\.50/);
  });

  it('formats USD using the wallet currency, not concatenated $', () => {
    const out = formatMoney(20, { currency: 'USD' });
    // ICU may render "US$20.00" or "USD 20.00" depending on the build.
    expect(out).toMatch(/(US\$|USD)\s?20\.00/);
  });

  it('formats EUR with the de-DE locale', () => {
    const out = formatMoney(1234.56, { currency: 'EUR', locale: 'de-DE' });
    // de-DE uses a comma decimal separator, e.g. "1.234,56 €".
    expect(out).toContain('1.234,56');
    expect(out).toContain('€');
  });

  it('falls back to the default locale when an unknown locale is supplied', () => {
    // RangeError-throwing locales should not propagate to callers.
    expect(() => formatMoney(10, { locale: 'not-a-locale-xx-yy', currency: 'GBP' })).not.toThrow();
    const out = formatMoney(10, { locale: 'not-a-locale-xx-yy', currency: 'GBP' });
    expect(out).toMatch(/(£|GBP)/);
  });

  it('falls back to a stable representation for unknown currency codes', () => {
    const out = formatMoney(5, { currency: 'XX1' });
    // "XX1" is not a valid ISO 4217 code → normalises to AUD.
    expect(out).toMatch(/\$|AUD/);
  });

  it('coerces null / undefined / NaN amounts to 0', () => {
    expect(formatMoney(null)).toMatch(/0\.00/);
    expect(formatMoney(undefined)).toMatch(/0\.00/);
    expect(formatMoney(Number.NaN)).toMatch(/0\.00/);
  });

  it('formatMoneyWhole drops fraction digits', () => {
    const out = formatMoneyWhole(49, { currency: 'AUD' });
    expect(out).not.toContain('.00');
    expect(out).toMatch(/\$\s?49(\b|$)/);
  });
});
