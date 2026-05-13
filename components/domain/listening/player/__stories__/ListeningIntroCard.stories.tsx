import type { Meta, StoryObj } from '@storybook/react';
import { ListeningIntroCard } from '../ListeningIntroCard';
import { listeningSessionFixture } from './fixture';

const meta: Meta<typeof ListeningIntroCard> = {
  title: 'Listening V2/ListeningIntroCard',
  component: ListeningIntroCard,
  parameters: { layout: 'padded' },
};
export default meta;

type Story = StoryObj<typeof ListeningIntroCard>;

const baseArgs = {
  session: listeningSessionFixture,
  isExam: true,
  drillId: null,
  strictReadinessRequired: true,
  techReadiness: null,
  isStarting: false,
  audioError: null,
  startError: null,
  onTechReadinessReady: () => {},
  onStart: () => {},
};

export const ExamReady: Story = {
  args: { ...baseArgs },
};

export const PracticeMode: Story = {
  args: {
    ...baseArgs,
    isExam: false,
    strictReadinessRequired: false,
    session: {
      ...listeningSessionFixture,
      modePolicy: {
        ...listeningSessionFixture.modePolicy,
        mode: 'practice',
        canPause: true,
        canScrub: true,
        onePlayOnly: false,
        integrityLockRequired: false,
      },
    },
  },
};

export const Starting: Story = {
  args: {
    ...baseArgs,
    isStarting: true,
    techReadiness: { audioOk: true, durationMs: 1200 },
  },
};

export const AudioError: Story = {
  args: {
    ...baseArgs,
    audioError: 'Audio probe failed. Check your speakers and try again.',
  },
};
