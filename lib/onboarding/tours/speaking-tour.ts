import type { TourDefinition } from '../tour-types';

/**
 * Speaking module tour. Triggers on first entry to the Speaking hub. Verified OET
 * facts: profession-specific, two role plays, ~3 minutes preparation and ~5 minutes
 * performance each, a warm-up that is not assessed, candidate role-play cards, and
 * recorded sessions with feedback released after review. The device check is
 * microphone-only (audio practice); live tutor sessions add video.
 */
export const speakingTour: TourDefinition = {
  id: 'speaking',
  role: 'learner',
  title: 'Speaking tour',
  description: 'Two role plays, prep timing, the candidate card, and recording.',
  completionKey: 'speaking',
  triggerRoute: '/speaking',
  steps: [
    {
      title: 'Speaking at a glance',
      body: 'Speaking is profession-specific. It uses two role plays with about 3 minutes to prepare and 5 minutes to perform each. The opening warm-up is not assessed.',
    },
    {
      target: 'speaking-hub',
      title: 'Start a role play',
      body: 'Pick a recommended role play or a drill here. You can choose guided AI practice, a strict exam-style run, or book a live tutor session, depending on what your course supports.',
      side: 'top',
    },
    {
      title: 'Check your microphone first',
      body: 'Run the device check before you begin. Practice sessions are audio-only; a live tutor session adds video.',
    },
    {
      title: 'Your candidate card',
      body: 'You receive a candidate card with the setting, the patient, and your tasks, plus 3 minutes to prepare. The interlocutor works from a separate, hidden card.',
    },
    {
      title: 'Recording & feedback',
      body: 'Your role plays are recorded. Tutor or assessor feedback — on communication as well as language — is released afterward and appears with your past sessions.',
    },
  ],
};
