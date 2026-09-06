import { apiRequest, type ApiRecord } from './client';
import { minutesToLabel, toSubTest } from './task-mappers';
import { normalizeAppRoute } from './route-normalizer';
import { type StudyPlanTask } from '../mock-data';

/**
 * Study plan tasks + swap.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export async function fetchStudyPlan(): Promise<StudyPlanTask[]> {
  const plan = await apiRequest<ApiRecord>('/v1/study-plan');
  return (plan.items ?? []).map((item: ApiRecord) => ({
    id: item.itemId,
    title: item.title,
    subTest: toSubTest(item.subtest),
    duration: minutesToLabel(item.durationMinutes),
    rationale: item.rationale,
    dueDate: item.dueDate,
    status: item.status,
    section: item.section,
    contentId: item.contentId ?? undefined,
    type: item.itemType ?? undefined,
    route: typeof item.route === 'string' ? normalizeAppRoute(item.route) : undefined,
  }));
}

export interface StudyPlanTaskUpdate extends Partial<StudyPlanTask> {
  feedbackRating?: number;
  actualMinutesSpent?: number;
}

export async function updateStudyPlanTask(taskId: string, updates: StudyPlanTaskUpdate): Promise<StudyPlanTask> {
  let result: ApiRecord;
  if (updates.status === 'completed') {
    const body: Record<string, unknown> = {};
    if (updates.feedbackRating !== undefined) body.feedbackRating = updates.feedbackRating;
    if (updates.actualMinutesSpent !== undefined) body.actualMinutesSpent = updates.actualMinutesSpent;
    result = await apiRequest(`/v1/study-plan/items/${taskId}/complete`, {
      method: 'POST',
      body: Object.keys(body).length > 0 ? JSON.stringify(body) : undefined,
    });
  } else if (updates.status === 'not_started') {
    result = await apiRequest(`/v1/study-plan/items/${taskId}/reset`, { method: 'POST' });
  } else if (updates.dueDate) {
    result = await apiRequest(`/v1/study-plan/items/${taskId}/reschedule`, { method: 'POST', body: JSON.stringify({ dueDate: updates.dueDate ?? null }) });
  } else {
    result = await apiRequest(`/v1/study-plan/items/${taskId}/skip`, { method: 'POST' });
  }

  return {
    id: result.itemId,
    title: result.title,
    subTest: toSubTest(result.subtest),
    duration: minutesToLabel(result.durationMinutes),
    rationale: result.rationale,
    dueDate: result.dueDate,
    status: result.status,
    section: result.section,
    contentId: result.contentId ?? undefined,
    type: result.itemType ?? undefined,
    route: typeof result.route === 'string' ? result.route : undefined,
  };
}

export interface StudyPlanSwapCandidate {
  contentId: string | null;
  title: string;
  route: string;
  durationMinutes: number;
}

export async function fetchStudyPlanSwapCandidates(taskId: string): Promise<StudyPlanSwapCandidate[]> {
  const result = await apiRequest<ApiRecord>(`/v1/study-plan/items/${taskId}/swap`, {
    method: 'POST',
    body: JSON.stringify({}),
  });
  const candidates = (result.candidates as ApiRecord[] | undefined) ?? [];
  return candidates.map((c) => ({
    contentId: (c.contentId as string | null) ?? null,
    title: String(c.title ?? ''),
    route: String(c.route ?? ''),
    durationMinutes: Number(c.durationMinutes ?? 0),
  }));
}

export async function applyStudyPlanSwap(taskId: string, replacementContentId: string): Promise<StudyPlanTask> {
  const result = await apiRequest<ApiRecord>(`/v1/study-plan/items/${taskId}/swap`, {
    method: 'POST',
    body: JSON.stringify({ replacementContentId }),
  });
  return {
    id: result.itemId,
    title: result.title,
    subTest: toSubTest(result.subtest),
    duration: minutesToLabel(result.durationMinutes),
    rationale: result.rationale,
    dueDate: result.dueDate,
    status: result.status,
    section: result.section,
    contentId: result.contentId ?? undefined,
    type: result.itemType ?? undefined,
    route: typeof result.route === 'string' ? result.route : undefined,
  };
}

// `fetchWritingTask` is retained: it is still used by `submitWritingTask` below
// to resolve the task title after a submit. The V1 `fetchWritingTasks` (list),
// `fetchWritingChecklist`, and `submitWritingDraft` were removed with the
// retired /writing/library and /writing/player surfaces.
