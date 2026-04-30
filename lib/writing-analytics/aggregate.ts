/**
 * ============================================================================
 * Writing — Weakness aggregation (pure)
 * ============================================================================
 *
 * Deterministic aggregation of tagged observations into the shape the
 * `/writing/analytics` page renders. No I/O, no AI, no hidden state.
 * ============================================================================
 */

import { paletteFor } from '@/lib/writing-criterion-colors';
import type { WritingCriterionKey } from '@/lib/types/expert';
import {
  WRITING_ERROR_TAGS,
  WRITING_ERROR_TAG_LABELS,
  type WeaknessCriterionSummary,
  type WeaknessDataPoint,
  type WeaknessSummary,
  type WeaknessTagSummary,
  type WeaknessTrendBucket,
  type WritingErrorTag,
} from './types';

const KNOWN_TAGS = new Set<string>(WRITING_ERROR_TAGS);

export interface AggregateOptions {
  /** Number of trend-bucket days to keep, ending at `endDate`. Default 14. */
  trendDays?: number;
  /** Right edge of the trend window (inclusive). Default = max occurredAt. */
  endDate?: Date;
  /** Number of top tags to surface. Default 5. */
  topN?: number;
}

function isoDateOnly(d: Date): string {
  // YYYY-MM-DD in UTC (deterministic regardless of caller TZ).
  const yyyy = d.getUTCFullYear();
  const mm = String(d.getUTCMonth() + 1).padStart(2, '0');
  const dd = String(d.getUTCDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

function addDaysUTC(d: Date, n: number): Date {
  return new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate() + n));
}

export function aggregateWeaknesses(
  raw: readonly WeaknessDataPoint[],
  options: AggregateOptions = {},
): WeaknessSummary {
  const trendDays = Math.max(1, options.trendDays ?? 14);
  const topN = Math.max(1, options.topN ?? 5);

  // Filter to known tags so a typo in upstream data never breaks the chart.
  const data = raw.filter((p) => KNOWN_TAGS.has(p.tag));

  if (data.length === 0) {
    return {
      totalObservations: 0,
      topTags: [],
      byCriterion: [],
      trend: buildEmptyTrend(options.endDate ?? new Date(), trendDays),
      firstSeenAt: '',
      lastSeenAt: '',
    };
  }

  // Tag counts.
  const tagCounts = new Map<WritingErrorTag, number>();
  for (const tag of WRITING_ERROR_TAGS) tagCounts.set(tag, 0);
  for (const p of data) {
    const t = p.tag as WritingErrorTag;
    tagCounts.set(t, (tagCounts.get(t) ?? 0) + 1);
  }

  const total = data.length;
  const topTags: WeaknessTagSummary[] = [...tagCounts.entries()]
    .filter(([, c]) => c > 0)
    .map(([tag, count]) => ({
      tag,
      label: WRITING_ERROR_TAG_LABELS[tag],
      count,
      share: count / total,
    }))
    .sort((a, b) => b.count - a.count || a.tag.localeCompare(b.tag))
    .slice(0, topN);

  // Per-criterion breakdown.
  const criterionCounts = new Map<WritingCriterionKey, number>();
  for (const p of data) {
    if (!p.criterion) continue;
    criterionCounts.set(p.criterion, (criterionCounts.get(p.criterion) ?? 0) + 1);
  }
  const totalWithCriterion = [...criterionCounts.values()].reduce((a, b) => a + b, 0);
  const byCriterion: WeaknessCriterionSummary[] = [...criterionCounts.entries()]
    .map(([criterion, count]) => ({
      criterion,
      label: paletteFor(criterion).label,
      count,
      share: totalWithCriterion > 0 ? count / totalWithCriterion : 0,
    }))
    .sort((a, b) => b.count - a.count || a.criterion.localeCompare(b.criterion));

  // Trend window.
  const sorted = [...data].sort(
    (a, b) => new Date(a.occurredAt).getTime() - new Date(b.occurredAt).getTime(),
  );
  const firstSeenAt = sorted[0].occurredAt;
  const lastSeenAt = sorted[sorted.length - 1].occurredAt;
  const endDate = options.endDate ?? new Date(lastSeenAt);

  const buckets = buildEmptyTrend(endDate, trendDays);
  const idxByDate = new Map(buckets.map((b, i) => [b.date, i]));
  for (const p of data) {
    const key = isoDateOnly(new Date(p.occurredAt));
    const i = idxByDate.get(key);
    if (i !== undefined) buckets[i].count += 1;
  }

  return {
    totalObservations: total,
    topTags,
    byCriterion,
    trend: buckets,
    firstSeenAt,
    lastSeenAt,
  };
}

function buildEmptyTrend(endDate: Date, days: number): WeaknessTrendBucket[] {
  const out: WeaknessTrendBucket[] = [];
  // Normalise endDate to UTC midnight to keep buckets stable.
  const end = new Date(Date.UTC(endDate.getUTCFullYear(), endDate.getUTCMonth(), endDate.getUTCDate()));
  for (let i = days - 1; i >= 0; i -= 1) {
    out.push({ date: isoDateOnly(addDaysUTC(end, -i)), count: 0 });
  }
  return out;
}
