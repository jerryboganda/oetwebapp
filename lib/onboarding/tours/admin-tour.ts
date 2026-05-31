import type { TourDefinition } from '../tour-types';

/**
 * Admin console orientation. Triggers on first entry to /admin. Scoped to the top
 * operational workflows rather than an exhaustive walk; preserves the admin-hallmark
 * discipline (dense, honest, no marketing tone). Anchors the nav; the rest are
 * concept steps that always show.
 */
export const adminTour: TourDefinition = {
  id: 'admin',
  role: 'admin',
  title: 'Admin console tour',
  description: 'Content building, publishing, assignment, review, and analytics.',
  completionKey: 'admin',
  triggerRoute: '/admin',
  steps: [
    {
      title: 'Welcome to the admin console',
      body: 'From here you create and publish exam content, assign study plans, oversee reviews, and manage people and access.',
    },
    {
      target: 'workspace-nav',
      title: 'Everything is grouped here',
      body: 'The navigation is organised into Content, Learner Plans, Reviews & Quality, People & Access, Billing & Growth, and System. Visibility follows your permissions.',
      side: 'right',
    },
    {
      title: 'Build & publish content',
      body: 'Author Listening, Reading, Writing, and Speaking content. The mock wizard assembles a full bundle step by step, and publish gates keep unfinished content away from learners.',
    },
    {
      title: 'Rubrics, criteria & review ops',
      body: 'Maintain rubrics and criteria, run calibration to keep marking aligned, and manage the review queue under Reviews & Quality.',
    },
    {
      title: 'People, plans & analytics',
      body: 'Manage learners, tutors, roles and permissions under People & Access, assign study-plan templates, and track content effectiveness and quality under Analytics.',
    },
  ],
};
