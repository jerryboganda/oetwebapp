import type { TourDefinition, TourId, TourRole } from './tour-types';
import { learnerDashboardTour } from './tours/learner-dashboard-tour';
import { listeningTour } from './tours/listening-tour';
import { readingTour } from './tours/reading-tour';
import { writingTour } from './tours/writing-tour';
import { speakingTour } from './tours/speaking-tour';
import { adminTour } from './tours/admin-tour';
import { expertTour } from './tours/expert-tour';
import { tutorTour } from './tours/tutor-tour';

/**
 * Central registry of every guided tour. Tours are appended here as each surface
 * gains its `data-tour` anchors. A tour whose anchors are not yet on the page is
 * harmless — runTour() skips steps whose target element is absent.
 */
const TOURS: TourDefinition[] = [
  learnerDashboardTour,
  listeningTour,
  readingTour,
  writingTour,
  speakingTour,
  adminTour,
  expertTour,
  tutorTour,
];

const BY_ID = new Map<TourId, TourDefinition>(TOURS.map((tour) => [tour.id, tour]));

export function getTour(id: TourId): TourDefinition | undefined {
  return BY_ID.get(id);
}

export function allTours(): TourDefinition[] {
  return TOURS;
}

export function toursForRole(role: TourRole): TourDefinition[] {
  // 'tutor' is a UI surface of the 'expert' API role; surface both expert + tutor tours to experts.
  return TOURS.filter((tour) =>
    tour.role === role || (role === 'expert' && tour.role === 'tutor'),
  );
}
