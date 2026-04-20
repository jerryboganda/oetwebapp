import { describe, it, expect } from 'vitest';
import {
  pronunciationProjectedScaled,
  pronunciationProjectedBand,
} from '@/lib/scoring';

describe('pronunciationProjectedScaled', () => {
  it('maps 0 to 0', () => {
    expect(pronunciationProjectedScaled(0)).toBe(0);
  });

  it('maps 70 exactly to 350 (B pass anchor)', () => {
    expect(pronunciationProjectedScaled(70)).toBe(350);
  });

  it('maps 60 to 300 (C+ anchor)', () => {
    expect(pronunciationProjectedScaled(60)).toBe(300);
  });

  it('maps 100 to 500', () => {
    expect(pronunciationProjectedScaled(100)).toBe(500);
  });

  it('interpolates linearly between anchors', () => {
    // Halfway between 70 and 80 (overall) should be halfway between 350 and 400 scaled
    expect(pronunciationProjectedScaled(75)).toBe(375);
  });

  it('clamps negative values to 0', () => {
    expect(pronunciationProjectedScaled(-10)).toBe(0);
  });

  it('clamps values over 100 to 500', () => {
    expect(pronunciationProjectedScaled(150)).toBe(500);
  });

  it('handles NaN defensively', () => {
    expect(pronunciationProjectedScaled(Number.NaN)).toBe(0);
  });
});

describe('pronunciationProjectedBand', () => {
  it('returns a Speaking pass at overall 70', () => {
    const r = pronunciationProjectedBand(70);
    expect(r.scaledScore).toBe(350);
    expect(r.passed).toBe(true);
    expect(r.subtest).toBe('speaking');
    expect(r.requiredScaled).toBe(350);
    expect(r.requiredGrade).toBe('B');
  });

  it('returns Speaking fail just below the anchor', () => {
    const r = pronunciationProjectedBand(68);
    expect(r.scaledScore).toBeLessThan(350);
    expect(r.passed).toBe(false);
  });

  it('never reports pass for overall < 69', () => {
    for (let o = 0; o < 69; o += 5) {
      expect(pronunciationProjectedBand(o).passed).toBe(false);
    }
  });
});
