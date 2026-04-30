import { describe, it, expect } from 'vitest';
import { aggregateWeaknesses } from '../aggregate';
import type { WeaknessDataPoint } from '../types';

const baseDate = new Date('2025-01-15T12:00:00Z');

const dp = (
  tag: string,
  daysAgo: number,
  criterion?: WeaknessDataPoint['criterion'],
): WeaknessDataPoint => ({
  tag,
  occurredAt: new Date(baseDate.getTime() - daysAgo * 86_400_000).toISOString(),
  ...(criterion ? { criterion } : {}),
});

describe('aggregateWeaknesses', () => {
  it('returns empty summary for no data', () => {
    const r = aggregateWeaknesses([], { endDate: baseDate, trendDays: 7 });
    expect(r.totalObservations).toBe(0);
    expect(r.topTags).toHaveLength(0);
    expect(r.trend).toHaveLength(7);
    expect(r.firstSeenAt).toBe('');
  });

  it('drops unknown tags so upstream typos do not pollute the chart', () => {
    const r = aggregateWeaknesses(
      [dp('missing_key_content', 0), dp('totally_made_up', 0)],
      { endDate: baseDate, trendDays: 5 },
    );
    expect(r.totalObservations).toBe(1);
    expect(r.topTags).toHaveLength(1);
    expect(r.topTags[0].tag).toBe('missing_key_content');
  });

  it('ranks tags by count and computes share', () => {
    const r = aggregateWeaknesses(
      [
        dp('missing_key_content', 0),
        dp('missing_key_content', 1),
        dp('missing_key_content', 2),
        dp('informal_tone', 0),
      ],
      { endDate: baseDate },
    );
    expect(r.topTags[0].tag).toBe('missing_key_content');
    expect(r.topTags[0].count).toBe(3);
    expect(r.topTags[0].share).toBeCloseTo(0.75, 5);
    expect(r.topTags[1].tag).toBe('informal_tone');
  });

  it('limits topN', () => {
    const tags = ['missing_key_content', 'irrelevant_content', 'unclear_purpose', 'informal_tone'];
    const r = aggregateWeaknesses(
      tags.map((t) => dp(t, 0)),
      { endDate: baseDate, topN: 2 },
    );
    expect(r.topTags).toHaveLength(2);
  });

  it('aggregates by criterion when provided', () => {
    const r = aggregateWeaknesses(
      [
        dp('missing_key_content', 0, 'content'),
        dp('informal_tone', 0, 'genre'),
        dp('grammar_articles', 0, 'language'),
        dp('grammar_articles', 1, 'language'),
      ],
      { endDate: baseDate },
    );
    expect(r.byCriterion[0].criterion).toBe('language');
    expect(r.byCriterion[0].count).toBe(2);
    expect(r.byCriterion[0].share).toBeCloseTo(0.5, 5);
  });

  it('builds a trend bucket per day, in chronological order', () => {
    const r = aggregateWeaknesses(
      [dp('missing_key_content', 0), dp('missing_key_content', 0), dp('missing_key_content', 2)],
      { endDate: baseDate, trendDays: 5 },
    );
    expect(r.trend).toHaveLength(5);
    expect(r.trend[r.trend.length - 1].count).toBe(2); // today
    expect(r.trend[r.trend.length - 3].count).toBe(1); // 2 days ago
    // Dates strictly ascending.
    for (let i = 1; i < r.trend.length; i += 1) {
      expect(r.trend[i].date > r.trend[i - 1].date).toBe(true);
    }
  });

  it('records firstSeenAt and lastSeenAt', () => {
    const r = aggregateWeaknesses(
      [dp('missing_key_content', 5), dp('informal_tone', 0)],
      { endDate: baseDate },
    );
    expect(new Date(r.firstSeenAt).getTime()).toBeLessThan(new Date(r.lastSeenAt).getTime());
  });
});
