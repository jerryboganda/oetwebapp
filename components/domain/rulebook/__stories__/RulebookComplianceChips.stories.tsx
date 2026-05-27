import type { Meta, StoryObj } from '@storybook/react';
import { RulebookComplianceChips, computeChipStatus, type RulebookComplianceChip } from '../RulebookComplianceChips';

const meta: Meta<typeof RulebookComplianceChips> = {
  title: 'Rulebook/RulebookComplianceChips',
  component: RulebookComplianceChips,
  parameters: { layout: 'padded' },
};
export default meta;
type Story = StoryObj<typeof RulebookComplianceChips>;

function mk(overrides: Partial<RulebookComplianceChip>): RulebookComplianceChip {
  const base: RulebookComplianceChip = {
    kind: 'writing',
    profession: 'medicine',
    status: 'green',
    totalRules: 78,
    criticalRules: 26,
    unenforcedCount: 0,
    aiGroundedCount: 0,
    humanReviewCount: 0,
  };
  const merged = { ...base, ...overrides };
  merged.status = computeChipStatus(merged);
  return merged;
}

export const HealthyDashboard: Story = {
  args: {
    chips: [
      mk({ kind: 'writing', profession: 'medicine' }),
      mk({ kind: 'writing', profession: 'nursing' }),
      mk({ kind: 'speaking', profession: 'medicine', aiGroundedCount: 1, note: 'RULE_40 tone is AI-grounded.' }),
      mk({ kind: 'speaking', profession: 'dentistry', aiGroundedCount: 1 }),
      mk({ kind: 'listening-exam-mode', profession: 'medicine' }),
      mk({ kind: 'reading-exam-mode', profession: 'medicine' }),
      mk({ kind: 'grammar', profession: 'medicine' }),
      mk({ kind: 'vocabulary', profession: 'medicine' }),
      mk({ kind: 'pronunciation', profession: 'medicine' }),
      mk({ kind: 'conversation', profession: 'medicine' }),
      mk({ kind: 'remediation', profession: 'medicine' }),
    ],
  },
};

export const PartialGaps: Story = {
  args: {
    chips: [
      mk({ kind: 'writing', profession: 'medicine' }),
      mk({ kind: 'writing', profession: 'nursing', unenforcedCount: 1, note: 'One critical rule missing enforcer — investigate.' }),
      mk({ kind: 'speaking', profession: 'medicine', aiGroundedCount: 1 }),
      mk({ kind: 'speaking', profession: 'pharmacy', humanReviewCount: 1 }),
      mk({ kind: 'listening-exam-mode', profession: 'medicine', aiGroundedCount: 2 }),
      mk({ kind: 'reading-exam-mode', profession: 'medicine', humanReviewCount: 2 }),
    ],
  },
};

export const EmptyState: Story = {
  args: { chips: [] },
};
