/**
 * Writing/Speaking task mappers + shared normalizers — extracted from `lib/api.ts`.
 * Not re-exported to consumers; internal shared layer for ./api/* slices.
 */
import { toStringArray, type ApiRecord } from './client';
import {
  titleCase,
  minutesToLabel,
} from '../domain/format';
import type { WritingTask, SpeakingTask, SubTest } from '../mock-data';

export { titleCase, minutesToLabel };

export function toSubTest(code: string): SubTest {
  switch (code?.toLowerCase()) {
    case 'writing':
      return 'Writing';
    case 'speaking':
      return 'Speaking';
    case 'reading':
      return 'Reading';
    case 'listening':
      return 'Listening';
    default:
      console.warn('[API] Unknown subtest code:', code, '- defaulting to Writing');
      return 'Writing';
  }
}

export function normalizeCriterionName(code: string | null | undefined): string {
  switch ((code ?? '').toLowerCase()) {
    case 'purpose':
      return 'Purpose';
    case 'content':
      return 'Content';
    case 'conciseness':
    case 'conciseness_clarity':
      return 'Conciseness & Clarity';
    case 'genre':
    case 'genre_style':
      return 'Genre & Style';
    case 'organization':
    case 'organisation_layout':
      return 'Organisation & Layout';
    case 'language':
      return 'Language';
    case 'intelligibility':
      return 'Intelligibility';
    case 'fluency':
      return 'Fluency';
    case 'appropriateness':
    case 'appropriateness_of_language':
      return 'Appropriateness of Language';
    case 'grammar':
    case 'grammar_expression':
    case 'resources_of_grammar_and_expression':
      return 'Resources of Grammar & Expression';
    case 'relationshipbuilding':
    case 'relationship_building':
      return 'Relationship Building';
    case 'patientperspective':
    case 'patient_perspective':
      return "Understanding & Incorporating Patient's Perspective";
    case 'providingstructure':
    case 'providing_structure':
      return 'Providing Structure';
    case 'informationgathering':
    case 'information_gathering':
      return 'Information Gathering';
    case 'informationgiving':
    case 'information_giving':
      return 'Information Giving';
    default:
      return titleCase(code ?? 'Criterion');
  }
}

export function mapWritingTask(item: ApiRecord): WritingTask {
  return {
    id: item.contentId,
    title: item.title,
    difficulty: titleCase(item.difficulty) as WritingTask['difficulty'],
    profession: titleCase(item.professionId),
    time: minutesToLabel(item.estimatedDurationMinutes),
    criteriaFocus: Array.isArray(item.criteriaFocus) ? item.criteriaFocus.map(normalizeCriterionName).join(', ') : '',
    scenarioType: titleCase(item.scenarioType),
    caseNotes: item.caseNotes ?? '',
    letterType: titleCase(item.letterType ?? item.taskType ?? item.scenarioType),
    scenario: typeof item.scenario === 'string' ? item.scenario : undefined,
    taskDate: typeof item.taskDate === 'string' ? item.taskDate : typeof item.date === 'string' ? item.date : undefined,
    writerRole: typeof item.writerRole === 'string' ? item.writerRole : undefined,
    recipient: typeof item.recipient === 'string' ? item.recipient : typeof item.recipientName === 'string' ? item.recipientName : undefined,
    purpose: typeof item.purpose === 'string' ? item.purpose : undefined,
    status: typeof item.status === 'string' ? titleCase(item.status) : undefined,
  };
}

export function mapSpeakingTask(item: ApiRecord): SpeakingTask {
  const criteriaFocusTags = toStringArray(item.criteriaFocus ?? item.criteriaFocusTags);
  return {
    id: item.contentId,
    title: item.title,
    scenarioType: titleCase(item.scenarioType),
    difficulty: titleCase(item.difficulty) as SpeakingTask['difficulty'],
    profession: titleCase(item.professionId),
    criteriaFocus: criteriaFocusTags.map(normalizeCriterionName).join(', '),
    duration: minutesToLabel(item.estimatedDurationMinutes),
    prepTimeSeconds: typeof item.prepTimeSeconds === 'number' ? item.prepTimeSeconds : undefined,
    roleplayTimeSeconds: typeof item.roleplayTimeSeconds === 'number' ? item.roleplayTimeSeconds : undefined,
    patientEmotion: typeof item.patientEmotion === 'string' ? item.patientEmotion : undefined,
    communicationGoal: typeof item.communicationGoal === 'string' ? item.communicationGoal : undefined,
    clinicalTopic: typeof item.clinicalTopic === 'string' ? item.clinicalTopic : undefined,
    criteriaFocusTags,
    disclaimer: typeof item.disclaimer === 'string' ? item.disclaimer : undefined,
  };
}
