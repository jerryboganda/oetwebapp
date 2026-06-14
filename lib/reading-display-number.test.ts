import { readingPublicDisplayNumber, READING_PART_C_PUBLIC_OFFSET } from '@/lib/reading-display-number';

describe('readingPublicDisplayNumber', () => {
  it('leaves Part A internal order unchanged (1..20)', () => {
    expect(readingPublicDisplayNumber('A', 1)).toBe(1);
    expect(readingPublicDisplayNumber('A', 20)).toBe(20);
  });

  it('leaves Part B internal order unchanged (1..6)', () => {
    expect(readingPublicDisplayNumber('B', 1)).toBe(1);
    expect(readingPublicDisplayNumber('B', 6)).toBe(6);
  });

  it('offsets Part C by +6 so C1 1..8 -> 7..14 and C2 9..16 -> 15..22', () => {
    expect(readingPublicDisplayNumber('C', 1)).toBe(7);
    expect(readingPublicDisplayNumber('C', 8)).toBe(14);
    expect(readingPublicDisplayNumber('C', 9)).toBe(15);
    expect(readingPublicDisplayNumber('C', 16)).toBe(22);
  });

  it('exposes the Part C offset as a named constant', () => {
    expect(READING_PART_C_PUBLIC_OFFSET).toBe(6);
  });
});
