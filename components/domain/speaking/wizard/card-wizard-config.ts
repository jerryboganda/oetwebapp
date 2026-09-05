/**
 * Step model + draft-seed for the unified Speaking role-play card wizard.
 *
 * The card is created up-front as a blank Draft (mirroring the mock-bundle
 * wizard's "create draft, then edit each step" pattern), so every wizard step
 * is a partial PATCH against an existing card id. The create endpoint requires
 * a handful of non-empty fields (profession, title, setting, candidate role,
 * emotion, goal, topic — see AdminService.SpeakingRolePlayCards.ValidateCreateRequest),
 * so the seed fills them with clearly-provisional placeholders that the
 * operator completes in the relevant steps. Placeholder values are blanked on
 * hydration via `unseedCardValue` so the inputs render empty.
 */

import type { WizardStepDef } from '@/components/domain/wizard/wizard-config';
import type { CreateRolePlayCardInput } from '@/lib/api/speaking-role-play-cards';

export const CARD_WIZARD_STEPS: WizardStepDef[] = [
  { id: 'classification', label: 'Classify', description: 'Profession, title & type' },
  { id: 'candidate', label: 'Candidate', description: 'Setting & background' },
  { id: 'tasks', label: 'Tasks', description: 'Candidate task bullets' },
  { id: 'interlocutor', label: 'Hidden script', description: 'AI patient persona', optional: true },
  { id: 'scoring', label: 'Scoring', description: 'Criteria & timing' },
  { id: 'review', label: 'Review', description: 'Check & publish' },
];

export function buildCardStepHref(cardId: string, stepId: string): string {
  return `/admin/speaking/cards/${encodeURIComponent(cardId)}/${stepId}`;
}

// Provisional values for the create-required fields the operator completes
// later. Chosen to read as obvious placeholders if ever surfaced in a list.
export const CARD_DRAFT_SEED_TITLE = 'Untitled role-play (draft)';
export const CARD_DRAFT_SEED_SETTING = 'Setting (to complete)';
export const CARD_DRAFT_SEED_ROLE = 'Candidate role (to complete)';

const SEED_VALUES = new Set<string>([
  CARD_DRAFT_SEED_TITLE,
  CARD_DRAFT_SEED_SETTING,
  CARD_DRAFT_SEED_ROLE,
]);

/**
 * Build the provisional draft payload for a brand-new card.
 *
 * `professionId` is a REQUIRED argument on purpose. It used to be hard-coded to
 * `'nursing'` here, which meant an operator authoring (say) Medicine cards who
 * did not revisit step 1 silently filed them under Nursing — the card looked
 * perfectly valid, so nothing downstream ever flagged it. The profession is now
 * chosen up-front, before the draft row exists, so it can never be wrong by
 * default.
 */
export function buildCardDraftSeed(professionId: string): CreateRolePlayCardInput {
  if (!professionId.trim()) {
    throw new Error('A profession must be chosen before a draft card is created.');
  }
  return {
    professionId: professionId.trim(),
    scenarioTitle: CARD_DRAFT_SEED_TITLE,
    setting: CARD_DRAFT_SEED_SETTING,
    candidateRole: CARD_DRAFT_SEED_ROLE,
    background: '',
    patientEmotion: 'neutral',
    communicationGoal: 'Inform',
    clinicalTopic: 'general',
    criteriaFocus: [],
    difficulty: 'core',
  };
}

/** Blank out a seed placeholder so the field hydrates empty for the operator. */
export function unseedCardValue(value: string | null | undefined): string {
  if (!value) return '';
  return SEED_VALUES.has(value) ? '' : value;
}
