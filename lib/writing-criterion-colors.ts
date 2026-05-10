/**
 * ============================================================================
 * Writing — Criterion colour palette (shared)
 * ============================================================================
 *
 * Single source of truth for the visual treatment of OET Writing criteria
 * across the expert review console (annotation highlights), the learner
 * revision page (improvement banner), and any future analytics surfaces.
 *
 * Tailwind classes are pre-baked so there is no need for safelisting at
 * build time — every class string used here is also referenced literally
 * in JSX so the JIT picks them up.
 * ============================================================================
 */

import type { WritingCriterionKey } from '@/lib/types/expert';

export interface CriterionPalette {
  /** Card / chip background tint. */
  bgClass: string;
  /** Card / chip border tint. */
  borderClass: string;
  /** Foreground text colour for chips / labels. */
  textClass: string;
  /** Visible label suitable for UI. */
  label: string;
  /** Short single-letter code suitable for compact chips. */
  short: string;
}

const PALETTE: Record<WritingCriterionKey, CriterionPalette> = {
  purpose: {
    bgClass: 'bg-rose-50',
    borderClass: 'border-rose-300',
    textClass: 'text-rose-800',
    label: 'Purpose',
    short: 'P',
  },
  content: {
    bgClass: 'bg-amber-50',
    borderClass: 'border-amber-300',
    textClass: 'text-amber-800',
    label: 'Content',
    short: 'C',
  },
  conciseness: {
    bgClass: 'bg-emerald-50',
    borderClass: 'border-emerald-300',
    textClass: 'text-emerald-800',
    label: 'Conciseness',
    short: 'Co',
  },
  genre: {
    bgClass: 'bg-violet-50',
    borderClass: 'border-violet-300',
    textClass: 'text-violet-800',
    label: 'Genre & Style',
    short: 'G',
  },
  organization: {
    bgClass: 'bg-blue-50',
    borderClass: 'border-blue-300',
    textClass: 'text-blue-800',
    label: 'Organisation',
    short: 'O',
  },
  language: {
    bgClass: 'bg-slate-100',
    borderClass: 'border-slate-300',
    textClass: 'text-slate-800',
    label: 'Language',
    short: 'L',
  },
};

const DEFAULT_PALETTE: CriterionPalette = {
  bgClass: 'bg-yellow-50',
  borderClass: 'border-yellow-200',
  textClass: 'text-yellow-900',
  label: 'General',
  short: '•',
};

export function paletteFor(criterion?: WritingCriterionKey | string | null): CriterionPalette {
  if (!criterion) return DEFAULT_PALETTE;
  return PALETTE[criterion as WritingCriterionKey] ?? DEFAULT_PALETTE;
}

/** Ordered list of criteria — used for picker UIs. */
export const WRITING_CRITERIA_ORDER: WritingCriterionKey[] = [
  'purpose',
  'content',
  'conciseness',
  'genre',
  'organization',
  'language',
];

/**
 * Per-criterion max raw scores for OET Writing.
 *
 * Purpose is on the 0–3 scale (rulebook R16.2); every other criterion is on
 * the 0–7 scale (rulebook R16.1). Mirrors the canonical map in
 * `lib/scoring.ts#WRITING_CRITERION_MAX_SCORES` but keyed by the UI-side
 * `WritingCriterionKey` ('conciseness', 'genre', 'organization') so the
 * feedback surface can render `n/3` vs `n/7` without collapsing the two
 * vocabularies into the same file.
 */
export const WRITING_CRITERION_MAX_SCORE: Readonly<Record<WritingCriterionKey, number>> = {
  purpose: 3,
  content: 7,
  conciseness: 7,
  genre: 7,
  organization: 7,
  language: 7,
};

/**
 * Maximum raw score for a single Writing criterion. Accepts either the
 * UI-facing `WritingCriterionKey` or a backend criterion code/label so
 * callers don't have to normalise first.
 *
 * Falls back to 7 (the dominant max) for unknown labels — every criterion
 * except Purpose uses 7, so this is the safest default.
 */
export function criterionMaxScore(criterion?: string | null): number {
  if (!criterion) return 7;
  const normalized = criterion.trim().toLowerCase();
  if (normalized === 'purpose') return 3;
  // Map common backend / display synonyms back to the UI key.
  if (normalized === 'conciseness_clarity' || normalized.startsWith('conciseness')) return 7;
  if (normalized === 'genre_style' || normalized.startsWith('genre')) return 7;
  if (normalized === 'organisation_layout' || normalized.startsWith('organisation') || normalized.startsWith('organization')) return 7;
  return WRITING_CRITERION_MAX_SCORE[normalized as WritingCriterionKey] ?? 7;
}
