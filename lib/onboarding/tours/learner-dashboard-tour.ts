import type { TourDefinition } from '../tour-types';

/**
 * Action-first orientation of the learner dashboard. Copy is exam-aware and
 * concise (one concept per step). Anchors are `data-tour` attributes added to
 * app/page.tsx and the learner shell. Steps whose anchor is absent are skipped.
 */
export const learnerDashboardTour: TourDefinition = {
  id: 'learner-dashboard',
  role: 'learner',
  title: 'Dashboard tour',
  description: 'Where to start, what to practise next, and how progress is tracked.',
  completionKey: 'dashboard',
  triggerRoute: '/dashboard',
  steps: [
    {
      title: 'Welcome to your OET workspace',
      body: 'This dashboard shows what to practise next based on your profession, your progress, and your target score. The quick tour points out where to start.',
    },
    {
      target: 'learner-dashboard-next-action',
      title: 'Your next best action',
      body: 'This card shows the single best thing to do next from your live study plan. If you are unsure where to begin, start here.',
      side: 'bottom',
      align: 'start',
    },
    {
      target: 'learner-dashboard-skills',
      title: 'The four sub-tests',
      body: 'Practise Listening, Reading, Writing, and Speaking here. Listening and Reading are common to every profession; Writing and Speaking use the profession you selected.',
      why: 'Writing and Speaking tasks are profession-specific in OET, so they adapt to your field.',
      side: 'bottom',
      align: 'start',
    },
    {
      target: 'learner-dashboard-today',
      title: "Today's study plan",
      body: 'Your plan lists today’s tasks. Completing them keeps your readiness estimate accurate and paces you toward your exam date.',
      side: 'top',
      align: 'start',
    },
    {
      target: 'learner-dashboard-readiness',
      title: 'Track your readiness',
      body: 'See how ready you are in each sub-test. OET reports every sub-test separately — there is no single overall score — so aim to clear your target in all four.',
      why: 'Most regulators require a minimum grade in each sub-test independently.',
      side: 'left',
      align: 'start',
    },
    {
      target: 'learner-help-launcher',
      title: 'Help is always one click away',
      body: 'Replay this tour, open a module guide, or review how mock and practice modes differ from the Help menu whenever you need it.',
      side: 'bottom',
      align: 'end',
    },
  ],
};
