import { analytics, type EventProperties } from '@/lib/analytics';
import type { TourId } from './tour-types';

/**
 * Typed wrappers over the existing analytics layer for onboarding/tour events.
 * The matching event names are declared in lib/analytics.ts TRACKED_EVENTS.
 * Rich step/route detail lives here (client-side); the backend only persists
 * durable completion state, so these events are the source of truth for funnels.
 */

type TourCtx = {
  tourId: TourId | string;
  role?: string;
  module?: string;
  route?: string;
};

export function trackTourStarted(p: TourCtx): void {
  analytics.track('tour_started', p as EventProperties);
}

export function trackTourReplayed(p: TourCtx): void {
  analytics.track('tour_replayed', p as EventProperties);
}

export function trackTourStepViewed(p: TourCtx & { stepId: string; stepIndex: number }): void {
  analytics.track('tour_step_viewed', p as EventProperties);
}

export function trackTourStepCompleted(p: TourCtx & { stepId: string; stepIndex: number }): void {
  analytics.track('tour_step_completed', p as EventProperties);
}

export function trackTourCompleted(p: TourCtx & { stepCount?: number }): void {
  analytics.track('tour_completed', p as EventProperties);
}

export function trackTourSkipped(p: TourCtx & { atStepIndex?: number }): void {
  analytics.track('tour_skipped', p as EventProperties);
}

export function trackTourDismissed(p: TourCtx): void {
  analytics.track('tour_dismissed', p as EventProperties);
}

export function trackChecklistItemCompleted(p: { itemId: string; role?: string }): void {
  analytics.track('checklist_item_completed', p as EventProperties);
}

export function trackHelpCenterOpened(p: { role?: string; route?: string }): void {
  analytics.track('help_center_opened', p as EventProperties);
}

export function trackWelcomeExamModeSet(mode: string): void {
  analytics.track('welcome_exam_mode_set', { mode });
}

export function trackWelcomeConfidenceSet(level: string): void {
  analytics.track('welcome_confidence_set', { level });
}
