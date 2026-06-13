/**
 * Step model for the Speaking mock-set wizard.
 *
 * Unlike the card wizard, a mock set cannot be created blank: the backend
 * requires a title and two distinct *speaking* role-play content ids up front
 * (AdminService.SpeakingMockSets.ValidateMockSetReferences). So the `new`
 * screen collects those essentials and creates the draft, then these wizard
 * steps edit the rest via partial PUT.
 */

import type { WizardStepDef } from '@/components/domain/wizard/wizard-config';

export const MOCK_SET_WIZARD_STEPS: WizardStepDef[] = [
  { id: 'details', label: 'Details', description: 'Title & metadata' },
  { id: 'role-plays', label: 'Role-plays', description: 'Pick two cards' },
  { id: 'assets', label: 'Exam assets', description: 'Warm-up & criteria', optional: true },
  { id: 'review', label: 'Review', description: 'Check & publish' },
];

export function buildMockSetStepHref(mockSetId: string, stepId: string): string {
  return `/admin/speaking/mock-sets/${encodeURIComponent(mockSetId)}/${stepId}`;
}

export const MOCK_SET_PROFESSION_OPTIONS = [
  { value: 'nursing', label: 'Nursing' },
  { value: 'medicine', label: 'Medicine' },
  { value: 'pharmacy', label: 'Pharmacy' },
  { value: 'physiotherapy', label: 'Physiotherapy' },
];

export const MOCK_SET_DIFFICULTY_OPTIONS = [
  { value: 'core', label: 'Core' },
  { value: 'extension', label: 'Extension' },
  { value: 'exam', label: 'Exam' },
];
