import type { TourDefinition } from '../tour-types';

/**
 * Listening module tour. Triggers on first entry to the Listening hub. Copy is
 * verified against official OET facts: 3 parts (A/B/C), 42 questions, audio played
 * once in strict mock. One stable anchor (the practice/exam chooser) plus centered
 * exam-fact steps so the tour is rich even before deeper anchors are added.
 */
export const listeningTour: TourDefinition = {
  id: 'listening',
  role: 'learner',
  title: 'Listening tour',
  description: 'Parts A–C, strict mock vs practice, and why the audio plays once.',
  completionKey: 'listening',
  triggerRoute: '/listening',
  steps: [
    {
      title: 'Listening at a glance',
      body: 'Listening is common to every profession. It has three parts — A, B and C — with 42 questions in about 40 minutes.',
    },
    {
      target: 'listening-hub',
      title: 'Mock vs practice',
      body: 'Attempt the full exam for a strict mock — the audio plays once and timing is enforced — or practise a single part to learn with replay, transcripts, and explanations.',
      why: 'On test day each recording is played a single time; the mock rehearses that exactly.',
      side: 'top',
    },
    {
      title: 'Run the sound check first',
      body: 'Before any mock, complete the device and audio check so playback works the moment the test starts.',
    },
    {
      title: 'How the parts work',
      body: 'Part A is note completion — type the exact words you hear, so spelling counts. Parts B and C use multiple-choice questions on short workplace and longer healthcare extracts.',
    },
    {
      title: 'Scoring & review',
      body: 'Submit to see your score. Transcripts and answer review unlock after submission in a mock, or anytime in practice mode.',
    },
  ],
};
