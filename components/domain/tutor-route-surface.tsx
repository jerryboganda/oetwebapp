'use client';

/**
 * Tutor route surface — thin re-exports over the Expert route components so the
 * tutor console keeps a single visual language with the rest of the
 * expert/admin/learner surfaces. The Expert components are already generic
 * (they wrap LearnerPageHero etc.), but giving the tutor namespace its own
 * names makes future divergence cheap.
 *
 * Wave B1 — see OET_ZOOM_INTEGRATION_PLAN.md §16/§20/§21.
 */

import {
  ExpertRouteFreshnessBadge,
  ExpertRouteHero,
  ExpertRouteSectionHeader,
  ExpertRouteSummaryCard,
  ExpertRouteWorkspace,
} from '@/components/domain/expert-route-surface';

export const TutorRouteWorkspace = ExpertRouteWorkspace;
export const TutorRouteHero = ExpertRouteHero;
export const TutorRouteSectionHeader = ExpertRouteSectionHeader;
export const TutorRouteSummaryCard = ExpertRouteSummaryCard;
export const TutorRouteFreshnessBadge = ExpertRouteFreshnessBadge;
