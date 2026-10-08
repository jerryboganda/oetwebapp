/**
 * Typed client for the AI Pipeline Control Center (`/v1/admin/ai/pipelines`).
 * The server returns provider codes, models and flags only: no key material, ever.
 */
import { apiClient } from '@/lib/api';

export type PipelineStageKey =
  | 'writing.grade'
  | 'writing.grade.review'
  | 'speaking.grade'
  | 'speaking.grade.review'
  | 'speaking.live_voice';

export interface PipelineHop {
  provider: string;
  model: string | null;
  enabled: boolean;
  attempts: number;
  budgetSeconds: number;
}

export interface PipelineHopView extends PipelineHop {
  /** ready | disabled | or the reason a step is left out of the next run. */
  status: string;
}

export interface PipelineStage {
  stageKey: PipelineStageKey;
  label: string;
  kind: 'grading' | 'reviewer' | 'voice';
  stageEnabled: boolean;
  version: number;
  /** Saved | LastKnownGood | InitialDefault */
  source: string;
  hops: PipelineHopView[];
  nextRunStartsOn: string | null;
  lastServed: { providerId: string | null; model: string | null; createdAt: string } | null;
  maxNotFirst: boolean;
  maxDisabled: boolean;
  lastChange: { version: number; kind: string; changedBy: string | null; reason: string | null; at: string } | null;
}

export interface PipelineProvider {
  code: string;
  name: string;
  dialect: string;
  defaultModel: string;
  isActive: boolean;
  hasKey: boolean;
  lastTestStatus: string | null;
  lastTestedAt: string | null;
  isSubscriptionBridge: boolean;
}

export interface PipelinesResponse {
  stages: PipelineStage[];
  providers: PipelineProvider[];
  disableMaxConfirmation: string;
}

export interface PipelineHopInput extends PipelineHop {
  benchmarkRunId?: string | null;
}

export interface SavePipelineInput {
  stageEnabled: boolean;
  hops: PipelineHopInput[];
  expectedVersion: number;
  reason?: string;
  confirmation?: string;
}

export interface PipelineRevision {
  version: number;
  kind: string;
  stageEnabled: boolean;
  hops: PipelineHop[];
  reason: string | null;
  changedBy: string | null;
  at: string;
}

export interface SelfCheckCheck {
  name: string;
  ok: boolean;
  detail?: string | null;
  violations?: Array<{ provider: string; disabledAt: string | null; callsAfterGrace: number; callsInsideGrace: number }>;
  since?: string;
  served?: Array<{ providerId: string | null; outcome: string; calls: number }>;
  probes?: Array<{ provider: string; status: string; latencyMs: number; errorMessage: string | null }>;
}

export interface SelfCheckResponse {
  ranAt: string;
  live: boolean;
  results: Array<{ stageKey: string; label: string; version: number; checks: SelfCheckCheck[] }>;
}

const base = '/v1/admin/ai/pipelines';

export function fetchPipelines(): Promise<PipelinesResponse> {
  return apiClient.get<PipelinesResponse>(base);
}

export function savePipelineStage(stageKey: string, input: SavePipelineInput): Promise<{ stageKey: string; version: number }> {
  return apiClient.put(`${base}/${encodeURIComponent(stageKey)}`, input);
}

export function fetchPipelineHistory(stageKey: string): Promise<PipelineRevision[]> {
  return apiClient.get<PipelineRevision[]>(`${base}/${encodeURIComponent(stageKey)}/history`);
}

export function rollbackPipelineStage(
  stageKey: string,
  toVersion: number,
  expectedVersion: number,
  reason?: string,
): Promise<{ stageKey: string; version: number }> {
  return apiClient.post(`${base}/${encodeURIComponent(stageKey)}/rollback`, { toVersion, expectedVersion, reason });
}

export function restorePipelineDefault(
  stageKey: string,
  expectedVersion: number,
  reason?: string,
): Promise<{ stageKey: string; version: number }> {
  return apiClient.post(`${base}/${encodeURIComponent(stageKey)}/restore-default`, { expectedVersion, reason });
}

export function runPipelineSelfCheck(live: boolean): Promise<SelfCheckResponse> {
  return apiClient.post<SelfCheckResponse>(`${base}/self-check?live=${live ? 'true' : 'false'}`);
}

// ── Phase 2: usage/cost overview, subscriptions, credits ──────────────────
// Every USD figure the overview returns is INTERNALLY TRACKED (our rate-card
// estimate over AiUsageRecords); subscription figures come from our own
// sidecars. Nothing here is provider-verified — the UI must keep saying so.

export type OverviewWindow = 'today' | '7d' | '30d' | 'all';

export interface PipelineBucketUsage {
  bucket: string;
  calls: number;
  successes: number;
  costUsd: number;
}

export interface PipelineProviderUsage {
  providerId: string;
  calls: number;
  successes: number;
  failures: number;
  promptTokens: number;
  completionTokens: number;
  costUsd: number;
  stages: PipelineBucketUsage[];
}

export interface PipelineCostPerUnit {
  count: number;
  totalUsd: number;
  avgUsd: number | null;
}

export interface PipelineLiveVoiceUsage {
  provider: string;
  sessions: number;
}

export interface PipelineClaudeMaxSnapshot {
  utilizationPct: number | null;
  resetsAt: string | null;
  /** reported = our sidecar's counter; estimated = computed from usage rows; unknown = sidecar down. */
  source: string;
  weeklyTokensUsed: number;
  weeklyTokenCap: number;
  writingTokens7d: number;
  speakingTokens7d: number;
}

export interface PipelineReviewerQueueItem {
  assessmentType: string;
  arrived: number;
  inFlight: number;
  codexTotal: number;
  codexSuccess: number;
  codexQuota: number;
  codexTimeout: number;
  apiFallbacks: number;
  completed: number;
  lastFallbackReason: string | null;
}

export interface PipelineCodexReviewerSnapshot {
  source: string;
  requestsThisWeek: number;
  inputTokensThisWeek: number;
  outputTokensThisWeek: number;
  utilizationPct: number | null;
  weekStartedAt: string | null;
  queue: PipelineReviewerQueueItem[];
}

export interface PipelineCreditGrantView {
  id: string;
  providerCode: string;
  grantUsd: number;
  startsAt: string;
  note: string | null;
  spentSinceStartUsd: number;
  /** null when spend already exceeds the grant (shown as exhausted, not negative). */
  remainingUsd: number | null;
  createdByAdminName: string;
  createdAt: string;
}

export interface PipelineOverview {
  window: OverviewWindow;
  windowStart: string | null;
  generatedAt: string;
  providers: PipelineProviderUsage[];
  stageTotals: PipelineBucketUsage[];
  writingLetter: PipelineCostPerUnit;
  speakingAssessment: PipelineCostPerUnit;
  reviewerRun: PipelineCostPerUnit;
  liveVoiceSessions: PipelineLiveVoiceUsage[];
  claudeMax: PipelineClaudeMaxSnapshot;
  codexReviewer: PipelineCodexReviewerSnapshot;
  credits: PipelineCreditGrantView[];
}

export function fetchPipelineOverview(window: OverviewWindow = '7d'): Promise<PipelineOverview> {
  return apiClient.get<PipelineOverview>(`${base}/overview?window=${encodeURIComponent(window)}`);
}

export interface CreditGrantInput {
  providerCode: string;
  grantUsd: number;
  startsAt?: string;
  note?: string;
}

export function createCreditGrant(input: CreditGrantInput): Promise<{ id: string }> {
  return apiClient.post<{ id: string }>(`${base}/credit-grants`, input);
}

export function deleteCreditGrant(id: string): Promise<void> {
  return apiClient.delete<void>(`${base}/credit-grants/${encodeURIComponent(id)}`);
}
