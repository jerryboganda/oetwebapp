import { describe, expect, it } from 'vitest';
import {
  buildBillingCsvString,
  sanitizeCsvCell,
  sanitizeCsvRows,
} from '@/components/admin/billing/csv-utils';

describe('admin billing CSV injection prevention', () => {
  it('prefixes formula triggers with a single quote', () => {
    expect(sanitizeCsvCell('=SUM(A1:A2)')).toBe("'=SUM(A1:A2)");
    expect(sanitizeCsvCell('+1+1')).toBe("'+1+1");
    expect(sanitizeCsvCell('-2+3')).toBe("'-2+3");
    expect(sanitizeCsvCell('@cmd')).toBe("'@cmd");
    expect(sanitizeCsvCell('\tHIDDEN')).toBe("'\tHIDDEN");
    expect(sanitizeCsvCell('\rPAYLOAD')).toBe("'\rPAYLOAD");
  });

  it('leaves benign values untouched', () => {
    expect(sanitizeCsvCell('Plan A')).toBe('Plan A');
    expect(sanitizeCsvCell('29.99')).toBe('29.99');
    expect(sanitizeCsvCell('')).toBe('');
    expect(sanitizeCsvCell(null)).toBeNull();
    expect(sanitizeCsvCell(undefined)).toBeUndefined();
    // numbers and booleans are pass-through (cannot be coerced into formulas)
    expect(sanitizeCsvCell(42)).toBe(42);
    expect(sanitizeCsvCell(true)).toBe(true);
  });

  it('sanitises every cell in every row', () => {
    const out = sanitizeCsvRows([
      { name: '=cmd|"calc"!A1', price: 10 },
      { name: 'Plan B', price: '+99' },
    ]);
    expect(out[0].name).toBe('\'=cmd|"calc"!A1');
    expect(out[0].price).toBe(10);
    expect(out[1].name).toBe('Plan B');
    expect(out[1].price).toBe("'+99");
  });

  it('emits sanitised values in the final CSV string', () => {
    const csv = buildBillingCsvString([{ code: '=evil()', name: 'Plan' }]);
    expect(csv).toContain("'=evil()");
    expect(csv).not.toMatch(/^code,name\r\n=evil/m);
  });
});
