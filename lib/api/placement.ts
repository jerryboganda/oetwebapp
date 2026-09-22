import { apiRequest, asRecord, type ApiRecord } from './client';

/**
 * Placement test (free General-English assessment on the private engine).
 * Every call goes through the OET API proxy (`/v1/placement/*`) — the
 * engine itself is never publicly reachable, and the proxy enforces the
 * `Placement.Enabled` flag + learner identity.
 */

export type PlacementModule = 'LS' | 'RD' | 'LSN';

export interface PlacementStatus {
  enabled: boolean;
  betaOnly?: boolean;
  /** 'not_in_beta' while the controlled beta excludes this account. */
  access?: 'granted' | 'not_in_beta';
  /** Admin-approved extra time (% on timed sections) for this account, else
   *  null. Read-only for the candidate — it is never a client-side setting. */
  extraTimePercent?: number | null;
}

/** True when this learner can actually open the test (flag on, and inside
 *  the beta allowlist while the beta is active). */
export function canAccessPlacement(status: PlacementStatus | null | undefined): boolean {
  return Boolean(status?.enabled) && (status?.access ?? 'granted') === 'granted';
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
  /** Full allowance for the unit in seconds (engine-owned). Optional so an
   *  engine that predates unit timing still parses. */
  time_budget_sec?: number;
  /** Null until the client calls startPlacementUnit. */
  started_at?: string | null;
  /** Listening only. */
  audio_duration_sec?: number | null;
  max_plays?: number | null;
}

export type PlacementTechnicalReason =
  | 'audio_unavailable'
  | 'audio_decode_error'
  | 'audio_zero_duration'
  | 'media_timeout';

/** localStorage key holding the in-progress session id (resume + the
 *  dashboard card's Continue state). */
export const PLACEMENT_ACTIVE_SESSION_KEY = 'oet_placement_active_session';

export interface PlacementDiagnosticArea {
  correct: number;
  total: number;
  strengths: string[];
  weaknesses: string[];
}

/** Prompt audio the candidate hears for a Speaking task (engine TaskAudio). */
export interface PlacementSpeakingTaskAudio {
  /** Engine-relative, e.g. "/api/media/audio/<task_id>.mp3" — resolve with
   *  resolvePlacementAudioUrl and fetch through fetchAuthorizedObjectUrl. */
  storagePath: string;
  durationSec: number;
}

export interface PlacementSpeakingTask {
  taskId: string;
  taskType: string;
  route: string;
  /** Candidate-safe instruction (the engine strips scripts). */
  prompt: string;
  prepSeconds?: number;
  speakingSeconds?: number;
  /** False when the candidate must hear, not read, the task. Undefined on an
   *  engine that predates the field. */
  candidateSeesText?: boolean;
  /** 'text_read_aloud' | 'audio_only_repeat' | 'text_prompt' |
   *  'audio_then_speak' | 'interlocutor_audio_then_speak'; '' when absent. */
  delivery: string;
  /** Null when the task has no prompt audio (yet). */
  audio: PlacementSpeakingTaskAudio | null;
}

export interface PlacementWritingTask {
  taskId: string;
  taskType: string;
  route: string;
  prompt: string;
  minWords?: number;
  minutes?: number;
  /** Authoritative duration for the countdown (extra time already applied
   *  server-side); `minutes` is the same value rounded, for display only. */
  timeLimitSeconds?: number;
}

export interface PlacementSkillResult {
  /** 'RD' | 'LSN' | 'SPK' | 'WRT' — Language Systems is never a skill. */
  skill: string;
  /** 'measured' | 'insufficient_evidence' | 'not_measured' */
  status: string;
  band: string | null;
  range: [string, string] | null;
  notes: string[];
  /** The engine serializes camelCase; snake_case kept for older rows. */
  canDo?: string[];
  can_do?: string[];
  growthAreas?: string[];
  growth_areas?: string[];
}

/**
 * The engine serializes report-level fields in camelCase (with snake_case
 * aliases on deserialize only) while some nested fields stay snake_case —
 * every report reader must tolerate both (verified against live output).
 */
export interface PlacementResultReport {
  session_id: string;
  profile_type?: string;
  profileType?: string;
  skills: PlacementSkillResult[];
  headline: { kind: string; band: string | null; range: [string, string] | null };
  confidence: string;
  confidence_reasons?: string[];
  confidenceReasons?: string[];
  readiness: {
    target: string;
    text: string;
    disclaimer: string;
    /** The engine serializes camelCase; snake_case kept for older rows. */
    currency_note?: string | null;
    currencyNote?: string | null;
  } | null;
  retest_advice?: string;
  retestAdvice?: string;
  wording_version?: string;
  wordingVersion?: string;
  generated_at?: string;
  generatedAt?: string;
  /** Grammar/Vocabulary diagnostics — never a fifth skill. */
  diagnostics?: {
    /** Grammar vs vocabulary breakdown (engine D-032); absent on older reports. */
    language_systems?: {
      grammar?: PlacementDiagnosticArea;
      vocabulary?: PlacementDiagnosticArea;
    } | null;
    /** Construct-level Language Systems diagnostic present on every report. */
    languageSystems?: {
      band?: string | null;
      range?: [string, string] | null;
      constructsStrong?: string[];
      constructsWeak?: string[];
    };
    pronunciationNotes?: string[];
    fluencyNotes?: string[];
  };
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

/** First argument that is a finite number — lets a reader accept both wire spellings. */
function firstNumber(...values: unknown[]): number | undefined {
  for (const value of values) {
    if (typeof value === 'number' && Number.isFinite(value)) return value;
  }
  return undefined;
}

export async function fetchPlacementStatus(): Promise<PlacementStatus> {
  return apiRequest<PlacementStatus>('/v1/placement/status');
}

export type PlacementDeviceClass = 'mobile' | 'tablet' | 'desktop';

/** Pure rule: a narrow viewport is mobile; a mid-width viewport is a tablet
 *  only when its primary pointer is touch (a small desktop window is not). */
export function derivePlacementDeviceClass(viewportWidth: number, coarsePointer: boolean): PlacementDeviceClass {
  if (viewportWidth < 768) return 'mobile';
  if (viewportWidth < 1024 && coarsePointer) return 'tablet';
  return 'desktop';
}

/** Reads the live viewport; guards window/matchMedia for SSR and tests. */
export function detectPlacementDeviceClass(): PlacementDeviceClass {
  if (typeof window === 'undefined' || !Number.isFinite(window.innerWidth)) return 'desktop';
  const coarse =
    typeof window.matchMedia === 'function'
      ? window.matchMedia('(pointer: coarse)').matches
      : typeof navigator !== 'undefined' && navigator.maxTouchPoints > 0;
  return derivePlacementDeviceClass(window.innerWidth, coarse);
}

export async function createPlacementSession(targetGoal?: string): Promise<{ sessionId: string; rulesetVersion: string }> {
  const created = await apiRequest<ApiRecord>('/v1/placement/session', {
    method: 'POST',
    body: JSON.stringify({ targetGoal: targetGoal ?? 'General', deviceClass: detectPlacementDeviceClass() }),
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

/**
 * Start the server clock on the module's current unit — call it when the
 * unit is actually usable (Listening: once audio playback has begun), so
 * buffering is never charged to the candidate. Idempotent engine-side.
 * Returns the updated unit, or null if this engine predates unit timing.
 */
export async function startPlacementUnit(
  sessionId: string,
  module: PlacementModule,
): Promise<PlacementDeliveryUnit | null> {
  try {
    return await apiRequest<PlacementDeliveryUnit>(
      `/v1/placement/session/${encodeURIComponent(sessionId)}/module/${encodeURIComponent(module)}/unit/start`,
      { method: 'POST' },
    );
  } catch {
    // Older engine without the start route: the build-time deadline (which
    // already carries a load allowance) stays authoritative.
    return null;
  }
}

/** Report that the current unit's media could not be delivered. The unit is
 *  excluded from scoring and the engine serves a replacement. */
export async function reportPlacementUnitTechnical(
  sessionId: string,
  module: PlacementModule,
  reason: PlacementTechnicalReason,
): Promise<{ next: PlacementDeliveryUnit | null }> {
  return apiRequest<{ next: PlacementDeliveryUnit | null }>(
    `/v1/placement/session/${encodeURIComponent(sessionId)}/module/${encodeURIComponent(module)}/unit/technical`,
    { method: 'POST', body: JSON.stringify({ reason }) },
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
  // The engine serializes SpeakingTask directly: identity fields stay
  // snake_case (task_id, task_type) but the timing fields are renamed
  // camelCase (prepSeconds, maxSpeakSeconds) and the prompt audio is a nested
  // { storagePath, durationSec }. Read both spellings.
  return (Array.isArray(payload) ? payload : []).map((task) => {
    const audio = asRecord(task.audio);
    const storagePath = asString(audio.storagePath) ?? asString(audio.storage_path);
    const seesText = task.candidate_sees_text ?? task.candidateSeesText;
    return {
      taskId: String(task.task_id ?? task.taskId ?? ''),
      taskType: String(task.task_type ?? task.taskType ?? ''),
      route: String(task.route ?? ''),
      prompt: String(task.prompt ?? ''),
      prepSeconds: firstNumber(task.prepSeconds, task.prep_seconds),
      speakingSeconds: firstNumber(task.maxSpeakSeconds, task.max_speak_seconds, task.speaking_seconds),
      candidateSeesText: typeof seesText === 'boolean' ? seesText : undefined,
      delivery: String(task.delivery ?? ''),
      audio: storagePath
        ? { storagePath, durationSec: firstNumber(audio.durationSec, audio.duration_sec) ?? 0 }
        : null,
    };
  });
}

/** Prompt-audio plays a Speaking task allows: sentence reconstruction is a
 *  one-shot memory task, every other audio task may be heard twice (matches
 *  the engine's standalone client). */
export function speakingPromptMaxPlays(taskType: string): number {
  return taskType === 'sentence_reconstruction' ? 1 : 2;
}

/** True when the candidate must hear the task (not read it) — its prompt audio
 *  is required, so missing audio is a fault, never a silent screen. */
export function speakingTaskExpectsAudio(task: Pick<PlacementSpeakingTask, 'candidateSeesText' | 'delivery'>): boolean {
  // `includes`, not `startsWith`: the 12 simulated-interaction tasks are
  // delivered as 'interlocutor_audio_then_speak' (and the candidate sees text).
  return task.candidateSeesText === false || task.delivery.includes('audio');
}

export async function uploadPlacementRecording(
  file: Blob,
  fileName: string,
): Promise<PlacementUploadedRecording> {
  const body = new FormData();
  body.append('file', file, fileName);
  // `json: false` is load-bearing: getHeaders defaults to setting
  // Content-Type: application/json, which overwrites the browser's generated
  // multipart boundary and makes the .NET IFormFile binder reject the upload
  // with a bodiless 415. Every other FormData call site opts out the same way.
  // The longer timeout matches the other recording uploads — the 30s default
  // aborts a large recording on mobile data mid-flight.
  const uploaded = await apiRequest<ApiRecord>('/v1/placement/upload', {
    method: 'POST',
    body,
  }, { json: false, timeoutMs: 90_000 });
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
  // WritingTask is serialized directly too: the time limit is camelCase
  // (timeLimitSeconds, extra time already applied) and the word guidance is a
  // nested { min, max } object.
  return (Array.isArray(payload) ? payload : []).map((task) => {
    const guidance = (task.word_guidance ?? task.wordGuidance ?? null) as ApiRecord | null;
    const limitSeconds = firstNumber(task.timeLimitSeconds, task.time_limit_seconds);
    return {
      taskId: String(task.task_id ?? task.taskId ?? ''),
      taskType: String(task.task_type ?? task.taskType ?? ''),
      route: String(task.route ?? ''),
      prompt: String(task.prompt ?? ''),
      minWords: firstNumber(guidance?.min, task.min_words),
      minutes: limitSeconds !== undefined ? Math.max(1, Math.round(limitSeconds / 60)) : firstNumber(task.minutes),
      timeLimitSeconds: limitSeconds,
    };
  });
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

/**
 * Engine audio URLs are engine-relative ("/api/media/audio/x.mp3"); the proxy
 * exposes them under /v1/placement/audio/x.mp3.
 *
 * NOTE: this returns an **API path**, not a URL a browser can fetch directly.
 * It still needs the API base prefix and an Authorization header — put it
 * through `fetchAuthorizedObjectUrl`, never straight into an `<audio src>`.
 */
export function resolvePlacementAudioUrl(audioUrl: string | null): string | null {
  if (!audioUrl) return null;
  const fileName = asString(audioUrl.split('/').pop());
  return fileName ? `/v1/placement/audio/${encodeURIComponent(fileName)}` : null;
}
