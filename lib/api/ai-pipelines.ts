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
