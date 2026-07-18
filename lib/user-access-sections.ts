/**
 * Shared section styling for the admin per-user allocation pickers (Materials +
 * Videos). Mirrors the learner /materials redesign palette (see
 * components/domain/materials/materials-browser.tsx) so the admin allocator and
 * the learner-facing browser read as one system:
 *   Listening = blue, Reading = emerald, Writing = amber, Speaking = purple,
 *   General English = slate, anything else = brand primary.
 */

import { BookOpen, Folder, GraduationCap, Headphones, Mic, PenLine, type LucideIcon } from 'lucide-react';

export type SectionKey = 'listening' | 'reading' | 'writing' | 'speaking' | 'general-english' | 'other';

export interface SectionSkin {
  key: SectionKey;
  label: string;
  Icon: LucideIcon;
  /** gradient tile + icon colour */
  tile: string;
  /** solid colour spine / accent bar */
  bar: string;
  /** hover border colour */
  ring: string;
  /** hover background wash */
  glow: string;
  /** small text-badge colours */
  chip: string;
}

export const SECTION_SKINS: Record<SectionKey, SectionSkin> = {
  listening: {
    key: 'listening',
    label: 'Listening',
    Icon: Headphones,
    tile: 'from-blue-500/20 to-blue-500/5 text-blue-600 dark:text-blue-300',
    bar: 'bg-blue-500',
    ring: 'hover:border-blue-400/60',
    glow: 'hover:bg-blue-500/[0.04]',
    chip: 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300',
  },
  reading: {
    key: 'reading',
    label: 'Reading',
    Icon: BookOpen,
    tile: 'from-emerald-500/20 to-emerald-500/5 text-emerald-600 dark:text-emerald-300',
    bar: 'bg-emerald-500',
    ring: 'hover:border-emerald-400/60',
    glow: 'hover:bg-emerald-500/[0.04]',
    chip: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300',
  },
  writing: {
    key: 'writing',
    label: 'Writing',
    Icon: PenLine,
    tile: 'from-amber-500/20 to-amber-500/5 text-amber-600 dark:text-amber-300',
    bar: 'bg-amber-500',
    ring: 'hover:border-amber-400/60',
    glow: 'hover:bg-amber-500/[0.04]',
    chip: 'bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300',
  },
  speaking: {
    key: 'speaking',
    label: 'Speaking',
    Icon: Mic,
    tile: 'from-purple-500/20 to-purple-500/5 text-purple-600 dark:text-purple-300',
    bar: 'bg-purple-500',
    ring: 'hover:border-purple-400/60',
    glow: 'hover:bg-purple-500/[0.04]',
    chip: 'bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300',
  },
  'general-english': {
    key: 'general-english',
    label: 'General English',
    Icon: GraduationCap,
    tile: 'from-slate-500/20 to-slate-500/5 text-slate-600 dark:text-slate-300',
    bar: 'bg-slate-500',
    ring: 'hover:border-slate-400/60',
    glow: 'hover:bg-slate-500/[0.04]',
    chip: 'bg-slate-100 text-slate-700 dark:bg-slate-800/60 dark:text-slate-300',
  },
  other: {
    key: 'other',
    label: 'Other',
    Icon: Folder,
    tile: 'from-primary/20 to-primary/5 text-primary',
    bar: 'bg-primary',
    ring: 'hover:border-primary/40',
    glow: 'hover:bg-primary/[0.03]',
    chip: 'bg-primary/10 text-primary',
  },
};

/** Resolve a section skin from an OET subtest code (video SubtestCode). */
export function skinForSubtest(subtestCode: string | null | undefined): SectionSkin {
  const code = subtestCode?.trim().toLowerCase();
  if (code === 'listening' || code === 'reading' || code === 'writing' || code === 'speaking') {
    return SECTION_SKINS[code];
  }
  return SECTION_SKINS.other;
}

/** Resolve a section skin from a free-text name (Materials folder name). */
export function skinForName(name: string): SectionSkin {
  const n = name.toLowerCase();
  if (n.includes('listening')) return SECTION_SKINS.listening;
  if (n.includes('reading')) return SECTION_SKINS.reading;
  if (n.includes('writing')) return SECTION_SKINS.writing;
  if (n.includes('speaking')) return SECTION_SKINS.speaking;
  if (n.includes('general english') || n.includes('basic english')) return SECTION_SKINS['general-english'];
  return SECTION_SKINS.other;
}

/** Stable display order for section groups in the pickers. */
export const SECTION_ORDER: SectionKey[] = [
  'listening',
  'reading',
  'writing',
  'speaking',
  'general-english',
  'other',
];

/** Language axis label (English tab/badge stays in Latin per the video-label rule). */
export function languageLabel(language: string | null | undefined): string {
  const l = language?.trim().toLowerCase();
  if (l === 'en') return 'English';
  if (l === 'ar') return 'Arabic';
  return 'Unspecified';
}
