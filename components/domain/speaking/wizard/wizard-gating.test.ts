import {
  getCurrentStepId,
  getStepIndex,
  getNextStepId,
  getPrevStepId,
  type WizardStepDef,
} from '@/components/domain/wizard/wizard-config';
import {
  CARD_WIZARD_STEPS,
  CARD_DRAFT_SEED_SETTING,
  CARD_DRAFT_SEED_TITLE,
  unseedCardValue,
} from '@/components/domain/speaking/wizard/card-wizard-config';
import { getCardReadiness } from '@/components/domain/speaking/wizard/use-card-readiness';
import type { RolePlayCardDetail } from '@/lib/api/speaking-role-play-cards';

const STEPS: WizardStepDef[] = [
  { id: 'a', label: 'A' },
  { id: 'b', label: 'B' },
  { id: 'c', label: 'C' },
];

describe('wizard-config step helpers', () => {
  it('resolves the current step from the last path segment', () => {
    expect(getCurrentStepId('/admin/speaking/cards/123/b', STEPS)).toBe('b');
  });

  it('falls back to the first step for unknown/empty paths', () => {
    expect(getCurrentStepId('/admin/speaking/cards/123/zzz', STEPS)).toBe('a');
    expect(getCurrentStepId(null, STEPS)).toBe('a');
  });

  it('computes index, next and prev', () => {
    expect(getStepIndex('b', STEPS)).toBe(1);
    expect(getNextStepId('a', STEPS)).toBe('b');
    expect(getNextStepId('c', STEPS)).toBeNull();
    expect(getPrevStepId('a', STEPS)).toBeNull();
    expect(getPrevStepId('c', STEPS)).toBe('b');
  });
});

describe('card wizard config', () => {
  it('exposes the six-step flow ending in review', () => {
    expect(CARD_WIZARD_STEPS.map((s) => s.id)).toEqual([
      'classification',
      'candidate',
      'tasks',
      'interlocutor',
      'scoring',
      'review',
    ]);
    expect(CARD_WIZARD_STEPS.find((s) => s.id === 'interlocutor')?.optional).toBe(true);
  });

  it('blanks seed placeholders but preserves real values', () => {
    expect(unseedCardValue(CARD_DRAFT_SEED_SETTING)).toBe('');
    expect(unseedCardValue(CARD_DRAFT_SEED_TITLE)).toBe('');
    expect(unseedCardValue('Surgical ward')).toBe('Surgical ward');
    expect(unseedCardValue(null)).toBe('');
  });
});

function makeCard(overrides: Partial<RolePlayCardDetail> = {}): RolePlayCardDetail {
  return {
    cardId: 'rpc-1',
    contentItemId: 'ci-1',
    professionId: 'nursing',
    scenarioTitle: 'Discharge advice',
    setting: 'Surgical ward',
    candidateRole: 'Nurse',
    interlocutorRole: 'Patient',
    patientName: null,
    patientAge: null,
    background: 'Case background text.',
    tasks: ['t1', 't2', 't3'],
    allowedNotes: true,
    prepTimeSeconds: 180,
    rolePlayTimeSeconds: 300,
    patientEmotion: 'worried',
    communicationGoal: 'Reassure',
    clinicalTopic: 'pain',
    difficulty: 'core',
    criteriaFocus: ['fluency'],
    disclaimer: 'x',
    isLiveTutorEligible: false,
    status: 'Draft',
    hasInterlocutorScript: true,
    createdAt: '',
    updatedAt: '',
    publishedAt: null,
    archivedAt: null,
    ...overrides,
  };
}

describe('getCardReadiness', () => {
  it('is publish-ready (hard) only when the interlocutor script exists', () => {
    expect(getCardReadiness(makeCard({ hasInterlocutorScript: true })).hardReady).toBe(true);
    expect(getCardReadiness(makeCard({ hasInterlocutorScript: false })).hardReady).toBe(false);
  });

  it('flags soft rules without blocking publish', () => {
    const card = makeCard({ tasks: ['only-one'], criteriaFocus: [], hasInterlocutorScript: true });
    const r = getCardReadiness(card);
    expect(r.hardReady).toBe(true); // soft failures do not block
    const tasksItem = r.items.find((i) => i.label.includes('3 task'));
    const criteriaItem = r.items.find((i) => i.label.toLowerCase().includes('criterion'));
    expect(tasksItem?.ok).toBe(false);
    expect(tasksItem?.hard).toBe(false);
    expect(criteriaItem?.ok).toBe(false);
  });

  it('treats seed placeholder setting as not filled', () => {
    const r = getCardReadiness(makeCard({ setting: CARD_DRAFT_SEED_SETTING }));
    expect(r.items.find((i) => i.label === 'Setting set')?.ok).toBe(false);
  });
});
