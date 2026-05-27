'use client';

/**
 * Writing profile-setup wizard — sessionStorage-backed cross-step state.
 * Lives next to the 4 step pages (profession, goals, focus, confirm).
 * Not a shared component — just a small client-only helper.
 */

import type { WritingLetterType, WritingProfession } from '@/lib/writing/types';

const STORAGE_KEY = 'writing.profile-setup.draft.v1';

export interface WritingProfileWizardState {
  profession: WritingProfession;
  subDiscipline: string;
  yearsExperience: number | null;
  targetBand: 'A' | 'B+' | 'B' | 'C+' | 'C';
  examDate: string | null;
  daysPerWeek: number;
  minutesPerDay: number;
  targetCountry: string;
  letterTypeFocus: WritingLetterType[];
  optInCommunity: boolean;
  optInLeaderboard: boolean;
  optInDataForTraining: boolean;
}

export const DEFAULT_WIZARD_STATE: WritingProfileWizardState = {
  profession: 'medicine',
  subDiscipline: '',
  yearsExperience: null,
  targetBand: 'B',
  examDate: null,
  daysPerWeek: 5,
  minutesPerDay: 45,
  targetCountry: 'GB',
  letterTypeFocus: ['LT-RR', 'LT-DG', 'LT-UR'],
  optInCommunity: false,
  optInLeaderboard: false,
  optInDataForTraining: false,
};

export function readWizardState(): WritingProfileWizardState {
  if (typeof window === 'undefined') return DEFAULT_WIZARD_STATE;
  try {
    const raw = window.sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return DEFAULT_WIZARD_STATE;
    const parsed = JSON.parse(raw) as Partial<WritingProfileWizardState>;
    return { ...DEFAULT_WIZARD_STATE, ...parsed };
  } catch {
    return DEFAULT_WIZARD_STATE;
  }
}

export function writeWizardState(patch: Partial<WritingProfileWizardState>): WritingProfileWizardState {
  const current = readWizardState();
  const next: WritingProfileWizardState = { ...current, ...patch };
  if (typeof window !== 'undefined') {
    try {
      window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(next));
    } catch {
      // sessionStorage may be unavailable (private mode, quota); fall through.
    }
  }
  return next;
}

export function clearWizardState(): void {
  if (typeof window === 'undefined') return;
  try {
    window.sessionStorage.removeItem(STORAGE_KEY);
  } catch {
    /* swallow */
  }
}

export const WIZARD_STEPS = [
  { code: 'profession', label: 'Profession', index: 1 },
  { code: 'goals', label: 'Goals', index: 2 },
  { code: 'focus', label: 'Focus', index: 3 },
  { code: 'confirm', label: 'Confirm', index: 4 },
] as const;
