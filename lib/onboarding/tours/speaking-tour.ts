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
  description: 'Two role plays, prep timing, the candidate card, and how your answers are marked.',
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
      body: 'Open the practice library, try the free sample, or take the full two-card AI Speaking mock here. You can also book a live tutor session, depending on what your course supports.',
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
      title: 'How your answers are saved',
      body: 'With the live AI patient, your conversation is saved as a text transcript and the audio is retained for a limited period. If live voice is unavailable, you record your answer instead and that recording is stored. Tutor or assessor feedback — on communication as well as language — is released afterward and appears with your past sessions.',
    },
  ],
};
