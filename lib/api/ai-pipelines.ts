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

/** A model a stage's route really sent in the last 7 days (the model id the provider received). */
export interface PipelineRecentModel {
  provider: string | null;
  model: string | null;
  calls: number;
  succeeded: number;
  lastAt: string;
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
  /** The approved Claude model for the paid-API route (claude-opus-5-5). */
  approvedClaudeModel?: string;
  /** Plain-language statement of the effort each Claude route runs at (null for stages with none). */
  effortNote?: string | null;
  /** Models actually sent in the last 7 days; null for live voice. */
  recentModels?: PipelineRecentModel[] | null;
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

/** One usage window of one subscription account: 5h rolling or weekly (owner directive 2026-10-10). */
export interface PipelineSubscriptionAccountWindow {
  label: '5h' | 'weekly';
  usedTokens: number;
  /** The operator-set safety budget for this window; null = no cap, so no estimate is possible. */
  cap: number | null;
  usedPct: number | null;
  resetsAt: string | null;
  windowStartedAt: string | null;
}

export type SubscriptionAccountStanding = 'serving' | 'parked' | 'cooldown' | 'drained';

export interface PipelineSubscriptionAccountView {
  providerCode: string;
  /** claude-max | codex — the pool only ever permutes accounts inside one group. */
  group: 'claude-max' | 'codex';
  name: string | null;
  standing: SubscriptionAccountStanding;
  standingReason: string | null;
  windows: PipelineSubscriptionAccountWindow[];
  lastQuotaErrorAt: string | null;
  lastQuotaErrorKind: string | null;
  cooldownUntil: string | null;
  sampledAt: string;
  /** false = the sidecar gave us no information at all; never "out of quota". */
  reachable: boolean;
}

export interface SubscriptionPoolPolicyView {
  switchPercent: number;
  cooldownFloorMinutes: number;
  refreshSeconds: number;
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
  subscriptionAccounts: PipelineSubscriptionAccountView[];
  subscriptionPool: SubscriptionPoolPolicyView;
  creditGuard?: CreditGuardState | null;
}

/** The runtime promotional-credit guard snapshot (owner directive 2026-10-09). */
export interface CreditGuardState {
  active: boolean;
  mode: 'none' | 'demoted' | 'skipped';
  providerCode: string;
  grantId: string | null;
  grantUsd: number;
  spentUsd: number;
  remainingUsd: number;
  reserveUsd: number;
  floorUsd: number;
  grantStartsAt: string | null;
  checkedAt: string;
}

export function fetchPipelineOverview(window: OverviewWindow = '7d'): Promise<PipelineOverview> {
  return apiClient.get<PipelineOverview>(`${base}/overview?window=${encodeURIComponent(window)}`);
}

/** The rotation's own view of every subscription account, refreshed live (not the 60s dashboard cache). */
export function fetchSubscriptionAccounts(): Promise<{
  policy: SubscriptionPoolPolicyView;
  lastRefreshedAt: string | null;
  accounts: PipelineSubscriptionAccountView[];
}> {
  return apiClient.get(`${base}/accounts`);
}

/** Park an account at the back of its engine group (or restore it). Audited. */
export function drainSubscriptionAccount(
  providerCode: string,
  drain: boolean,
  reason?: string,
): Promise<{ providerCode: string; drained: boolean }> {
  return apiClient.put<{ providerCode: string; drained: boolean }>(`${base}/accounts/drain`, {
    providerCode,
    drain,
    reason,
  });
}

/** The percent of a window at which the runtime prefers another account. Audited. */
export function setSubscriptionPoolThreshold(
  switchPercent: number,
  reason?: string,
): Promise<{ policy: { switchPercent: number } }> {
  return apiClient.put<{ policy: { switchPercent: number } }>(`${base}/accounts/threshold`, {
    switchPercent,
    reason,
  });
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

// ── Cost breakdown (owner directive 2026-10-10) ──────────────────────────
// Writing and Speaking cost by processing stage. Every figure is an internally
// tracked estimate (provider-reported tokens x list prices, or connected live
// voice minutes x an operator rate). A subscription route is never an API
// charge: its cost is 0 and its API-equivalent value is reported separately.

export type CostRowKind = 'subscription' | 'api' | 'live_voice';
export type CostBasis =
  | 'subscription'
  | 'rate_card'
  | 'stored_estimate'
  | 'duration_estimate'
  | 'duration_assumed'
  | 'reported_tokens'
  | 'mixed'
  | 'unpriced';

export interface CostRow {
  providerId: string;
  providerName: string;
  model: string;
  kind: CostRowKind;
  requests: number;
  successes: number;
  failedAttempts: number;
  retries: number;
  promptTokens: number;
  completionTokens: number;
  cacheTokens: number;
  costUsd: number;
  apiEquivalentUsd: number | null;
  basis: CostBasis;
  minutes: number | null;
  /** Live voice sessions that carried provider-reported token usage. */
  reportedSessions: number;
}

export interface CostComponent {
  key: string;
  label: string;
  requests: number;
  failedAttempts: number;
  retries: number;
  promptTokens: number;
  completionTokens: number;
  cacheTokens: number;
  subscriptionRequests: number;
  apiRequests: number;
  apiUsd: number;
  subscriptionApiEquivalentUsd: number;
  rows: CostRow[];
}

export interface WritingCostBlock {
  grading: CostComponent;
  reviewer: CostComponent;
  totalUsd: number;
  letters: number;
  avgPerLetterUsd: number | null;
}

export interface SpeakingCostBlock {
  liveVoice: CostComponent;
  grading: CostComponent;
  reviewer: CostComponent;
  audioModel: CostComponent;
  totalUsd: number;
  singleCards: number;
  fullMocks: number;
  avgPerSingleCardUsd: number | null;
  avgPerFullMockUsd: number | null;
  avgPerAssessmentUsd: number | null;
  liveVoiceSessions: number;
  liveVoiceMinutes: number;
  /** Minutes whose provider is no longer recorded: they are not priced. */
  liveVoiceUnpricedMinutes: number;
  liveVoiceReportedSessions: number;
}

export interface PromoGrantCost {
  grantId: string;
  providerCode: string;
  grantUsd: number;
  startsAt: string;
  note: string | null;
  consumedInWindowUsd: number;
  consumedTotalUsd: number;
  remainingUsd: number;
  overflowUsd: number;
}

export interface MoneyBlock {
  grossApiUsd: number;
  pipelineApiUsd: number;
  otherFeaturesApiUsd: number;
  subscriptionApiEquivalentUsd: number;
  promoConsumedUsd: number;
  promoRemainingUsd: number;
  outOfPocketUsd: number;
  grants: PromoGrantCost[];
}

export interface CostBreakdown {
  window: OverviewWindow;
  windowStart: string | null;
  generatedAt: string;
  writing: WritingCostBlock;
  speaking: SpeakingCostBlock;
  money: MoneyBlock;
}

export interface CostWindowSummary {
  window: OverviewWindow;
  label: string;
  writingGradingUsd: number;
  writingReviewerUsd: number;
  writingTotalUsd: number;
  letters: number;
  writingAvgPerLetterUsd: number | null;
  speakingLiveVoiceUsd: number;
  speakingGradingUsd: number;
  speakingReviewerUsd: number;
  speakingAudioUsd: number;
  speakingTotalUsd: number;
  singleCards: number;
  fullMocks: number;
  speakingAvgPerAssessmentUsd: number | null;
  speakingAvgPerFullMockUsd: number | null;
  grossApiUsd: number;
  promoConsumedUsd: number;
  outOfPocketUsd: number;
}

export interface CostRunLeg {
  providerId: string;
  providerName: string;
  model: string;
  kind: CostRowKind;
  calls: number;
  failed: number;
  costUsd: number;
}

export interface CostRunPart {
  costUsd: number;
  calls: number;
  failed: number;
  retries: number;
  legs: CostRunLeg[];
}

export interface CostRun {
  kind: 'writing' | 'speaking_card' | 'speaking_mock';
  at: string;
  learner: string;
  succeeded: boolean;
  grading: CostRunPart;
  reviewer: CostRunPart;
  audio: CostRunPart;
  liveVoice: CostRunPart;
  totalUsd: number;
}

export interface LiveVoiceRateView {
  provider: 'openai' | 'gemini';
  name: string;
  model: string;
  perMinuteUsd: number;
  /** false = the built-in starting assumption is in use, not a figure the owner entered. */
  ownerSet: boolean;
  updatedBy: string | null;
  updatedAt: string | null;
  /** Optional blended USD per 1M tokens; both set = sessions with reported usage are priced from tokens. */
  inputPerMillionUsd: number | null;
  outputPerMillionUsd: number | null;
}

export interface CostBreakdownResponse {
  selected: CostBreakdown;
  summary: CostWindowSummary[];
  runs: CostRun[];
  liveVoiceRates: LiveVoiceRateView[];
  rateCardVerifiedOn: string;
}

export function fetchCostBreakdown(window: OverviewWindow = '7d'): Promise<CostBreakdownResponse> {
  return apiClient.get<CostBreakdownResponse>(`${base}/cost-breakdown?window=${encodeURIComponent(window)}`);
}

/** Sets the per-minute live voice rate used to price connected minutes. Audited. */
export function saveLiveVoiceRate(
  provider: 'openai' | 'gemini',
  perMinuteUsd: number,
  options?: { inputPerMillionUsd?: number | null; outputPerMillionUsd?: number | null; reason?: string },
): Promise<{ provider: string; perMinuteUsd: number }> {
  return apiClient.put<{ provider: string; perMinuteUsd: number }>(`${base}/live-voice-rates`, {
    provider,
    perMinuteUsd,
    inputPerMillionUsd: options?.inputPerMillionUsd ?? null,
    outputPerMillionUsd: options?.outputPerMillionUsd ?? null,
    reason: options?.reason,
  });
}

export interface ReconciliationCheck {
  name: string;
  ok: boolean;
  detail: string;
}

export interface CostReconciliation {
  window: OverviewWindow;
  generatedAt: string;
  checks: ReconciliationCheck[];
}

/** On-demand evidence: recomputes the cost figures through independent paths and compares them. Read-only. */
export function fetchCostReconciliation(window: OverviewWindow = '7d'): Promise<CostReconciliation> {
  return apiClient.get<CostReconciliation>(`${base}/cost-breakdown/reconciliation?window=${encodeURIComponent(window)}`);
}

// ── Route benchmarks (the gate a model that leaves Claude must pass) ─────────

export interface BenchmarkRun {
  id: string;
  featureCode: string;
  providerCode: string;
  model: string;
  corpusVersion: string | null;
  passed: boolean;
  recordedAt: string;
}

export interface BenchmarkResult {
  runId: string;
  passed: boolean;
  failures: string[];
}

const opsBase = '/v1/admin/ai';

export async function fetchBenchmarkRuns(featureCode?: string): Promise<BenchmarkRun[]> {
  const query = featureCode ? `?featureCode=${encodeURIComponent(featureCode)}` : '';
  const response = await apiClient.get<{ rows: BenchmarkRun[] }>(`${opsBase}/benchmark-runs${query}`);
  return response.rows;
}

/** Calls the candidate model on the benchmark corpus through the real dispatch path; spends a few cents. */
export function runBenchmark(input: { featureCode: string; providerCode: string; model: string }): Promise<BenchmarkResult> {
  return apiClient.post<BenchmarkResult>(`${opsBase}/benchmark-runs/run`, input);
}
