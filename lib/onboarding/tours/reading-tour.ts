import type { TourDefinition } from '../tour-types';

/**
 * Reading module tour. Triggers on first entry to the Reading hub. Verified OET
 * facts: Part A = strict 15-minute block (then locked), Parts B + C share a
 * separate 45-minute block, 42 questions total. Deliberately avoids implying a
 * single free 60-minute timer.
 */
export const readingTour: TourDefinition = {
  id: 'reading',
  role: 'learner',
  title: 'Reading tour',
  description: 'The strict Part A timer, the B/C block, and how answers are graded.',
  completionKey: 'reading',
  triggerRoute: '/reading',
  steps: [
    {
      title: 'Reading at a glance',
      body: 'Reading is common to every profession and has three parts — A, B and C — with 42 questions across two separately timed blocks.',
    },
    {
      target: 'reading-hub',
      title: 'Two timed blocks, not one',
      body: 'Part A runs for a strict 15 minutes. Parts B and C then share a separate 45-minute block. Choose a full mock to rehearse this, or practise a part on its own first.',
      why: 'Treating Reading as one free 60-minute timer is the most common pacing mistake.',
      side: 'top',
    },
    {
      title: 'Part A locks after 15 minutes',
      body: 'Part A is expeditious reading across four short texts. In strict mock it cannot be reopened once the 15 minutes are up — complete every Part A answer before the timer ends.',
    },
    {
      title: 'Answer types',
      body: 'Part A uses typed answers, so spelling matters. Parts B and C use multiple choice on workplace and academic healthcare texts.',
    },
    {
      title: 'Results & review',
      body: 'After you submit, the result breakdown shows how you did in each part so you can target the next practice.',
    },
  ],
};
