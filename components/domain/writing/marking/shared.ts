/**
 * Shared constants + helpers for the WS-F5 tutor marking surface.
 *
 * Kept framework-light (no JSX) so both the tutor and expert review pages plus
 * every marking sub-component can import the same criterion metadata, severity
 * styling, and score-clamping logic without duplicating the OET rubric ranges.
 *
 * Spec §12/§13/§14.
 */

import type {
  WritingCriteriaScoresDto,
  WritingCriterionCode,
  WritingChecklistVerdict,
  WritingSeverity,
} from '@/lib/writing/types';

export const CRITERION_CODES: WritingCriterionCode[] = ['c1', 'c2', 'c3', 'c4', 'c5', 'c6'];

/** Short + long labels for each OET writing criterion (spec §12). */
export const CRITERION_LABEL: Record<WritingCriterionCode, string> = {
  c1: 'C1 Purpose',
  c2: 'C2 Content',
  c3: 'C3 Conciseness & Clarity',
  c4: 'C4 Genre & Style',
  c5: 'C5 Organisation & Layout',
  c6: 'C6 Language',
};

/** Per-criterion maximum: C1 Purpose 0–3, all others 0–7. */
export const CRITERION_MAX: Record<WritingCriterionCode, number> = {
  c1: 3,
  c2: 7,
  c3: 7,
  c4: 7,
  c5: 7,
  c6: 7,
};

/** Sum of all criterion maxima = the raw-total denominator (3 + 7·5 = 38). */
export const RAW_TOTAL_MAX = CRITERION_CODES.reduce((sum, c) => sum + CRITERION_MAX[c], 0);

/** Quick-insert feedback snippets per criterion (spec §14 — tutor templates). */
export const CRITERION_FEEDBACK_TEMPLATES: Record<WritingCriterionCode, string[]> = {
  c1: [
    'The purpose of the letter is clear from the opening sentence.',
    'State the main reason for writing earlier and more explicitly.',
    'The requested action for the reader is not clearly identified.',
  ],
  c2: [
    'All key clinical information from the case notes is included accurately.',
    'Important content is missing — review the case notes for omitted facts.',
    'Some details are inaccurate or do not match the case notes.',
  ],
  c3: [
    'The letter is appropriately concise for the receiving reader.',
    'Remove irrelevant background detail to improve clarity.',
    'Several sentences are wordy and could be tightened.',
  ],
  c4: [
    'Register and conventions are appropriate for a referral letter.',
    'The salutation/closing does not match the named recipient.',
    'Tone is too informal for professional correspondence.',
  ],
  c5: [
    'Information is logically sequenced and easy to follow.',
    'Paragraphing does not group related information effectively.',
    'The chronology of events is unclear in places.',
  ],
  c6: [
    'Grammar, spelling and punctuation are well controlled.',
    'Recurrent tense errors interfere with readability.',
    'Sentence structure is repetitive; vary connectors and clause patterns.',
  ],
};

/**
 * Severity → presentation. Color is never the *only* signal: each severity
 * carries a glyph + word so the highlight + side-list remain legible for
 * colour-blind users and screen readers (design rules: a11y).
 */
export interface SeverityStyle {
  label: string;
  /** Short uppercase tag e.g. for badges. */
  tag: string;
  /** Glyph rendered alongside the colour. */
  glyph: string;
  /** Inline highlight classes (background + underline). */
  highlightClass: string;
  /** Solid swatch / badge classes. */
  badgeClass: string;
  /** Border accent for list cards. */
  borderClass: string;
}

export const SEVERITY_STYLE: Record<WritingSeverity, SeverityStyle> = {
  high: {
    label: 'High',
    tag: 'HIGH',
    glyph: '▲',
    highlightClass: 'bg-error/15 underline decoration-error decoration-2 decoration-wavy',
    badgeClass: 'bg-error/10 text-error',
    borderClass: 'border-l-error',
  },
  medium: {
    label: 'Medium',
    tag: 'MED',
    glyph: '◆',
    highlightClass: 'bg-warning/20 underline decoration-warning decoration-2 decoration-dashed',
    badgeClass: 'bg-warning/15 text-warning',
    borderClass: 'border-l-warning',
  },
  low: {
    label: 'Low',
    tag: 'LOW',
    glyph: '●',
    highlightClass: 'bg-info/15 underline decoration-info decoration-1 decoration-dotted',
    badgeClass: 'bg-info/10 text-info',
    borderClass: 'border-l-info',
  },
};

export const SEVERITY_ORDER: WritingSeverity[] = ['high', 'medium', 'low'];

/** Criterion choices for the annotation popover (six criteria + general). */
export const ANNOTATION_CRITERION_OPTIONS: { value: WritingCriterionCode | 'general'; label: string }[] = [
  { value: 'general', label: 'General' },
  ...CRITERION_CODES.map((c) => ({ value: c, label: CRITERION_LABEL[c] })),
];

export const CHECKLIST_VERDICTS: { value: WritingChecklistVerdict; label: string; hint: string }[] = [
  { value: 'included', label: 'Included', hint: 'Present and accurate' },
  { value: 'missing', label: 'Missing', hint: 'Required but absent' },
  { value: 'inaccurate', label: 'Inaccurate', hint: 'Present but wrong' },
  { value: 'irrelevant', label: 'Irrelevant', hint: 'Should not appear' },
];

export const VERDICT_BADGE_CLASS: Record<WritingChecklistVerdict, string> = {
  included: 'bg-success/10 text-success',
  missing: 'bg-error/10 text-error',
  inaccurate: 'bg-warning/15 text-warning',
  irrelevant: 'bg-muted/40 text-muted',
};

/** Clamp + round a numeric criterion score into its legal range. */
export function clampCriterionScore(code: WritingCriterionCode, value: number): number {
  if (!Number.isFinite(value)) return 0;
  const rounded = Math.round(value);
  return Math.max(0, Math.min(CRITERION_MAX[code], rounded));
}

/**
 * Parse a free-text input into a clamped score, or null when blank/invalid.
 * Empty string is treated as "no override" (null) rather than 0.
 */
export function parseScoreInput(code: WritingCriterionCode, raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed === '') return null;
  const num = Number(trimmed);
  if (!Number.isFinite(num)) return null;
  return clampCriterionScore(code, num);
}

export type ScoreDraft = Record<WritingCriterionCode, string>;

export const EMPTY_SCORE_DRAFT: ScoreDraft = { c1: '', c2: '', c3: '', c4: '', c5: '', c6: '' };
export const EMPTY_COMMENT_DRAFT: Record<WritingCriterionCode, string> = {
  c1: '', c2: '', c3: '', c4: '', c5: '', c6: '',
};

/** Convert a partial/whole criteria-scores DTO into a string draft for inputs. */
export function scoresToDraft(scores: Partial<WritingCriteriaScoresDto> | null | undefined): ScoreDraft {
  const draft: ScoreDraft = { ...EMPTY_SCORE_DRAFT };
  if (!scores) return draft;
  for (const c of CRITERION_CODES) {
    const v = scores[c];
    if (typeof v === 'number' && Number.isFinite(v)) draft[c] = String(v);
  }
  return draft;
}

/** Collapse a string draft into a numeric partial score override (clamped). */
export function draftToScoreOverride(draft: ScoreDraft): Partial<WritingCriteriaScoresDto> {
  const out: Partial<WritingCriteriaScoresDto> = {};
  for (const c of CRITERION_CODES) {
    const parsed = parseScoreInput(c, draft[c]);
    if (parsed !== null) out[c] = parsed;
  }
  return out;
}

/** Sum a string draft for the live raw total (blank counts as 0). */
export function draftRawTotal(draft: ScoreDraft): number {
  return CRITERION_CODES.reduce((sum, c) => {
    const parsed = parseScoreInput(c, draft[c]);
    return sum + (parsed ?? 0);
  }, 0);
}

/** True when every criterion has a non-blank value. */
export function isDraftComplete(draft: ScoreDraft): boolean {
  return CRITERION_CODES.every((c) => parseScoreInput(c, draft[c]) !== null);
}
