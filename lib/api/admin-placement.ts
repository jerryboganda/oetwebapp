import { apiRequest, type ApiRecord } from './client';

/**
 * Admin surface for the placement test's pending-review queue (the private
 * GEPA engine's reviewer flow, proxied by the OET API — reviewers never log
 * into the engine).
 */

export interface PlacementReviewEntry {
  sessionId: string;
  flagType: string;
  module: string;
  details: string;
  timestamp: string;
}

export interface PlacementReviewSession {
  sessionId: string;
  taskId: string;
  taskPrompt: string | null;
  candidateAudioUrl: string | null;
  candidateDraft: string | null;
  transcript: string | null;
  atLower: Record<string, number>;
  atUpper: Record<string, number>;
  aiRationale: string | null;
  flags: string[];
}

export interface PlacementEngineHealth {
  ready: boolean;
  checks?: Record<string, unknown>;
}

export async function fetchPlacementReviewQueue(): Promise<PlacementReviewEntry[]> {
  const rows = await apiRequest<ApiRecord[]>('/v1/admin/placement/review/queue');
  return (Array.isArray(rows) ? rows : []).map((row) => ({
    sessionId: String(row.session_id ?? ''),
    flagType: String(row.flag_type ?? ''),
    module: String(row.module ?? ''),
    details: String(row.details ?? ''),
    timestamp: String(row.timestamp ?? ''),
  }));
}

export async function fetchPlacementReviewSession(sessionId: string): Promise<PlacementReviewSession> {
  const row = await apiRequest<ApiRecord>(`/v1/admin/placement/review/${encodeURIComponent(sessionId)}`);
  return {
    sessionId,
    taskId: String(row.task_id ?? ''),
    taskPrompt: typeof row.task_prompt === 'string' ? row.task_prompt : null,
    candidateAudioUrl: typeof row.candidate_audio_url === 'string' ? row.candidate_audio_url : null,
    candidateDraft: typeof row.candidate_draft === 'string' ? row.candidate_draft : null,
    transcript: typeof row.transcript === 'string' ? row.transcript : null,
    atLower: (row.at_lower ?? {}) as Record<string, number>,
    atUpper: (row.at_upper ?? {}) as Record<string, number>,
    aiRationale: typeof row.ai_rationale === 'string' ? row.ai_rationale : null,
    flags: Array.isArray(row.flags) ? row.flags.map(String) : [],
  };
}

/** Staff-only streaming of the candidate's recording through the OET proxy. */
export function resolvePlacementReviewAudioUrl(sessionId: string, taskId: string): string {
  return `/api/backend/v1/admin/placement/review/${encodeURIComponent(sessionId)}/audio/${encodeURIComponent(taskId)}`;
}

export async function rescorePlacementSession(
  sessionId: string,
  options: { taskId?: string; rubricVersion?: string; reason?: string } = {},
): Promise<ApiRecord> {
  return apiRequest(`/v1/admin/placement/review/${encodeURIComponent(sessionId)}/rescore`, {
    method: 'POST',
    body: JSON.stringify({
      task_id: options.taskId ?? null,
      rubric_version: options.rubricVersion ?? null,
      reason: options.reason ?? 'Reviewer requested rescore',
    }),
  });
}

export async function humanScorePlacementSession(
  sessionId: string,
  taskId: string,
  atLower: Record<string, number>,
  atUpper: Record<string, number>,
  rationale: string,
): Promise<ApiRecord> {
  return apiRequest(`/v1/admin/placement/review/${encodeURIComponent(sessionId)}/human-score`, {
    method: 'POST',
    body: JSON.stringify({
      task_id: taskId,
      at_lower: atLower,
      at_upper: atUpper,
      rationale,
    }),
  });
}

export async function fetchPlacementEngineHealth(): Promise<PlacementEngineHealth> {
  return apiRequest<PlacementEngineHealth>('/v1/admin/placement/health');
}
