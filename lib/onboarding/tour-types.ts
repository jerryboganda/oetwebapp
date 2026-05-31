import type { OnboardingTourState } from '@/lib/api';
import type { UserRole } from '@/lib/types/auth';

/**
 * Bump when tour CONTENT changes materially enough that users who already
 * completed a tour should see the refreshed version. The provider compares this
 * to the persisted `lastSeenTourVersion` (see backend LearnerService.OnboardingTourVersion).
 */
export const TOUR_VERSION = 1;

/** Stable tour ids. The learner module tours map 1:1 to backend completion columns. */
export type TourId =
  | 'learner-dashboard'
  | 'listening'
  | 'reading'
  | 'writing'
  | 'speaking'
  | 'admin'
  | 'expert'
  | 'tutor';

export type TourStatus = 'completed' | 'skipped' | 'dismissed';

/** Workspace a tour belongs to. 'tutor' is a UI surface that shares the 'expert' API role. */
export type TourRole = Extract<UserRole, 'learner' | 'expert' | 'admin'> | 'tutor';

/** Backend completion flags (LearnerOnboardingTour booleans / OnboardingTourState.completed). */
export type TourCompletionKey =
  | 'intro'
  | 'dashboard'
  | 'listening'
  | 'reading'
  | 'writing'
  | 'speaking'
  | 'admin'
  | 'expert';

export interface TourStep {
  /**
   * `data-tour` attribute value of the element to spotlight. Omit for a centered,
   * element-less step (intro / outro). If the element is not in the DOM when the
   * tour runs, the step is skipped gracefully.
   */
  target?: string;
  title: string;
  /** One concept per step. Keep it short, professional, exam-aware. Plain text. */
  body: string;
  /** Optional "Why this matters" line rendered beneath the body. */
  why?: string;
  side?: 'top' | 'bottom' | 'left' | 'right' | 'over';
  align?: 'start' | 'center' | 'end';
}

export interface TourDefinition {
  id: TourId;
  role: TourRole;
  /** Label shown in the Help / replay center. */
  title: string;
  /** One-line description for the Help center. */
  description: string;
  /** Persisted completion flag this tour writes when finished. */
  completionKey: TourCompletionKey;
  /** Route prefix where the tour auto-triggers on first visit (exact or startsWith). */
  triggerRoute?: string;
  steps: TourStep[];
}

export type { OnboardingTourState };
