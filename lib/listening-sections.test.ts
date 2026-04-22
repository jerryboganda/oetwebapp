import { describe, expect, it } from 'vitest';
import {
  LISTENING_REVIEW_SECONDS,
  LISTENING_SECTION_SEQUENCE,
  computeListeningSectionMap,
  formatReviewSeconds,
  groupQuestionsBySection,
} from './listening-sections';

type Q = { id: string; partCode: string; number: number };

describe('listening-sections', () => {
  it('respects canonical review-window durations per OET CBT rules', () => {
    expect(LISTENING_REVIEW_SECONDS.A1).toBe(60);
    expect(LISTENING_REVIEW_SECONDS.A2).toBe(60);
    expect(LISTENING_REVIEW_SECONDS.B).toBe(0);
    expect(LISTENING_REVIEW_SECONDS.C1).toBe(30);
    expect(LISTENING_REVIEW_SECONDS.C2).toBe(120);
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

  it('formats countdown seconds as mm:ss', () => {
    expect(formatReviewSeconds(120)).toBe('02:00');
    expect(formatReviewSeconds(60)).toBe('01:00');
    expect(formatReviewSeconds(30)).toBe('00:30');
    expect(formatReviewSeconds(0)).toBe('00:00');
    expect(formatReviewSeconds(-5)).toBe('00:00');
    expect(formatReviewSeconds(5)).toBe('00:05');
  });
});
