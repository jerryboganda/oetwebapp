/**
 * Deterministic mock data for the Writing weakness analytics dashboard.
 *
 * The real `/writing/analytics` page renders straight from `aggregateWeaknesses`,
 * so the seed values here only need to be plausible. They are deterministic so
 * snapshot / Vitest / Playwright runs are stable.
 *
 * When the backend `/v1/writing/analytics/weaknesses` endpoint lands, replace
 * `fetchWritingWeaknessData` in `lib/api.ts` to call it; the page does not
 * need to change.
 */

import type { WeaknessDataPoint } from './types';

const REFERENCE_ISO = '2025-01-15T12:00:00Z';
const DAY_MS = 86_400_000;

function pointAt(daysAgo: number, tag: string, criterion?: WeaknessDataPoint['criterion']): WeaknessDataPoint {
  return {
    occurredAt: new Date(new Date(REFERENCE_ISO).getTime() - daysAgo * DAY_MS).toISOString(),
    tag,
    ...(criterion ? { criterion } : {}),
  };
}

export const SAMPLE_WEAKNESS_OBSERVATIONS: WeaknessDataPoint[] = [
  // Today — heavy on missing key content + grammar.
  pointAt(0, 'missing_key_content', 'content'),
  pointAt(0, 'missing_key_content', 'content'),
  pointAt(0, 'grammar_articles', 'language'),
  pointAt(0, 'informal_tone', 'genre'),

  // 1 day ago.
  pointAt(1, 'missing_key_content', 'content'),
  pointAt(1, 'unclear_purpose', 'purpose'),
  pointAt(1, 'grammar_articles', 'language'),

  // 2 days ago.
  pointAt(2, 'irrelevant_content', 'content'),
  pointAt(2, 'poor_paragraphing', 'organization'),

  // 3 days ago.
  pointAt(3, 'abbreviation_issue', 'genre'),
  pointAt(3, 'inaccurate_transfer', 'content'),

  // 5 days ago.
  pointAt(5, 'grammar_articles', 'language'),
  pointAt(5, 'informal_tone', 'genre'),

  // 7 days ago.
  pointAt(7, 'missing_key_content', 'content'),
  pointAt(7, 'unclear_purpose', 'purpose'),

  // 9 days ago.
  pointAt(9, 'poor_paragraphing', 'organization'),

  // 12 days ago.
  pointAt(12, 'grammar_articles', 'language'),
];

/** Right edge for the trend window — keeps Playwright snapshots reproducible. */
export const SAMPLE_WEAKNESS_REFERENCE_DATE = REFERENCE_ISO;
