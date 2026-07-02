/**
 * Step model + draft-seed for the Video Library wizard.
 *
 * Mirrors the Speaking card wizard (`card-wizard-config.ts`): the video is
 * created up-front as a blank Draft so every wizard step is a partial PATCH
 * against an existing video id. The create endpoint only requires a title, so
 * the seed fills it with a clearly-provisional placeholder that the operator
 * replaces in the Details step. The placeholder is blanked on hydration via
 * `unseedVideoValue` so the input renders empty.
 */

import type { WizardStepDef } from '@/components/domain/wizard/wizard-config';

export const VIDEO_WIZARD_STEPS: WizardStepDef[] = [
  { id: 'details', label: 'Details', description: 'Title, category & tags' },
  { id: 'video', label: 'Video', description: 'Upload & encoding' },
  { id: 'extras', label: 'Extras', description: 'Thumbnail, captions, chapters & PDFs', optional: true },
  { id: 'access', label: 'Access', description: 'Tier, professions & scheduling' },
  { id: 'review', label: 'Review', description: 'Check & publish' },
];

export function buildVideoStepHref(videoId: string, stepId: string): string {
  return `/admin/content/videos/${encodeURIComponent(videoId)}/${stepId}`;
}

// Provisional title for the create-required field the operator completes in
// the Details step. Chosen to read as an obvious placeholder in any list.
export const VIDEO_DRAFT_SEED_TITLE = 'Untitled video (draft)';

const SEED_VALUES = new Set<string>([VIDEO_DRAFT_SEED_TITLE]);

/** Blank out a seed placeholder so the field hydrates empty for the operator. */
export function unseedVideoValue(value: string | null | undefined): string {
  if (!value) return '';
  return SEED_VALUES.has(value) ? '' : value;
}

/** Subtest tags a video can be filed under (admin UI is English-only). */
export const VIDEO_SUBTEST_OPTIONS: { value: string; label: string }[] = [
  { value: 'listening', label: 'Listening' },
  { value: 'reading', label: 'Reading' },
  { value: 'writing', label: 'Writing' },
  { value: 'speaking', label: 'Speaking' },
  { value: 'general', label: 'General' },
];

export const VIDEO_DIFFICULTY_OPTIONS: { value: string; label: string }[] = [
  { value: 'foundation', label: 'Foundation' },
  { value: 'core', label: 'Core' },
  { value: 'advanced', label: 'Advanced' },
];
