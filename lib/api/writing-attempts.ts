import { ensureAttempt } from './attempt-cache';
import { ApiError, apiRequest, toStringArray, type ApiRecord } from './client';
import { minutesToLabel, normalizeCriterionName, titleCase } from './task-mappers';
import { parseCriterionScore, scoreToGrade } from './result-mappers';
import { WRITING_CRITERION_MAX_SCORES, type WritingCriterionCode } from '../scoring';
import { type CriterionFeedback, SpeakingTask, WritingTask } from '../mock-data';

/**
 * Writing attempt session + ensureWritingAttempt + evaluation-id helpers.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export interface WritingAttemptSession {
  attemptId: string;
  contentId: string;
  context: string;
  mode: string;
  state: string;
  startedAt: string;
  draftVersion: number;
  draftContent: string;
  feedbackMessage?: string | null;
}

export type WritingAttemptMode = 'exam' | 'learning' | 'diagnostic';

export async function ensureWritingAttempt(taskId: string, mode: WritingAttemptMode = 'exam'): Promise<WritingAttemptSession> {
  const attempt = await ensureAttempt('writing', taskId, mode);
  const startedAt = typeof attempt.startedAt === 'string' ? attempt.startedAt : '';
  if (!startedAt || Number.isNaN(Date.parse(startedAt))) {
    throw new Error('Writing attempt start time was missing from the API response.');
  }

  return {
    attemptId: String(attempt.attemptId ?? ''),
    contentId: String(attempt.contentId ?? taskId),
    context: String(attempt.context ?? (mode === 'diagnostic' ? 'diagnostic' : mode === 'exam' ? 'exam' : 'practice')),
    mode: String(attempt.mode ?? mode),
    state: String(attempt.state ?? 'in_progress'),
    startedAt,
    draftVersion: Number(attempt.draftVersion ?? 1),
    feedbackMessage: typeof attempt.feedbackMessage === 'string' ? attempt.feedbackMessage : null,
    draftContent: typeof attempt.draftContent === 'string' ? attempt.draftContent : '',
  };
}



function mapWritingTask(item: ApiRecord): WritingTask {
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

function mapSpeakingTask(item: ApiRecord): SpeakingTask {
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

function readingErrorType(question: ApiRecord): string {
  return question.type === 'mcq' ? 'Inference' : 'Detail Extraction';
}


function toSpeakingEvaluationRouteId(value: string): string | null {
  if (!value) return null;
  if (value.startsWith('se-')) return value;
  if (value.startsWith('sa-')) return `se-${value.slice(3)}`;
  return null;
}

function rewriteLegacyLearnerRoute(pathname: string, search: string, hash: string): string {
  if (pathname === '/dashboard') return `/${search}${hash}`.replace(/\/\?/, '/?');
  if (pathname === '/history') return `/submissions${search}${hash}`;
  if (pathname === '/reviews') return `/submissions${search}${hash}`;
  if (pathname === '/speaking/tasks') return `/speaking/selection${search}${hash}`;

  if (pathname.startsWith('/speaking/review/')) {
    const legacyId = pathname.slice('/speaking/review/'.length);
    const evaluationId = toSpeakingEvaluationRouteId(legacyId);
    if (evaluationId) {
      return `/speaking/phrasing/${evaluationId}${search}${hash}`;
    }
    return `/speaking/selection${search}${hash}`;
  }

  if (pathname.startsWith('/speaking/result/')) {
    const evaluationId = pathname.slice('/speaking/result/'.length);
    return `/speaking/results/${evaluationId}${search}${hash}`;
  }

  if (pathname.startsWith('/speaking/attempt/')) {
    const legacyId = pathname.slice('/speaking/attempt/'.length);
    const evaluationId = toSpeakingEvaluationRouteId(legacyId);
    if (evaluationId) {
      return `/speaking/results/${evaluationId}${search}${hash}`;
    }
    return `/speaking/selection${search}${hash}`;
  }

  if (pathname === '/writing/tasks') {
    return `/writing/practice/library${search}${hash}`;
  }

  if (pathname.startsWith('/writing/tasks/')) {
    // Legacy V1 task IDs have no mapping into the V2 scenario library, so the
    // deep link can't be preserved. Route to the writing landing instead of the
    // retired V1 player.
    return `/writing${search}${hash}`;
  }

  if (pathname.startsWith('/reading/task/')) {
    return `/reading${search}${hash}`;
  }

  if (pathname.startsWith('/listening/task/')) {
    const taskOrEvaluationId = pathname.slice('/listening/task/'.length);
    if (taskOrEvaluationId.startsWith('lt-')) {
      return `/listening/player/${taskOrEvaluationId}${search}${hash}`;
    }
    return `/listening${search}${hash}`;
  }

  return `${pathname}${search}${hash}`;
}

function normalizeAppRoute(route: string) {
  const withoutAppPrefix = route === '/app'
    ? '/'
    : route.startsWith('/app/')
      ? route.replace('/app', '')
      : route;

  if (!withoutAppPrefix.startsWith('/')) {
    return withoutAppPrefix;
  }

  const parsed = new URL(withoutAppPrefix, 'http://localhost');
  return rewriteLegacyLearnerRoute(parsed.pathname, parsed.search, parsed.hash);
}

function normalizeRouteValues<T>(value: T): T {
  if (Array.isArray(value)) {
    return value.map((item) => normalizeRouteValues(item)) as T;
  }

  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>).map(([key, nestedValue]) => {
        if (typeof nestedValue === 'string' && (nestedValue.startsWith('/app') || key.toLowerCase().includes('route') || key.toLowerCase().includes('href'))) {
          return [key, normalizeAppRoute(nestedValue)];
        }
        return [key, normalizeRouteValues(nestedValue)];
      }),
    ) as T;
  }

  return value;
}
