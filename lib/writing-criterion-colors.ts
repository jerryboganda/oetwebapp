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
