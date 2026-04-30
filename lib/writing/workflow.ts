import {
  WRITING_CRITERION_MAX_SCORES,
  type WritingCriterionCode,
} from '@/lib/scoring';

export type WritingPracticeMode = 'exam' | 'learning';

export const WRITING_READING_WINDOW_SECONDS = 5 * 60;
export const WRITING_WINDOW_SECONDS = 40 * 60;
export const WRITING_TOTAL_SECONDS = WRITING_READING_WINDOW_SECONDS + WRITING_WINDOW_SECONDS;

export const DEFAULT_WRITING_TARGET_WORD_RANGE = {
  min: 180,
  max: 200,
  warningMin: 170,
  warningMax: 220,
} as const;

export const WRITING_LETTER_TYPES = [
  'Referral',
  'Discharge',
  'Transfer',
  'Advice',
  'Update',
] as const;

export type WritingLetterType = (typeof WRITING_LETTER_TYPES)[number];

export type WritingWordCountState = 'empty' | 'under-warning' | 'near-target' | 'target' | 'over-warning';

export interface WritingWordCountStatus {
  state: WritingWordCountState;
  label: string;
  message: string;
  variant: 'muted' | 'info' | 'success' | 'warning';
}

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

export interface WritingTargetWordRange {
  min: number;
  max: number;
  warningMin: number;
  warningMax: number;
}

export function getWritingWordCountStatus(
  wordCount: number,
  range: Partial<WritingTargetWordRange> = DEFAULT_WRITING_TARGET_WORD_RANGE,
): WritingWordCountStatus {
  const min = range.min ?? DEFAULT_WRITING_TARGET_WORD_RANGE.min;
  const max = range.max ?? DEFAULT_WRITING_TARGET_WORD_RANGE.max;
  const warningMin = range.warningMin ?? DEFAULT_WRITING_TARGET_WORD_RANGE.warningMin;
  const warningMax = range.warningMax ?? DEFAULT_WRITING_TARGET_WORD_RANGE.warningMax;

  if (wordCount === 0) {
    return {
      state: 'empty',
      label: 'No body words yet',
      message: `Target body length is ${min}-${max} words. Word count is guidance only, not an automatic fail rule.`,
      variant: 'muted',
    };
  }

  if (wordCount < warningMin) {
    return {
      state: 'under-warning',
      label: 'Likely too short',
      message: `Below ${warningMin} words: check whether key reader actions and relevant case notes are missing.`,
      variant: 'warning',
    };
  }

  if (wordCount > warningMax) {
    return {
      state: 'over-warning',
      label: 'Likely too long',
      message: `Above ${warningMax} words: check for irrelevant notes and tighten conciseness. Do not mark down by count alone.`,
      variant: 'warning',
    };
  }

  if (wordCount >= min && wordCount <= max) {
    return {
      state: 'target',
      label: 'Target range',
      message: `Within the usual ${min}-${max} body-word target. Keep prioritising relevance and accuracy.`,
      variant: 'success',
    };
  }

  return {
    state: 'near-target',
    label: 'Near target',
    message: `Close to the ${min}-${max} body-word target. Word count supports judgment; it is not a standalone score.`,
    variant: 'info',
  };
}
