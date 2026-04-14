import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { convertToCsvString, formatDateForExport, exportToCsv } from './csv-export';

describe('convertToCsvString', () => {
  it('returns header row + data rows for simple objects', () => {
    const data = [
      { name: 'Alice', score: 90 },
      { name: 'Bob', score: 85 },
    ];
    const csv = convertToCsvString(data);
    expect(csv).toBe('name,score\r\nAlice,90\r\nBob,85');
  });

  it('returns empty string for empty array', () => {
    expect(convertToCsvString([])).toBe('');
  });

  it('escapes values containing commas', () => {
    const data = [{ note: 'hello, world', id: 1 }];
    const csv = convertToCsvString(data);
    expect(csv).toBe('note,id\r\n"hello, world",1');
  });

  it('escapes values containing double quotes', () => {
    const data = [{ note: 'say "hi"', id: 2 }];
    const csv = convertToCsvString(data);
    expect(csv).toBe('note,id\r\n"say ""hi""",2');
  });

  it('escapes values containing newlines', () => {
    const data = [{ note: 'line1\nline2', id: 3 }];
    const csv = convertToCsvString(data);
    expect(csv).toBe('note,id\r\n"line1\nline2",3');
  });

  it('handles null and undefined values as empty string', () => {
    const data = [{ a: null, b: undefined, c: 'ok' }];
    const csv = convertToCsvString(data);
    expect(csv).toBe('a,b,c\r\n,,ok');
  });

  it('handles boolean and numeric values', () => {
    const data = [{ active: true, count: 0, rate: 3.14 }];
    const csv = convertToCsvString(data);
    expect(csv).toBe('active,count,rate\r\ntrue,0,3.14');
  });

  it('escapes header names containing special characters', () => {
    const data = [{ 'full, name': 'Alice', 'score "raw"': 90 }];
    const csv = convertToCsvString(data);
    expect(csv).toContain('"full, name"');
    expect(csv).toContain('"score ""raw"""');
  });

  it('uses union of all keys when objects have different shapes', () => {
    const data = [
      { a: 1, b: 2 },
      { b: 3, c: 4 },
    ];
    const csv = convertToCsvString(data);
    const lines = csv.split('\r\n');
    expect(lines[0]).toBe('a,b,c');
    expect(lines[1]).toBe('1,2,');
    expect(lines[2]).toBe(',3,4');
  });
});

describe('formatDateForExport', () => {
  it('formats a Date object to YYYY-MM-DD', () => {
    const d = new Date(2025, 0, 15);
    expect(formatDateForExport(d)).toBe('2025-01-15');
  });

  it('formats an ISO string to YYYY-MM-DD', () => {
    expect(formatDateForExport('2025-06-03T14:30:00Z')).toBe('2025-06-03');
  });

  it('returns the string as-is if not a valid date', () => {
    expect(formatDateForExport('not-a-date')).toBe('not-a-date');
  });
});

describe('exportToCsv', () => {
  let createObjectURLMock: ReturnType<typeof vi.fn>;
  let revokeObjectURLMock: ReturnType<typeof vi.fn>;
  let appendChildSpy: ReturnType<typeof vi.spyOn>;
  let removeChildSpy: ReturnType<typeof vi.spyOn>;
  let clickSpy: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    createObjectURLMock = vi.fn(() => 'blob:mock-url');
    revokeObjectURLMock = vi.fn();
    clickSpy = vi.fn();

    Object.defineProperty(globalThis, 'URL', {
      value: { createObjectURL: createObjectURLMock, revokeObjectURL: revokeObjectURLMock },
      writable: true,
    });

    Object.defineProperty(globalThis, 'Blob', {
      value: class MockBlob {
        parts: unknown[];
        options: unknown;
        constructor(parts: unknown[], options: unknown) {
          this.parts = parts;
          this.options = options;
        }
      },
      writable: true,
    });

    appendChildSpy = vi.spyOn(document.body, 'appendChild').mockImplementation((node) => {
      // Trigger click when appended
      return node;
    });
    removeChildSpy = vi.spyOn(document.body, 'removeChild').mockImplementation((node) => node);

    vi.spyOn(document, 'createElement').mockImplementation(((tag: string): HTMLElement => {
      if (tag === 'a') {
        const mockAnchor = {
          href: '',
          download: '',
          click: clickSpy,
          remove: vi.fn(),
        };
        return mockAnchor as unknown as HTMLAnchorElement;
      }
      return document.createElement(tag);
    }) as any);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('creates a Blob with CSV content and triggers download', () => {
    const data = [{ name: 'Alice', score: 90 }];
    exportToCsv(data, 'test-export.csv');

    expect(createObjectURLMock).toHaveBeenCalledOnce();
    expect(clickSpy).toHaveBeenCalledOnce();
    expect(revokeObjectURLMock).toHaveBeenCalledWith('blob:mock-url');
  });

  it('does nothing for empty data', () => {
    exportToCsv([], 'empty.csv');
    expect(createObjectURLMock).not.toHaveBeenCalled();
  });
});
