import type { Meta, StoryObj } from '@storybook/react';
import { ListeningAudioTransport } from '../ListeningAudioTransport';

const meta: Meta<typeof ListeningAudioTransport> = {
  title: 'Listening V2/ListeningAudioTransport',
  component: ListeningAudioTransport,
  parameters: { layout: 'padded' },
};
export default meta;

type Story = StoryObj<typeof ListeningAudioTransport>;

const baseArgs = {
  isPlaying: false,
  progressSeconds: 0,
  durationSeconds: 240,
  canScrub: false,
  isPreviewPhase: false,
  audioState: 'ready' as const,
  saveState: 'idle' as const,
  answeredCount: 0,
  totalQuestions: 42,
  attemptSecondsRemaining: 2400,
  onTogglePlayPause: () => {},
  onScrub: () => {},
};

export const ExamMode: Story = {
  args: { ...baseArgs },
};

export const PracticeWithScrub: Story = {
  args: {
    ...baseArgs,
    canScrub: true,
    isPlaying: true,
    progressSeconds: 120,
    saveState: 'saved',
    answeredCount: 18,
    attemptSecondsRemaining: null,
  },
};

export const Buffering: Story = {
  args: {
    ...baseArgs,
    audioState: 'buffering',
    isPlaying: true,
  },
};

export const PreviewPhase: Story = {
  args: {
    ...baseArgs,
    isPreviewPhase: true,
  },
};

export const SaveError: Story = {
  args: {
    ...baseArgs,
    saveState: 'error',
  },
};
