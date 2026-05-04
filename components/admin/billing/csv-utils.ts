/**
 * CSV-export safety helpers for admin billing.
 *
 * Wraps the shared `lib/csv-export.ts` helper with formula-injection prevention:
 * any cell whose first character is `=`, `+`, `-`, `@`, tab, or carriage return
 * is prefixed with a single quote. This is the OWASP-recommended mitigation
 * against spreadsheet formula injection (CSV → Excel/Sheets).
 *
 * Usage:
 *   exportBillingCsv(rows, 'plans.csv');
 */

import { exportToCsv, convertToCsvString } from '@/lib/csv-export';

const FORMULA_PREFIXES = ['=', '+', '-', '@', '\t', '\r', '\u0000'] as const;

/**
 * Prefix a single cell value with `'` if it would be interpreted as a formula
 * by Excel / Google Sheets / LibreOffice Calc. Numbers and booleans are left
 * intact (they cannot be coerced into formulas without a leading symbol).
 */
export function sanitizeCsvCell(value: unknown): unknown {
  if (value === null || value === undefined) return value;
  if (typeof value !== 'string') return value;
  if (value.length === 0) return value;
  const first = value.charAt(0);
  if ((FORMULA_PREFIXES as readonly string[]).includes(first)) {
    return `'${value}`;
  }
  return value;
}

export function sanitizeCsvRows<T extends Record<string, unknown>>(rows: T[]): T[] {
  return rows.map((row) => {
    const next: Record<string, unknown> = {};
    for (const key of Object.keys(row)) {
      next[key] = sanitizeCsvCell(row[key]);
    }
    return next as T;
  });
}

/**
 * Sanitised CSV string — useful for tests or non-browser callers.
 */
export function buildBillingCsvString(rows: Record<string, unknown>[]): string {
  return convertToCsvString(sanitizeCsvRows(rows));
}

/**
 * Trigger a browser download of `rows` as a sanitised CSV file.
 * Always routes through `lib/csv-export.ts` (no direct Blob construction here).
 */
export function exportBillingCsv(rows: Record<string, unknown>[], filename: string): void {
  exportToCsv(sanitizeCsvRows(rows), filename);
}
