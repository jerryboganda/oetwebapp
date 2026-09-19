import { apiRequest, toNullableString, type ApiRecord } from './client';

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
/**
 * API path of a candidate recording for the review console. The route is
 * Bearer-authorised (AdminOnly), which a bare `<audio src>` cannot satisfy —
 * fetch it with `fetchAuthorizedObjectUrl` and play the object URL.
 */
export function resolvePlacementReviewAudioUrl(sessionId: string, taskId: string): string {
  return `/v1/admin/placement/review/${encodeURIComponent(sessionId)}/audio/${encodeURIComponent(taskId)}`;
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

export interface PlacementInventoryCell {
  module: string;
  band: string;
  total: number;
  active: number;
  inactive: number;
}

export interface PlacementInventoryTask {
  route: string;
  taskType: string;
  total: number;
  active: number;
}

export interface PlacementInventory {
  generatedAt: string;
  rulesetVersion: string;
  objective: PlacementInventoryCell[];
  speaking: PlacementInventoryTask[];
  writing: PlacementInventoryTask[];
  totals: Record<string, number>;
}

function toCount(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

function toTasks(rows: unknown): PlacementInventoryTask[] {
  return (Array.isArray(rows) ? (rows as ApiRecord[]) : []).map((row) => ({
    route: String(row.route ?? ''),
    taskType: String(row.task_type ?? ''),
    total: toCount(row.total),
    active: toCount(row.active),
  }));
}

/** Active item counts by skill × CEFR band (objective) and route (speaking /
 *  writing) — owner spec §8.1 content-completeness check. */
export async function fetchPlacementInventory(): Promise<PlacementInventory> {
  const payload = await apiRequest<ApiRecord>('/v1/admin/placement/inventory');
  const totals = (payload.totals ?? {}) as ApiRecord;
  return {
    generatedAt: String(payload.generated_at ?? ''),
    rulesetVersion: String(payload.ruleset_version ?? 'unknown'),
    objective: (Array.isArray(payload.objective) ? (payload.objective as ApiRecord[]) : []).map((row) => ({
      module: String(row.module ?? ''),
      band: String(row.band ?? ''),
      total: toCount(row.total),
      active: toCount(row.active),
      inactive: toCount(row.inactive),
    })),
    speaking: toTasks(payload.speaking),
    writing: toTasks(payload.writing),
    totals: Object.fromEntries(Object.entries(totals).map(([key, value]) => [key, toCount(value)])),
  };
}

// ── Extra-time accommodations ────────────────────────────────────────

/**
 * Extra time on the placement test is granted by an administrator and never
 * self-selected by a candidate. Each grant records who approved it, when, the
 * percentage, and which attempts used it. Served by the OET API itself
 * (camelCase JSON), not proxied from the engine.
 */

export interface PlacementAccommodationUse {
  sessionId: string;
  appliedAt: string;
  extraTimePercent: number;
}

export interface PlacementAccommodation {
  id: string;
  learnerUserId: string;
  learnerEmail: string;
  learnerName: string | null;
  extraTimePercent: number;
  reference: string | null;
  status: 'active' | 'revoked';
  approvedByUserId: string;
  approvedByName: string;
  approvedAt: string;
  revokedByUserId: string | null;
  revokedByName: string | null;
  revokedAt: string | null;
  revokedReason: string | null;
  uses: PlacementAccommodationUse[];
}

export interface GrantPlacementAccommodationInput {
  /** Exactly one of the two learner identifiers is expected. */
  learnerEmail?: string;
  learnerUserId?: string;
  /** 1..100. */
  extraTimePercent: number;
  /** Administrative reference only (<= 200 chars) — never health details. */
  reference?: string;
}

function toAccommodation(row: ApiRecord): PlacementAccommodation {
  return {
    id: String(row.id ?? ''),
    learnerUserId: String(row.learnerUserId ?? ''),
    learnerEmail: String(row.learnerEmail ?? ''),
    learnerName: toNullableString(row.learnerName),
    extraTimePercent: toCount(row.extraTimePercent),
    reference: toNullableString(row.reference),
    status: String(row.status ?? '').toLowerCase() === 'revoked' ? 'revoked' : 'active',
    approvedByUserId: String(row.approvedByUserId ?? ''),
    approvedByName: String(row.approvedByName ?? ''),
    approvedAt: String(row.approvedAt ?? ''),
    revokedByUserId: toNullableString(row.revokedByUserId),
    revokedByName: toNullableString(row.revokedByName),
    revokedAt: toNullableString(row.revokedAt),
    revokedReason: toNullableString(row.revokedReason),
    uses: (Array.isArray(row.uses) ? (row.uses as ApiRecord[]) : []).map((use) => ({
      sessionId: String(use.sessionId ?? ''),
      appliedAt: String(use.appliedAt ?? ''),
      extraTimePercent: toCount(use.extraTimePercent),
    })),
  };
}

/** Grants, newest first. `learner` is an email or user id; revoked grants are
 *  omitted unless `includeRevoked` is set. */
export async function fetchPlacementAccommodations(
  opts: { learner?: string; includeRevoked?: boolean } = {},
): Promise<PlacementAccommodation[]> {
  const params = new URLSearchParams();
  const learner = opts.learner?.trim();
  if (learner) params.set('learner', learner);
  params.set('includeRevoked', String(Boolean(opts.includeRevoked)));
  const rows = await apiRequest<ApiRecord[]>(`/v1/admin/placement/accommodations?${params.toString()}`);
  return (Array.isArray(rows) ? rows : []).map(toAccommodation);
}

export async function grantPlacementAccommodation(
  input: GrantPlacementAccommodationInput,
): Promise<PlacementAccommodation> {
  const row = await apiRequest<ApiRecord>('/v1/admin/placement/accommodations', {
    method: 'POST',
    body: JSON.stringify(input),
  });
  return toAccommodation(row ?? {});
}

export async function revokePlacementAccommodation(id: string, reason?: string): Promise<PlacementAccommodation> {
  const row = await apiRequest<ApiRecord>(`/v1/admin/placement/accommodations/${encodeURIComponent(id)}/revoke`, {
    method: 'POST',
    body: JSON.stringify(reason ? { reason } : {}),
  });
  return toAccommodation(row ?? {});
}
