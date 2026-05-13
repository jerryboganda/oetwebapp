import type { Meta, StoryObj } from '@storybook/react';
import { ListeningSectionStepper } from '../ListeningSectionStepper';
import type { ListeningSectionCode } from '@/lib/listening-sections';

const meta: Meta<typeof ListeningSectionStepper> = {
  title: 'Listening V2/ListeningSectionStepper',
  component: ListeningSectionStepper,
  parameters: { layout: 'padded' },
};
export default meta;

type Story = StoryObj<typeof ListeningSectionStepper>;
const sections: ListeningSectionCode[] = ['A1', 'A2', 'B', 'C1', 'C2'];

export const PartAActive: Story = {
  args: { sections, currentIndex: 0, isReviewing: false },
};

export const PartBActive: Story = {
  args: { sections, currentIndex: 2, isReviewing: false },
};

export const PartBReviewing: Story = {
  args: { sections, currentIndex: 2, isReviewing: true },
};

export const PartCFinal: Story = {
  args: { sections, currentIndex: 4, isReviewing: false },
};
