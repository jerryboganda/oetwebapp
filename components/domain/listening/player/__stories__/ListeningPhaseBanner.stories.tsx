import type { Meta, StoryObj } from '@storybook/react';
import { ListeningPreviewBanner, ListeningReviewBanner } from '../ListeningPhaseBanner';

const meta: Meta = {
  title: 'Listening V2/ListeningPhaseBanner',
  parameters: { layout: 'padded' },
};
export default meta;

type PreviewStory = StoryObj<typeof ListeningPreviewBanner>;
type ReviewStory = StoryObj<typeof ListeningReviewBanner>;

export const PreviewWithSkip: PreviewStory = {
  render: (args) => <ListeningPreviewBanner {...args} />,
  args: {
    section: 'A1',
    secondsRemaining: 25,
    canSkip: true,
    onSkip: () => {},
  },
};

export const PreviewNoSkip: PreviewStory = {
  render: (args) => <ListeningPreviewBanner {...args} />,
  args: {
    section: 'B',
    secondsRemaining: 12,
    canSkip: false,
    onSkip: () => {},
  },
};

export const ReviewMidPaper: ReviewStory = {
  render: (args) => <ListeningReviewBanner {...args} />,
  args: {
    section: 'A2',
    secondsRemaining: 45,
    isLastSection: false,
    onNext: () => {},
  },
};

export const ReviewFinalSection: ReviewStory = {
  render: (args) => <ListeningReviewBanner {...args} />,
  args: {
    section: 'C2',
    secondsRemaining: 60,
    isLastSection: true,
    onNext: () => {},
  },
};
