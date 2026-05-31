import type { TourDefinition } from '../tour-types';

/**
 * Expert (reviewer) console tour. Triggers on first entry to /expert. Complements
 * the existing expert setup wizard by orienting reviewers to the queue and marking
 * workflow. Anchors the review queue; the rest are concept steps.
 */
export const expertTour: TourDefinition = {
  id: 'expert',
  role: 'expert',
  title: 'Reviewer console tour',
  description: 'Your queue, the marking workspace, rubric scoring, and releasing feedback.',
  completionKey: 'expert',
  triggerRoute: '/expert',
  steps: [
    {
      title: 'Welcome to the reviewer console',
      body: 'Your assigned learners, the review queue, and the marking tools all live here.',
    },
    {
      target: 'workspace-nav',
      title: 'Open your Review Queue',
      body: 'The Review Queue in the navigation is your starting point — it lists pending submissions you can filter by type, profession, priority, and overdue status, then claim to review.',
      side: 'right',
    },
    {
      title: 'Writing review',
      body: 'A writing review opens the marking workspace: read the case notes and response, score the six criteria, add criterion-tagged inline annotations, and check the content checklist.',
    },
    {
      title: 'Speaking review',
      body: 'A speaking review plays the recording alongside the candidate and role-player cards, scores the linguistic and clinical-communication criteria, and supports timestamped voice notes.',
    },
    {
      title: 'Release controls the learner view',
      body: 'Listening and Reading attempts are auto-scored — you can review answers and add targeted practice. Across all sub-tests, feedback reaches the learner only when you release it; drafts stay private until then.',
    },
  ],
};
