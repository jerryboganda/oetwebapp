import { describe, expect, it } from 'vitest';
import { convertToCsvString } from '../csv-export';

/**
 * Slice G — May 2026 admin billing hardening, formula-prefix sanitization.
 * Lifted into the shared `lib/csv-export.ts` during 2026-05-12 closure so
 * every export surface in the app inherits the guard.
 *
 * Class of attacks (CSV injection):
 *   - Excel/Numbers/Sheets evaluate any cell starting with `=`, `+`, `-`,
 *     `@`, `\t`, `\r`, or `\u0000` as a formula.
 *   - We prefix the cell with a single quote so the engine renders text.
 */
describe('csv-export — formula-prefix injection guard', () => {
  it('prefixes a leading "=" with a single quote', () => {
    const csv = convertToCsvString([{ field: '=cmd|"/C calc"!A1' }]);
    // The cell contains " so RFC 4180 quoting wraps the prefixed value and doubles internal quotes.
    expect(csv).toBe(`field\r\n"'=cmd|""/C calc""!A1"`);
  });

  it('prefixes a leading "+" with a single quote', () => {
    const csv = convertToCsvString([{ field: '+SUM(1,1)' }]);
    // Comma triggers RFC 4180 quoting around the prefixed value.
    expect(csv).toBe(`field\r\n"'+SUM(1,1)"`);
  });

  it('prefixes a leading "-" with a single quote', () => {
    const csv = convertToCsvString([{ field: '-2+3' }]);
    expect(csv).toBe(`field\r\n'-2+3`);
  });

  it('prefixes a leading "@" with a single quote', () => {
    const csv = convertToCsvString([{ field: '@SUM(A:A)' }]);
    expect(csv).toBe(`field\r\n'@SUM(A:A)`);
  });

  it('prefixes a leading TAB with a single quote', () => {
    const csv = convertToCsvString([{ field: '\tHIDDEN' }]);
    expect(csv).toBe(`field\r\n'\tHIDDEN`);
  });

  it('prefixes a leading carriage return with a single quote', () => {
    const csv = convertToCsvString([{ field: '\rstart' }]);
    expect(csv).toBe(`field\r\n"'\rstart"`);
  });

  it('prefixes a leading null byte with a single quote', () => {
    const csv = convertToCsvString([{ field: '\u0000oops' }]);
    expect(csv).toBe(`field\r\n'\u0000oops`);
  });

  it('does not double-prefix when the cell already starts with a single quote', () => {
    const csv = convertToCsvString([{ field: "'safe" }]);
    expect(csv).toBe(`field\r\n'safe`);
  });

  it('leaves benign content untouched', () => {
    const csv = convertToCsvString([{ field: 'hello world' }]);
    expect(csv).toBe(`field\r\nhello world`);
  });

  it('handles null and undefined as empty cells', () => {
    const csv = convertToCsvString([{ a: null, b: undefined, c: '=DANGER' }]);
    expect(csv).toBe(`a,b,c\r\n,,'=DANGER`);
  });

  it('preserves RFC 4180 quoting for embedded commas alongside the prefix guard', () => {
    const csv = convertToCsvString([{ field: '=A1,B1' }]);
    expect(csv).toBe(`field\r\n"'=A1,B1"`);
  });

  it('preserves RFC 4180 doubling of internal quotes alongside the prefix guard', () => {
    const csv = convertToCsvString([{ field: '=She said "hi"' }]);
    expect(csv).toBe(`field\r\n"'=She said ""hi"""`);
  });
});
