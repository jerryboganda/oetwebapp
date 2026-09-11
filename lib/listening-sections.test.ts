import { describe, expect, it } from 'vitest';
import {
  LISTENING_REVIEW_SECONDS,
  LISTENING_SECTION_SEQUENCE,
  computeListeningSectionMap,
  formatReviewSeconds,
  groupQuestionsBySection,
  workspaceBoundaryIndex,
} from './listening-sections';

type Q = { id: string; partCode: string; number: number };

describe('listening-sections', () => {
  it('disables post-audio review windows in every section (audio plays once, then auto-advances)', () => {
    expect(LISTENING_REVIEW_SECONDS.A1).toBe(0);
    expect(LISTENING_REVIEW_SECONDS.A2).toBe(0);
    expect(LISTENING_REVIEW_SECONDS.B).toBe(0);
    expect(LISTENING_REVIEW_SECONDS.C1).toBe(0);
    expect(LISTENING_REVIEW_SECONDS.C2).toBe(0);
  });

  it('orders the section sequence A1 → A2 → B → C1 → C2', () => {
    expect(LISTENING_SECTION_SEQUENCE).toEqual(['A1', 'A2', 'B', 'C1', 'C2']);
  });

  it('accepts granular section codes directly', () => {
    const qs: Q[] = [
      { id: 'q1', partCode: 'A1', number: 1 },
      { id: 'q2', partCode: 'A2', number: 13 },
      { id: 'q3', partCode: 'B', number: 25 },
      { id: 'q4', partCode: 'C1', number: 31 },
      { id: 'q5', partCode: 'C2', number: 37 },
    ];
    const map = computeListeningSectionMap(qs);
    expect(map.get('q1')).toBe('A1');
    expect(map.get('q2')).toBe('A2');
    expect(map.get('q3')).toBe('B');
    expect(map.get('q4')).toBe('C1');
    expect(map.get('q5')).toBe('C2');
  });

  it('splits legacy Part A evenly into A1 and A2 by question number', () => {
    const qs: Q[] = Array.from({ length: 24 }, (_, i) => ({
      id: `a-${i + 1}`,
      partCode: 'A',
      number: i + 1,
    }));
    const groups = groupQuestionsBySection(qs);
    expect(groups.A1).toHaveLength(12);
    expect(groups.A2).toHaveLength(12);
    expect(groups.A1.every((q) => q.number <= 12)).toBe(true);
    expect(groups.A2.every((q) => q.number > 12)).toBe(true);
  });

  it('splits legacy Part C evenly into C1 and C2', () => {
    const qs: Q[] = Array.from({ length: 12 }, (_, i) => ({
      id: `c-${i + 1}`,
      partCode: 'C',
      number: i + 31,
    }));
    const groups = groupQuestionsBySection(qs);
    expect(groups.C1).toHaveLength(6);
    expect(groups.C2).toHaveLength(6);
  });

  it('recovers legacy or missing codes from the authoritative B/C question ranges', () => {
    const qs: Q[] = [
      { id: 'b-25', partCode: '', number: 25 },
      { id: 'b-30', partCode: 'legacy', number: 30 },
      { id: 'c-31', partCode: 'C', number: 31 },
      { id: 'c-37', partCode: 'C', number: 37 },
      { id: 'c-42', partCode: '', number: 42 },
    ];
    const groups = groupQuestionsBySection(qs);
    expect(groups.B.map((q) => q.number)).toEqual([25, 30]);
    expect(groups.C1.map((q) => q.number)).toEqual([31]);
    expect(groups.C2.map((q) => q.number)).toEqual([37, 42]);
  });

  it('formats countdown seconds as mm:ss', () => {
    expect(formatReviewSeconds(120)).toBe('02:00');
    expect(formatReviewSeconds(60)).toBe('01:00');
    expect(formatReviewSeconds(30)).toBe('00:30');
    expect(formatReviewSeconds(0)).toBe('00:00');
    expect(formatReviewSeconds(-5)).toBe('00:00');
    expect(formatReviewSeconds(5)).toBe('00:05');
  });

  describe('workspaceBoundaryIndex', () => {
    // Part C is presented as one Q31–Q42 workspace while C1 and C2 are separate
    // audio extracts, so the boundary — not the end of the workspace — is where
    // the sub-section transition belongs.
    const workspace = Array.from({ length: 12 }, (_, i) => ({ id: `q-${31 + i}` }));
    const idsFor = (numbers: number[]) => new Set(numbers.map((n) => `q-${n}`));
    const partC1 = idsFor([31, 32, 33, 34, 35, 36]);
    const partC2 = idsFor([37, 38, 39, 40, 41, 42]);

    it('puts the C1 boundary on Q36, not the end of the Q31–Q42 workspace', () => {
      expect(workspaceBoundaryIndex(workspace, partC1)).toBe(5);
      expect(workspace[workspaceBoundaryIndex(workspace, partC1)].id).toBe('q-36');
    });

    it('puts the C2 boundary on Q42, the final card', () => {
      expect(workspaceBoundaryIndex(workspace, partC2)).toBe(11);
      expect(workspace[workspaceBoundaryIndex(workspace, partC2)].id).toBe('q-42');
    });

    it('follows the real membership when an extract is short', () => {
      expect(workspaceBoundaryIndex(workspace, idsFor([31, 32]))).toBe(1);
      expect(workspaceBoundaryIndex(workspace, idsFor([37, 38, 39]))).toBe(8);
    });

    it('returns the last index for a section that owns the whole workspace', () => {
      const allIds = idsFor(workspace.map((q) => Number(q.id.slice(2))));
      expect(workspaceBoundaryIndex(workspace, allIds)).toBe(workspace.length - 1);
      expect(workspaceBoundaryIndex([{ id: 'q-25' }, { id: 'q-30' }], idsFor([25, 30]))).toBe(1);
    });

    it('falls back to the last index when nothing in the workspace is owned', () => {
      expect(workspaceBoundaryIndex(workspace, new Set<string>())).toBe(workspace.length - 1);
    });
  });
});
