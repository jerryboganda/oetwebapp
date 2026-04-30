import { describe, it, expect } from 'vitest';
import { computeImprovementScore } from '../improvement-score';
import type { CriteriaDelta } from '@/lib/mock-data';

const purposeOnly = (original: number, revised: number, max = 3): CriteriaDelta => ({
  name: 'Purpose',
  original,
  revised,
  max,
});

const sevenPoint = (
  name: string,
  original: number,
  revised: number,
  max = 7,
): CriteriaDelta => ({ name, original, revised, max });

describe('computeImprovementScore', () => {
  it('returns 0 when no deltas provided', () => {
    const r = computeImprovementScore({ deltas: [], unresolvedIssuesCount: 0 });
    expect(r.score).toBe(0);
    expect(r.band).toBe('minimal');
    expect(r.totalPossible).toBe(0);
  });

  it('full headroom captured → score 100, band major', () => {
    const r = computeImprovementScore({
      deltas: [
        purposeOnly(1, 3),
        sevenPoint('Content', 4, 7),
        sevenPoint('Conciseness', 4, 7),
        sevenPoint('Genre', 4, 7),
        sevenPoint('Organization', 4, 7),
        sevenPoint('Language', 4, 7),
      ],
      unresolvedIssuesCount: 0,
    });
    expect(r.score).toBe(100);
    expect(r.band).toBe('major');
    expect(r.criteriaImproved).toBe(6);
    expect(r.criteriaRegressed).toBe(0);
  });

  it('half headroom captured + no issues → moderate band', () => {
    const r = computeImprovementScore({
      deltas: [sevenPoint('Content', 3, 5), sevenPoint('Language', 3, 5)],
      unresolvedIssuesCount: 0,
    });
    // possible = (7-3) + (7-3) = 8; gained = 2 + 2 = 4 → 50%
    expect(r.score).toBe(50);
    expect(r.band).toBe('moderate');
  });

  it('caps revised > max so bad data cannot inflate the score', () => {
    const r = computeImprovementScore({
      deltas: [sevenPoint('Content', 3, 99)],
      unresolvedIssuesCount: 0,
    });
    expect(r.score).toBe(100);
    expect(r.totalGained).toBe(4);
  });

  it('regression flagged when any criterion drops', () => {
    const r = computeImprovementScore({
      deltas: [sevenPoint('Content', 5, 3), sevenPoint('Language', 4, 4)],
      unresolvedIssuesCount: 0,
    });
    expect(r.criteriaRegressed).toBe(1);
    expect(r.band).toBe('regressed');
  });

  it('issue penalty caps at 0.30', () => {
    const r = computeImprovementScore({
      deltas: [sevenPoint('Content', 3, 7)], // 100% gain
      unresolvedIssuesCount: 50,
    });
    // 1.0 - 0.3 = 0.7 → 70%
    expect(r.score).toBe(70);
    expect(r.issuePenalty).toBeCloseTo(0.3, 5);
  });

  it('gain ratio cannot go negative even with extreme penalties', () => {
    const r = computeImprovementScore({
      deltas: [sevenPoint('Content', 5, 5)], // 0% gain
      unresolvedIssuesCount: 100,
    });
    expect(r.score).toBeGreaterThanOrEqual(0);
  });

  it('produces a useful summary', () => {
    const r = computeImprovementScore({
      deltas: [sevenPoint('Content', 3, 5)],
      unresolvedIssuesCount: 2,
    });
    expect(r.summary).toMatch(/2 issues still open/);
  });
});
