import {
  WRITING_CRITERION_MAX_SCORES,
  type WritingCriterionCode,
} from '@/lib/scoring';
import {
  CANONICAL_LETTER_TYPES,
  type CanonicalLetterType,
} from './letter-types';

export type WritingPracticeMode = 'exam' | 'learning';

export const WRITING_READING_WINDOW_SECONDS = 5 * 60;
export const WRITING_WINDOW_SECONDS = 40 * 60;
export const WRITING_TOTAL_SECONDS = WRITING_READING_WINDOW_SECONDS + WRITING_WINDOW_SECONDS;

/**
 * The six canonical OET Writing letter types. This is now a re-export of the
 * single source of truth in `./letter-types.ts` — values are the on-the-wire
 * codes that match the backend `WritingContentStructure.LetterType` field.
 *
 * Previously this constant held display labels (`'Referral'`, `'Discharge'`,
 * `'Transfer'`, `'Advice'`, `'Update'`) which did not match the canonical
 * backend vocabulary; that drift caused rule-applicability filters to miss.
 * Use `LETTER_TYPE_DISPLAY_LABELS` from `./letter-types` for human-facing copy.
 */
export const WRITING_LETTER_TYPES = CANONICAL_LETTER_TYPES;

export type WritingLetterType = CanonicalLetterType;

export interface WritingCriterionDescriptor {
  code: WritingCriterionCode;
  label: string;
  maxScore: number;
  guidance: string;
}

export const WRITING_CRITERIA: readonly WritingCriterionDescriptor[] = [
  {
    code: 'purpose',
    label: 'Purpose',
    maxScore: WRITING_CRITERION_MAX_SCORES.purpose,
    guidance: 'The reason for writing is clear early and expanded appropriately.',
  },
  {
    code: 'content',
    label: 'Content',
    maxScore: WRITING_CRITERION_MAX_SCORES.content,
    guidance: 'Necessary case-note information is selected, accurate, and reader-aware.',
  },
  {
    code: 'conciseness_clarity',
    label: 'Conciseness & Clarity',
    maxScore: WRITING_CRITERION_MAX_SCORES.conciseness_clarity,
    guidance: 'Irrelevant detail is removed and the clinical story is summarised clearly.',
  },
  {
    code: 'genre_style',
    label: 'Genre & Style',
    maxScore: WRITING_CRITERION_MAX_SCORES.genre_style,
    guidance: 'Tone, register, abbreviations, and document type suit the reader.',
  },
  {
    code: 'organisation_layout',
    label: 'Organisation & Layout',
    maxScore: WRITING_CRITERION_MAX_SCORES.organisation_layout,
    guidance: 'Information is sequenced by reader need and laid out as a professional letter.',
  },
  {
    code: 'language',
    label: 'Language',
    maxScore: WRITING_CRITERION_MAX_SCORES.language,
    guidance: 'Grammar, vocabulary, spelling, and punctuation support safe communication.',
  },
] as const;

export function normalizeWritingPracticeMode(value: string | null | undefined): WritingPracticeMode {
  return value?.toLowerCase() === 'learning' ? 'learning' : 'exam';
}
