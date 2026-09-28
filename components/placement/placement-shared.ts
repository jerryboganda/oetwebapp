import type { PlacementModule } from '@/lib/api/placement';

// ── Parts ────────────────────────────────────────────────────────────

export type PartKey = PlacementModule | 'SPK' | 'WRT';

export interface PartInfo {
  key: PartKey;
  name: string;
  measures: string;
  count: string;
  time: string;
}

export const PARTS: PartInfo[] = [
  { key: 'LS', name: 'Language Systems', measures: 'Grammar and vocabulary', count: 'Approx. 12–18 items', time: '8–12 min' },
  { key: 'RD', name: 'Reading', measures: 'Notices, messages and longer texts', count: 'Approx. 12–18 items', time: '10–15 min' },
  { key: 'LSN', name: 'Listening', measures: 'Conversations, announcements and talks', count: 'Approx. 12–18 items', time: '10–15 min' },
  { key: 'SPK', name: 'Speaking', measures: 'Recorded spoken responses', count: '8 recorded responses', time: '10–15 min' },
  { key: 'WRT', name: 'Writing', measures: 'Short and extended written tasks', count: '3 scored tasks', time: '20–30 min' },
];

export function partIndex(key: PartKey): number {
  return PARTS.findIndex((part) => part.key === key);
}

export const PRIMARY_BUTTON =
  'inline-flex min-h-11 w-full items-center justify-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-sm font-semibold text-white transition hover:opacity-90 disabled:opacity-60 sm:w-auto';
export const SECONDARY_BUTTON =
  'inline-flex min-h-11 w-full items-center justify-center gap-2 rounded-xl border border-border bg-surface px-5 py-2.5 text-sm font-semibold text-navy transition hover:border-primary/60 disabled:opacity-60 sm:w-auto';
