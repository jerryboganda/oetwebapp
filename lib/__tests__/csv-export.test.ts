import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import {
  convertToCsvString,
  formatDateForExport,
  exportToCsv,
} from '../csv-export';

describe('convertToCsvString', () => {
  it('returns empty string for empty array', () => {
    expect(convertToCsvString([])).toBe('');
  });

  it('emits header row + data row separated by CRLF', () => {
    const out = convertToCsvString([{ a: 1, b: 'x' }]);
    expect(out).toBe('a,b\r\n1,x');
  });

  it('escapes values containing commas', () => {
    const out = convertToCsvString([{ name: 'Smith, John' }]);
    expect(out).toBe('name\r\n"Smith, John"');
  });

  it('escapes values containing double quotes by doubling them', () => {
    const out = convertToCsvString([{ q: 'He said "hi"' }]);
    expect(out).toBe('q\r\n"He said ""hi"""');
  });

  it('escapes values containing newlines and carriage returns', () => {
    const out = convertToCsvString([{ note: 'line1\nline2' }, { note: 'a\rb' }]);
    expect(out).toBe('note\r\n"line1\nline2"\r\n"a\rb"');
  });

  it('coerces null and undefined to empty string', () => {
    const out = convertToCsvString([{ a: null, b: undefined, c: 'x' }]);
    expect(out).toBe('a,b,c\r\n,,x');
  });

  it('unions all keys across heterogeneous rows', () => {
    const out = convertToCsvString([{ a: 1 }, { b: 2 }, { a: 3, b: 4 }]);
    const lines = out.split('\r\n');
    // headers: a,b — rows: 1,, then ,2, then 3,4
    expect(lines[0]).toBe('a,b');
    expect(lines[1]).toBe('1,');
    expect(lines[2]).toBe(',2');
    expect(lines[3]).toBe('3,4');
  });

  it('coerces non-string scalars (numbers, booleans) to string', () => {
    const out = convertToCsvString([{ n: 42, b: true, z: 0 }]);
    expect(out).toBe('n,b,z\r\n42,true,0');
  });

  it('does not allow CSV injection by silently re-quoting "=" prefixed values', () => {
    // The util doesn't sanitise formula injection, but it must NOT lose the =
    // (callers are responsible for prefixing single-quote per their threat model).
    const out = convertToCsvString([{ formula: '=SUM(A1:A2)' }]);
    expect(out).toContain('=SUM(A1:A2)');
  });
});

describe('formatDateForExport', () => {
  it('formats Date as YYYY-MM-DD', () => {
    expect(formatDateForExport(new Date(2026, 0, 5))).toBe('2026-01-05');
  });

  it('parses ISO strings', () => {
    expect(formatDateForExport('2026-04-26T12:34:56Z')).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it('returns the raw input when the date is invalid', () => {
    expect(formatDateForExport('not-a-date')).toBe('not-a-date');
  });

  it('zero-pads single-digit months and days', () => {
    expect(formatDateForExport(new Date(2026, 8, 3))).toBe('2026-09-03');
  });
});

describe('exportToCsv (browser side-effects)', () => {
  let createObjectURL: ReturnType<typeof vi.fn>;
  let revokeObjectURL: ReturnType<typeof vi.fn>;
  let appendSpy: ReturnType<typeof vi.spyOn>;
  let removeSpy: ReturnType<typeof vi.spyOn>;
  let clickSpy: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    createObjectURL = vi.fn(() => 'blob:mock-url');
    revokeObjectURL = vi.fn();
    // jsdom doesn't implement these — define them.
    Object.defineProperty(URL, 'createObjectURL', { value: createObjectURL, configurable: true });
    Object.defineProperty(URL, 'revokeObjectURL', { value: revokeObjectURL, configurable: true });

    clickSpy = vi.fn();
    // Force every <a> created during the test to use our click spy.
    appendSpy = vi.spyOn(document.body, 'appendChild').mockImplementation((node: Node) => {
      if ((node as HTMLAnchorElement).tagName === 'A') {
        (node as HTMLAnchorElement).click = clickSpy;
      }
      return node;
    });
    removeSpy = vi.spyOn(HTMLElement.prototype, 'remove').mockImplementation(() => undefined);
  });

  afterEach(() => {
    appendSpy.mockRestore();
    removeSpy.mockRestore();
    vi.restoreAllMocks();
  });

  it('does nothing when data is empty', () => {
    exportToCsv([], 'empty.csv');
    expect(createObjectURL).not.toHaveBeenCalled();
    expect(clickSpy).not.toHaveBeenCalled();
  });

  it('creates a blob URL, clicks an anchor with the given filename, then revokes it', () => {
    exportToCsv([{ a: 1, b: 2 }], 'data.csv');

    expect(createObjectURL).toHaveBeenCalledTimes(1);
    const blob = createObjectURL.mock.calls[0]![0] as Blob;
    expect(blob.type).toBe('text/csv;charset=utf-8;');

    expect(appendSpy).toHaveBeenCalledTimes(1);
    const anchor = appendSpy.mock.calls[0]![0] as HTMLAnchorElement;
    expect(anchor.tagName).toBe('A');
    expect(anchor.download).toBe('data.csv');
    expect(anchor.href).toContain('blob:mock-url');

    expect(clickSpy).toHaveBeenCalledTimes(1);
    expect(removeSpy).toHaveBeenCalled();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock-url');
  });
});
