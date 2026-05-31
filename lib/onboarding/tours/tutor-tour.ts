import type { TourDefinition } from '../tour-types';

/**
 * Tutor console tour. Triggers on first entry to /tutor. "Tutor" is a UI surface of
 * the expert API role with a classes + writing-review focus, so this tour completes
 * the shared `expert` flag. Anchors the writing queue; the rest are concept steps.
 */
export const tutorTour: TourDefinition = {
  id: 'tutor',
  role: 'tutor',
  title: 'Tutor console tour',
  description: 'Your classes, the writing review queue, and calibration.',
  completionKey: 'expert',
  triggerRoute: '/tutor',
  steps: [
    {
      title: 'Welcome to your tutor workspace',
      body: 'Your classes and your writing review queue live here. (Tutor is the teaching surface of the reviewer role.)',
    },
    {
      target: 'workspace-nav',
      title: 'Open your Writing Queue',
      body: 'Your Writing Queue is in the navigation — open it to see submissions awaiting review, then mark them in the writing workspace.',
      side: 'right',
    },
    {
      title: 'Mark against the six criteria',
      body: 'Score the six OET writing criteria, annotate the response inline, and release feedback to the learner when it is ready.',
    },
    {
      title: 'Stay calibrated',
      body: 'Writing calibration cases keep your marking aligned with the standard so learners get consistent, fair feedback.',
    },
    {
      title: 'Classes & availability',
      body: 'Manage your classes, availability, and earnings from the navigation whenever you need them.',
    },
  ],
};
