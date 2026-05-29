import type { ReadingReviewState } from '@/lib/reading-authoring-api';
import type { BadgeTone } from '@/components/admin/ui/badge';

export const REVIEW_STATES: readonly ReadingReviewState[] = [
  'Draft',
  'AcademicReview',
  'MedicalReview',
  'LanguageReview',
  'Pilot',
  'Published',
  'Retired',
];

export const REVIEW_STATE_LABELS: Record<ReadingReviewState, string> = {
  Draft: 'Draft',
  AcademicReview: 'Academic Review',
  MedicalReview: 'Medical Review',
  LanguageReview: 'Language Review',
  Pilot: 'Pilot',
  Published: 'Published',
  Retired: 'Retired',
};

export function reviewStateTone(state: ReadingReviewState | undefined): BadgeTone {
  switch (state) {
    case 'Published':
      return 'success';
    case 'Pilot':
      return 'warning';
    case 'Retired':
      return 'danger';
    case 'AcademicReview':
    case 'MedicalReview':
    case 'LanguageReview':
      return 'info';
    case 'Draft':
    default:
      return 'default';
  }
}
