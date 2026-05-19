import type { AdminConversationAiDraftResult } from '@/lib/api';

export function buildConversationCreatePayload(
  draft: AdminConversationAiDraftResult,
  profession: string,
): Record<string, unknown> {
  return {
    title: draft.title,
    taskTypeCode: draft.taskTypeCode,
    professionId: profession,
    scenario: draft.scenario,
    roleDescription: draft.roleDescription,
    patientContext: draft.patientContext,
    expectedOutcomes: draft.expectedOutcomes,
    difficulty: draft.difficulty,
    estimatedDurationSeconds: draft.estimatedDurationSeconds,
    objectivesJson: JSON.stringify(draft.objectives ?? []),
    expectedRedFlagsJson: JSON.stringify(draft.expectedRedFlags ?? []),
    keyVocabularyJson: JSON.stringify(draft.keyVocabulary ?? []),
    status: 'draft',
  };
}
