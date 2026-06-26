// Authored preview — Accordion. Each named export = one labeled card cell.
import { Accordion } from 'oet-prep';

const faqItems = [
  {
    id: 'speaking-marking',
    title: 'How is the Speaking sub-test marked?',
    content:
      'Speaking is assessed on intelligibility, fluency, appropriateness of language, and resources of grammar and expression, plus the clinical communication criteria. In AI mode your role-plays are scored automatically; in live-tutor mode a qualified tutor marks them.',
    defaultOpen: true,
  },
  {
    id: 'grade-scale',
    title: 'What is the OET grade scale?',
    content:
      'Each sub-test is reported on a scale from 0 to 500, mapped to grades A to E. Most registration boards require at least a B (350) in each of the four sub-tests.',
  },
  {
    id: 'results-time',
    title: 'When will I get my results?',
    content:
      'Practice results for Reading and Listening are available instantly. AI-marked Speaking returns within a few minutes, while human-marked Writing is typically reviewed within 24 hours.',
  },
  {
    id: 'retake',
    title: 'Can I retake a mock exam?',
    content:
      'Yes. You can resit any mock paper as often as your plan allows. Each attempt is saved separately so you can track your progress over time.',
  },
];

export const SpeakingFaq = () => (
  <div style={{ maxWidth: 640 }}>
    <Accordion items={faqItems} />
  </div>
);

export const AllowMultiple = () => (
  <div style={{ maxWidth: 640 }}>
    <Accordion
      allowMultiple
      items={[
        {
          id: 'mic',
          title: 'My microphone isn’t being detected',
          content:
            'Grant microphone permission in your browser settings, then reload the page before starting the Speaking test.',
          defaultOpen: true,
        },
        {
          id: 'audio',
          title: 'The Listening audio won’t play',
          content:
            'Check that your device isn’t muted and that no other app is using your audio output. Try refreshing if the player fails to load.',
          defaultOpen: true,
        },
        {
          id: 'autosave',
          title: 'Are my answers saved automatically?',
          content:
            'Yes — your responses are saved as you go, so you can safely resume an in-progress section if you get disconnected.',
        },
      ]}
    />
  </div>
);
