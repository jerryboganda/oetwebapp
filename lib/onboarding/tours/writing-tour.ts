import type { TourDefinition } from '../tour-types';

/**
 * Writing module tour. Triggers on first entry to the Writing hub. Verified OET
 * facts: profession-specific, ONE task, 45 minutes (5 reading + 40 writing), a
 * letter built from case notes, reviewed against criteria (the platform uses six —
 * see /feedback-guide) rather than auto-scored.
 */
export const writingTour: TourDefinition = {
  id: 'writing',
  role: 'learner',
  title: 'Writing tour',
  description: 'Case notes to letter, the 5 + 40 minute split, and how feedback works.',
  completionKey: 'writing',
  triggerRoute: '/writing',
  steps: [
    {
      title: 'Writing at a glance',
      body: 'Writing is profession-specific. You complete one task in 45 minutes — 5 minutes to read the case notes and 40 minutes to write your letter.',
      why: 'Your selected profession determines the case notes and task you receive.',
    },
    {
      target: 'writing-hub',
      title: 'Choose a task and how it is reviewed',
      body: 'Start from a recommended task or the practice library. Before launching you can pick computer or paper style and whether an AI assessor or a tutor reviews your letter.',
      side: 'top',
    },
    {
      title: 'Work from the case notes',
      body: 'You write a formal healthcare letter using only the relevant details from the case notes. In strict mock the notes lock for selection during the 5-minute reading window.',
    },
    {
      title: 'Word count & auto-save',
      body: 'Your draft auto-saves as you type and the word count updates live — aim for a complete, well-organised letter rather than a number alone.',
    },
    {
      title: 'Criteria-based feedback',
      body: 'Writing is not auto-scored like multiple choice. It is assessed against criteria by an AI assessor and/or a tutor; released feedback and any model answer appear on this page.',
    },
  ],
};
