/**
 * RFC 4180-compliant CSV export utility.
 * Browser-side only — no backend changes needed.
 *
 * Formula-prefix sanitization (CSV injection / OWASP-style attack class):
 * Excel, Numbers, and Google Sheets evaluate any cell whose first character is
 * `=`, `+`, `-`, `@`, `\t`, `\r`, or `\u0000` as a formula. An attacker who
 * controls a learner-facing string (e.g. "+cmd|' /C calc'!A1") could escalate a
 * benign export into RCE on the operator's machine. We prefix any such cell
 * with a single quote so the spreadsheet engine renders it as plain text.
 *
 * Slice G — May 2026 admin billing hardening; lifted to the shared helper
 * during 2026-05-12 closure so every export across the app benefits.
 */

const CSV_INJECTION_PREFIXES = new Set(['=', '+', '-', '@', '\t', '\r', '\u0000']);

function sanitizeCsvCell(str: string): string {
  if (str.length === 0) return str;
  return CSV_INJECTION_PREFIXES.has(str[0]) ? `'${str}` : str;
}

function escapeCsvValue(value: unknown): string {
  if (value === null || value === undefined) return '';
  const sanitized = sanitizeCsvCell(String(value));
  if (sanitized.includes(',') || sanitized.includes('"') || sanitized.includes('\n') || sanitized.includes('\r')) {
    return `"${sanitized.replace(/"/g, '""')}"`;
  }
  return sanitized;
}

export function convertToCsvString(data: Record<string, unknown>[]): string {
  if (data.length === 0) return '';

  const keySet = new Set<string>();
  for (const row of data) {
    for (const key of Object.keys(row)) {
      keySet.add(key);
    }
  }
  const headers = [...keySet];

  const headerRow = headers.map(escapeCsvValue).join(',');
  const rows = data.map((row) =>
    headers.map((h) => escapeCsvValue(row[h])).join(','),
  );

  return [headerRow, ...rows].join('\r\n');
}

export function formatDateForExport(date: string | Date): string {
  const d = date instanceof Date ? date : new Date(date);
  if (isNaN(d.getTime())) return String(date);
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

export function exportToCsv(data: Record<string, unknown>[], filename: string): void {
  if (data.length === 0) return;

  const csvString = convertToCsvString(data);
  const blob = new Blob([csvString], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
