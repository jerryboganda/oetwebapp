/**
 * Speaking learner practice surface: inline transcript comments,
 * self-practice launcher, drills bank — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest, asRecord, type ApiRecord } from './client';

export interface SpeakingTranscriptComment {
  commentId: string;
  attemptId: string;
  expertId: string;
  transcriptLineIndex: number;
  criterionCode: string;
  body: string;
  createdAt: string;
}

function mapTranscriptComment(rec: ApiRecord): SpeakingTranscriptComment {
  return {
    commentId: typeof rec.commentId === 'string' ? rec.commentId : '',
    attemptId: typeof rec.attemptId === 'string' ? rec.attemptId : '',
    expertId: typeof rec.expertId === 'string' ? rec.expertId : '',
    transcriptLineIndex: typeof rec.transcriptLineIndex === 'number' ? rec.transcriptLineIndex : 0,
    criterionCode: typeof rec.criterionCode === 'string' ? rec.criterionCode : 'general',
    body: typeof rec.body === 'string' ? rec.body : '',
    createdAt: typeof rec.createdAt === 'string' ? rec.createdAt : '',
  };
}

export async function fetchSpeakingTranscriptComments(attemptId: string): Promise<SpeakingTranscriptComment[]> {
  const json = await apiRequest<ApiRecord>(`/v1/speaking/attempts/${encodeURIComponent(attemptId)}/comments`);
  const items = Array.isArray(json.comments) ? json.comments.map(asRecord) : [];
  return items.map(mapTranscriptComment);
}

export async function postExpertSpeakingTranscriptComment(
  attemptId: string,
  payload: { transcriptLineIndex: number; body: string; criterionCode?: string },
): Promise<SpeakingTranscriptComment> {
  const json = await apiRequest<ApiRecord>(
    `/v1/expert/speaking/attempts/${encodeURIComponent(attemptId)}/comments`,
    { method: 'POST', body: JSON.stringify(payload) },
  );
  return mapTranscriptComment(json);
}

// Wave 5 of docs/SPEAKING-MODULE-PLAN.md - deep-link from a speaking
// task into the AI-patient Conversation module. Returns the redirect
// path the caller should navigate the learner to.
export interface SpeakingSelfPracticeStartResult {
  sessionId: string;
  redirectPath: string;
  feedbackMessage?: string | null;
}

export async function startSpeakingSelfPracticeSession(
  taskId: string,
): Promise<SpeakingSelfPracticeStartResult> {
  const json = await apiRequest<ApiRecord>(
    `/v1/speaking/tasks/${encodeURIComponent(taskId)}/self-practice`,
    { method: 'POST', body: JSON.stringify({}) },
  );
  const session = asRecord(json.session);
  const sessionId = typeof session.id === 'string' ? session.id : '';
  const redirectPath = typeof json.redirectPath === 'string' && json.redirectPath
    ? json.redirectPath
    : `/conversation/${sessionId}`;
  const feedbackMessage = typeof json.feedbackMessage === 'string' ? json.feedbackMessage : null;
  return { sessionId, redirectPath, feedbackMessage };
}

// Wave 6 of docs/SPEAKING-MODULE-PLAN.md - speaking drills bank.
export interface SpeakingDrillRow {
  id: string;
  drillId: string;
  title: string;
  kind: string;
  difficulty: string;
  estimatedDurationMinutes: number;
  professionCode: string | null;
  criteriaFocus: string[];
  caseNotes: string | null;
  completed: boolean;
}

export interface SpeakingDrillsListResponse {
  kinds: string[];
  totalCount: number;
  completedCount: number;
  items: SpeakingDrillRow[];
}

export async function fetchSpeakingDrills(filters?: {
  kind?: string;
  profession?: string;
  criterion?: string;
}): Promise<SpeakingDrillsListResponse> {
  const params = new URLSearchParams();
  if (filters?.kind) params.set('kind', filters.kind);
  if (filters?.profession) params.set('profession', filters.profession);
  if (filters?.criterion) params.set('criterion', filters.criterion);
  const qs = params.toString();
  const json = await apiRequest<ApiRecord>(`/v1/speaking/drills${qs ? `?${qs}` : ''}`);
  const items = Array.isArray(json.items) ? json.items.map(asRecord) : [];
  const kinds = Array.isArray(json.kinds)
    ? json.kinds.filter((k): k is string => typeof k === 'string')
    : [];
  return {
    kinds,
    totalCount: typeof json.totalCount === 'number' ? json.totalCount : items.length,
    completedCount: typeof json.completedCount === 'number' ? json.completedCount : 0,
    items: items.map((row) => ({
      id: typeof row.id === 'string' ? row.id : '',
      drillId: typeof row.drillId === 'string'
        ? row.drillId
        : typeof row.id === 'string'
          ? row.id
          : '',
      title: typeof row.title === 'string' ? row.title : '',
      kind: typeof row.kind === 'string' ? row.kind : 'drill',
      difficulty: typeof row.difficulty === 'string' ? row.difficulty : 'easy',
      estimatedDurationMinutes:
        typeof row.estimatedDurationMinutes === 'number' ? row.estimatedDurationMinutes : 5,
      professionCode: typeof row.professionCode === 'string' ? row.professionCode : null,
      criteriaFocus: Array.isArray(row.criteriaFocus)
        ? row.criteriaFocus.filter((c): c is string => typeof c === 'string')
        : [],
      caseNotes: typeof row.caseNotes === 'string' ? row.caseNotes : null,
      completed: row.completed === true,
    })),
  };
}
