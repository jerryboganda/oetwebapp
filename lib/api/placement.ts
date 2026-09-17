import { apiRequest, type ApiRecord } from './client';

/**
 * Placement test (free General-English assessment on the private engine).
 * Every call goes through the OET API proxy (`/v1/placement/*`) — the
 * engine itself is never publicly reachable, and the proxy enforces the
 * `Placement.Enabled` flag + learner identity.
 */

export type PlacementModule = 'LS' | 'RD' | 'LSN';

export interface PlacementStatus {
  enabled: boolean;
}

export interface PlacementSessionState {
  session_id: string;
  candidate_uid: string;
  mode: string;
  state_version: number;
  target_goal: string;
  flags: string[];
  created_at: string;
  ruleset_version: string;
  ls_status: string;
  rd_status: string;
  lsn_status: string;
  spk_status: string;
  wrt_status: string;
  has_result: boolean;
}

export interface PlacementDeliveryUnit {
  stimulus_id: string | null;
  stimulus_text: string | null;
  stimulus_type: string | null;
  audio_url: string | null;
  items: Array<{
    item_id: string;
    module: string;
    stem: string;
    options: Array<{ option_id: string; text: string }>;
  }>;
  deadline_at: string;
  module_complete: boolean;
}

export interface PlacementSpeakingTask {
  taskId: string;
  taskType: string;
  route: string;
  prompt: string;
  prepSeconds?: number;
  speakingSeconds?: number;
}

export interface PlacementWritingTask {
  taskId: string;
  taskType: string;
  route: string;
  prompt: string;
  minWords?: number;
  minutes?: number;
}

export interface PlacementSkillResult {
  skill: string;
  status: string;
  band: string | null;
  range: [string, string] | null;
  notes: string[];
  can_do: string[];
  growth_areas: string[];
}

export interface PlacementResultReport {
  session_id: string;
  profile_type: string;
  skills: PlacementSkillResult[];
  headline: { kind: string; band: string | null; range: [string, string] | null };
  confidence: string;
  confidence_reasons: string[];
  readiness: { target: string; text: string; disclaimer: string; currency_note: string | null } | null;
  retest_advice: string;
  wording_version: string;
  generated_at: string;
}

export interface PlacementHistoryItem {
  id: string;
  sessionId: string;
  rulesetVersion: string;
  status: string;
  createdAt: string;
}

export interface PlacementUploadedRecording {
  storagePath: string;
  durationSec: number | null;
  container: string;
  metricsProvenance: string;
}

function asString(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

export async function fetchPlacementStatus(): Promise<PlacementStatus> {
  return apiRequest<PlacementStatus>('/v1/placement/status');
}

export async function createPlacementSession(targetGoal?: string): Promise<{ sessionId: string; rulesetVersion: string }> {
  const created = await apiRequest<ApiRecord>('/v1/placement/session', {
    method: 'POST',
    body: JSON.stringify({ targetGoal: targetGoal ?? 'General' }),
  });
  return {
    sessionId: String(created.session_id ?? ''),
    rulesetVersion: String(created.ruleset_version ?? 'unknown'),
  };
}

export async function fetchPlacementSessionState(sessionId: string): Promise<PlacementSessionState> {
  return apiRequest<PlacementSessionState>(`/v1/placement/session/${encodeURIComponent(sessionId)}`);
}

export async function startPlacementModule(sessionId: string, module: PlacementModule): Promise<PlacementDeliveryUnit> {
  return apiRequest<PlacementDeliveryUnit>(
    `/v1/placement/session/${encodeURIComponent(sessionId)}/module/${encodeURIComponent(module)}/start`,
    { method: 'POST' },
  );
}

export async function submitPlacementResponses(
  sessionId: string,
  responses: Array<{ itemId: string; selectedOptionId: string | null; responseMs: number }>,
  replayCount = 0,
): Promise<{ next: PlacementDeliveryUnit | null }> {
  return apiRequest<{ next: PlacementDeliveryUnit | null }>(
    `/v1/placement/session/${encodeURIComponent(sessionId)}/responses`,
    {
      method: 'POST',
      body: JSON.stringify({
        responses: responses.map((r) => ({
          item_id: r.itemId,
          selected_option_id: r.selectedOptionId,
          response_ms: r.responseMs,
        })),
        replay_count: replayCount,
      }),
    },
  );
}

export async function fetchPlacementResultStatus(sessionId: string): Promise<{ complete: boolean }> {
  const status = await apiRequest<ApiRecord>(
    `/v1/placement/session/${encodeURIComponent(sessionId)}/result/status`,
  );
  return { complete: status.complete === true };
}

export async function fetchPlacementReceptiveResult(sessionId: string): Promise<PlacementResultReport> {
  return apiRequest<PlacementResultReport>(
    `/v1/placement/session/${encodeURIComponent(sessionId)}/result/receptive`,
  );
}

export async function fetchPlacementFullResult(sessionId: string): Promise<PlacementResultReport> {
  return apiRequest<PlacementResultReport>(
    `/v1/placement/session/${encodeURIComponent(sessionId)}/result/full`,
  );
}

export async function fetchPlacementHistory(): Promise<PlacementHistoryItem[]> {
  const rows = await apiRequest<ApiRecord[]>('/v1/placement/history');
  return (Array.isArray(rows) ? rows : []).map((row) => ({
    id: String(row.id ?? ''),
    sessionId: String(row.sessionId ?? ''),
    rulesetVersion: String(row.rulesetVersion ?? 'unknown'),
    status: String(row.status ?? ''),
    createdAt: String(row.createdAt ?? ''),
  }));
}

export async function fetchPlacementStoredResult(resultId: string): Promise<PlacementResultReport> {
  return apiRequest<PlacementResultReport>(`/v1/placement/history/${encodeURIComponent(resultId)}`);
}

export async function fetchPlacementSpeakingTasks(sessionId: string): Promise<PlacementSpeakingTask[]> {
  const payload = await apiRequest<ApiRecord[]>(
    `/v1/placement/session/${encodeURIComponent(sessionId)}/speaking/tasks`,
  );
  return (Array.isArray(payload) ? payload : []).map((task) => ({
    taskId: String(task.task_id ?? ''),
    taskType: String(task.task_type ?? ''),
    route: String(task.route ?? ''),
    prompt: String(task.prompt ?? ''),
    prepSeconds: typeof task.prep_seconds === 'number' ? task.prep_seconds : undefined,
    speakingSeconds: typeof task.speaking_seconds === 'number' ? task.speaking_seconds : undefined,
  }));
}

export async function uploadPlacementRecording(
  file: Blob,
  fileName: string,
): Promise<PlacementUploadedRecording> {
  const body = new FormData();
  body.append('file', file, fileName);
  const uploaded = await apiRequest<ApiRecord>('/v1/placement/upload', {
    method: 'POST',
    body,
  });
  const metrics = (uploaded.metrics ?? {}) as ApiRecord;
  return {
    storagePath: String(uploaded.storage_path ?? ''),
    durationSec: typeof metrics.duration_sec === 'number' ? metrics.duration_sec : null,
    container: String(metrics.container ?? 'unknown'),
    metricsProvenance: String(metrics.metrics_provenance ?? 'unparsed'),
  };
}

export async function submitPlacementSpeaking(
  sessionId: string,
  taskId: string,
  storagePath: string,
): Promise<{ accepted: boolean; status: string; usable: boolean | null }> {
  const payload = await apiRequest<ApiRecord>(
    `/v1/placement/session/${encodeURIComponent(sessionId)}/speaking/${encodeURIComponent(taskId)}/submit`,
    { method: 'POST', body: JSON.stringify({ storagePath }) },
  );
  return {
    accepted: payload.accepted === true,
    status: String(payload.status ?? 'pending_review'),
    usable: typeof payload.usable === 'boolean' ? payload.usable : null,
  };
}

export async function fetchPlacementWritingTasks(sessionId: string): Promise<PlacementWritingTask[]> {
  const payload = await apiRequest<ApiRecord[]>(
    `/v1/placement/session/${encodeURIComponent(sessionId)}/writing/tasks`,
  );
  return (Array.isArray(payload) ? payload : []).map((task) => ({
    taskId: String(task.task_id ?? ''),
    taskType: String(task.task_type ?? ''),
    route: String(task.route ?? ''),
    prompt: String(task.prompt ?? ''),
    minWords: typeof task.min_words === 'number' ? task.min_words : undefined,
    minutes: typeof task.minutes === 'number' ? task.minutes : undefined,
  }));
}

export async function savePlacementWritingDraft(sessionId: string, taskId: string, text: string): Promise<void> {
  await apiRequest(
    `/v1/placement/session/${encodeURIComponent(sessionId)}/writing/${encodeURIComponent(taskId)}/draft`,
    { method: 'PUT', body: JSON.stringify({ text }) },
  );
}

export async function submitPlacementWriting(
  sessionId: string,
  taskId: string,
  text: string,
): Promise<{ accepted: boolean; status: string; usable: boolean | null }> {
  const payload = await apiRequest<ApiRecord>(
    `/v1/placement/session/${encodeURIComponent(sessionId)}/writing/${encodeURIComponent(taskId)}/submit`,
    { method: 'POST', body: JSON.stringify({ text }) },
  );
  return {
    accepted: payload.accepted === true,
    status: String(payload.status ?? 'pending_review'),
    usable: typeof payload.usable === 'boolean' ? payload.usable : null,
  };
}

export function resolvePlacementAudioUrl(audioUrl: string | null): string | null {
  // Engine audio URLs are engine-relative ("/api/media/audio/x.mp3"); the
  // proxy exposes them under /v1/placement/audio/x.mp3.
  if (!audioUrl) return null;
  const fileName = asString(audioUrl.split('/').pop());
  return fileName ? `/v1/placement/audio/${encodeURIComponent(fileName)}` : null;
}
