/**
 * API client for the AI Usage Management subsystem (Slices 1-7).
 *
 * All calls authenticate via the shared apiRequest() helper in ./api so
 * they pick up JWT refresh, CSP, and retry behaviour automatically.
 *
 * See docs/AI-USAGE-POLICY.md for the policy model and behaviour.
 */

import { env } from './env';
import { ensureFreshAccessToken } from './auth-client';
import { fetchWithTimeout } from './network/fetch-with-timeout';

// ═════════════════════════════════════════════════════════════════════════
// Types — mirror .NET contracts 1:1 so the UI never invents fields.
// ═════════════════════════════════════════════════════════════════════════

export type AiKeySource = 'None' | 'Platform' | 'Byok' | 'PlatformFallback';
export type AiCallOutcome =
  | 'Success'
  | 'ProviderError'
  | 'GatewayRefused'
  | 'Cancelled'
  | 'Timeout'
  | 'PlatformError';

export type AiQuotaPeriod = 'Monthly' | 'Daily' | 'Weekly' | 'Rolling30d' | 'NeverExpire';
export type AiQuotaRolloverPolicy = 'Expire' | 'RolloverCapped' | 'RolloverFull';
export type AiOveragePolicy = 'Deny' | 'AllowWithCharge' | 'AutoUpgrade' | 'DegradeToSmallerModel';
export type AiKillSwitchScope = 'PlatformKeysOnly' | 'AllCalls';
export type AiCredentialMode = 'Auto' | 'ByokOnly' | 'PlatformOnly';
export type AiProviderDialect = 'OpenAiCompatible' | 'Anthropic' | 'Cloudflare' | 'Mock';
export type AiCredentialStatus = 'Active' | 'Invalid' | 'Revoked';

export interface AiUsageRow {
  id: string;
  userId: string | null;
  featureCode: string;
  providerId: string | null;
  model: string | null;
  keySource: AiKeySource;
  rulebookVersion: string | null;
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
  costEstimateUsd: number;
  outcome: AiCallOutcome;
  errorCode: string | null;
  errorMessage: string | null;
  latencyMs: number;
  retryCount: number;
  policyTrace: string | null;
  createdAt: string;
  periodMonthKey: string;
  periodDayKey: string;
}

export interface AiUsagePage {
  page: number;
  pageSize: number;
  total: number;
  rows: AiUsageRow[];
}

export interface AiUsageSummaryRow {
  key: string;
  calls: number;
  totalTokens: number;
  costEstimateUsd?: number;
  successes?: number;
  failures?: number;
}

export interface AiQuotaPlan {
  id: string;
  code: string;
  name: string;
  description: string;
  period: AiQuotaPeriod;
  monthlyTokenCap: number;
  dailyTokenCap: number;
  maxConcurrentRequests: number;
  rolloverPolicy: AiQuotaRolloverPolicy;
  rolloverCapPct: number;
  overagePolicy: AiOveragePolicy;
  overageRatePer1kTokens: number | null;
  autoUpgradeTargetPlanCode: string | null;
  degradeModel: string | null;
  allowedFeaturesCsv: string;
  allowedModelsCsv: string;
  isActive: boolean;
  displayOrder: number;
  createdAt: string;
  updatedAt: string;
}

export interface AiGlobalPolicy {
  id: string;
  killSwitchEnabled: boolean;
  killSwitchScope: AiKillSwitchScope;
  killSwitchReason: string | null;
  /**
   * CSV of specific feature codes to disable without flipping the global
   * kill-switch (e.g. `conversation.evaluation,speaking.grade`). Empty → no
   * per-feature disable.
   */
  disabledFeaturesCsv: string;
  monthlyBudgetUsd: number;
  softWarnPct: number;
  hardKillPct: number;
  currentSpendUsd: number;
  allowByokOnScoringFeatures: boolean;
  allowByokOnNonScoringFeatures: boolean;
  defaultPlatformProviderId: string;
  byokErrorCooldownHours: number;
  byokTransientRetryCount: number;
  anomalyDetectionEnabled: boolean;
  anomalyMultiplierX: number;
  rowVersion: number;
  updatedAt: string;
  updatedByAdminId: string | null;
}

export interface AiProviderRow {
  id: string;
  code: string;
  name: string;
  dialect: AiProviderDialect;
  baseUrl: string;
  apiKeyHint: string;
  defaultModel: string;
  reasoningEffort?: string | null;
  allowedModelsCsv: string;
  pricePer1kPromptTokens: number;
  pricePer1kCompletionTokens: number;
  retryCount: number;
  circuitBreakerThreshold: number;
  circuitBreakerWindowSeconds: number;
  failoverPriority: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface AiUserPolicySnapshot {
  planCode: string;
  planName: string;
  monthlyTokenCap: number;
  dailyTokenCap: number;
  tokensUsedThisMonth: number;
  tokensUsedToday: number;
  requestsThisMonth: number;
  costEstimateUsdThisMonth: number;
  overagePolicy: AiOveragePolicy;
  aiDisabled: boolean;
  killSwitchActive: boolean;
  killSwitchScope: AiKillSwitchScope;
}

export interface AiCredentialItem {
  id: string;
  providerCode: string;
  keyHint: string;
  status: AiCredentialStatus;
  modelAllowlistCsv: string | null;
  createdAt: string;
  lastUsedAt: string | null;
  cooldownUntil: string | null;
}

export interface AiCreditLedgerRow {
  id: string;
  userId: string;
  tokensDelta: number;
  costDeltaUsd: number;
  source: 'PlanRenewal' | 'Promo' | 'Purchase' | 'AdminAdjustment' | 'UsageDebit' | 'Expiration';
  description: string | null;
  referenceId: string | null;
  expiresAt: string | null;
  expiredByEntryId: string | null;
  createdAt: string;
  createdByAdminId: string | null;
}

export interface AiCreditBalance {
  tokensAvailable: number;
  costAvailableUsd: number;
  tokensGrantedLifetime: number;
  tokensConsumedLifetime: number;
}

// ═════════════════════════════════════════════════════════════════════════
// Thin wrapper — mirrors the shape of apiRequest() in ./api without pulling
// in its 4000-line file. Enough for the AI management pages.
// ═════════════════════════════════════════════════════════════════════════

function resolveUrl(path: string): string {
  if (path.startsWith('http')) return path;
  const base = env.apiBaseUrl || '';
  if (base) return `${base.replace(/\/$/, '')}${path}`;
  return path;
}

async function aiApi<T>(path: string, init?: RequestInit): Promise<T> {
  const token = await ensureFreshAccessToken();
  const headers = new Headers(init?.headers);
  headers.set('Accept', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (init?.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  const response = await fetchWithTimeout(resolveUrl(path), { ...init, headers });
  if (!response.ok) {
    let detail: unknown = null;
    try {
      detail = await response.json();
    } catch {
      /* ignore */
    }
    const err = new Error(`HTTP ${response.status}`) as Error & { status?: number; detail?: unknown };
    err.status = response.status;
    err.detail = detail;
    throw err;
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

// ═════════════════════════════════════════════════════════════════════════
// Admin — usage, trend, anomalies
// ═════════════════════════════════════════════════════════════════════════

export function fetchAiUsage(filters: {
  page?: number;
  pageSize?: number;
  userId?: string;
  featureCode?: string;
  providerId?: string;
  outcome?: string;
  periodMonthKey?: string;
  periodDayKey?: string;
}) {
  const qs = new URLSearchParams();
  Object.entries(filters).forEach(([k, v]) => {
    if (v !== undefined && v !== null && v !== '') qs.append(k, String(v));
  });
  return aiApi<AiUsagePage>(`/v1/admin/ai/usage?${qs.toString()}`);
}

export function fetchAiUsageSummary(periodMonthKey?: string, groupBy: 'feature' | 'provider' | 'outcome' | 'user' = 'feature') {
  const qs = new URLSearchParams({ groupBy });
  if (periodMonthKey) qs.append('periodMonthKey', periodMonthKey);
  return aiApi<{ periodMonthKey: string; groupBy: string; rows: AiUsageSummaryRow[] }>(
    `/v1/admin/ai/usage/summary?${qs.toString()}`,
  );
}

export function fetchAiUsageTrend(fromMonth?: string, toMonth?: string) {
  const qs = new URLSearchParams();
  if (fromMonth) qs.append('fromMonth', fromMonth);
  if (toMonth) qs.append('toMonth', toMonth);
  return aiApi<{
    fromMonth: string;
    toMonth: string;
    rows: Array<{ day: string; calls: number; totalTokens: number; successes: number; failures: number; costUsd: number }>;
  }>(`/v1/admin/ai/usage/trend${qs.toString() ? `?${qs}` : ''}`);
}

export function fetchAiAnomalies() {
  return aiApi<{
    enabled: boolean;
    multiplier?: number;
    rows: Array<{ userId: string; tokensToday: number; median7d: number }>;
  }>(`/v1/admin/ai/usage/anomalies`);
}

// ═════════════════════════════════════════════════════════════════════════
// Admin — plans, global policy, user override
// ═════════════════════════════════════════════════════════════════════════

export const fetchAiPlans = () => aiApi<AiQuotaPlan[]>('/v1/admin/ai/plans');
export const createAiPlan = (body: Partial<AiQuotaPlan>) =>
  aiApi<AiQuotaPlan>('/v1/admin/ai/plans', { method: 'POST', body: JSON.stringify(body) });
export const updateAiPlan = (id: string, body: Partial<AiQuotaPlan>) =>
  aiApi<AiQuotaPlan>(`/v1/admin/ai/plans/${id}`, { method: 'PUT', body: JSON.stringify(body) });
export const deactivateAiPlan = (id: string) =>
  aiApi<void>(`/v1/admin/ai/plans/${id}`, { method: 'DELETE' });

export const fetchAiGlobalPolicy = () => aiApi<AiGlobalPolicy>('/v1/admin/ai/global-policy');
export const updateAiGlobalPolicy = (body: Partial<AiGlobalPolicy>) =>
  aiApi<AiGlobalPolicy>('/v1/admin/ai/global-policy', { method: 'PUT', body: JSON.stringify(body) });
export const toggleAiKillSwitch = (enabled: boolean, scope: AiKillSwitchScope, reason?: string) =>
  aiApi<AiGlobalPolicy>('/v1/admin/ai/kill-switch', {
    method: 'POST',
    body: JSON.stringify({ enabled, scope, reason }),
  });

// ═════════════════════════════════════════════════════════════════════════
// Admin — provider registry
// ═════════════════════════════════════════════════════════════════════════

export const fetchAiProviders = () => aiApi<AiProviderRow[]>('/v1/admin/ai/providers');
export const createAiProvider = (body: Record<string, unknown>) =>
  aiApi<AiProviderRow>('/v1/admin/ai/providers', { method: 'POST', body: JSON.stringify(body) });
export const updateAiProvider = (id: string, body: Record<string, unknown>) =>
  aiApi<AiProviderRow>(`/v1/admin/ai/providers/${id}`, { method: 'PUT', body: JSON.stringify(body) });
export const deactivateAiProvider = (id: string) =>
  aiApi<void>(`/v1/admin/ai/providers/${id}`, { method: 'DELETE' });

// ═════════════════════════════════════════════════════════════════════════
// Admin — credit ledger per user
// ═════════════════════════════════════════════════════════════════════════

export const fetchUserCredits = (userId: string) =>
  aiApi<{ balance: AiCreditBalance; entries: AiCreditLedgerRow[] }>(`/v1/admin/ai/users/${userId}/credits`);

export const grantUserCredits = (userId: string, body: {
  tokens: number;
  costUsd: number;
  source: 'promo' | 'purchase' | 'admin';
  description?: string;
  referenceId?: string;
  expiresAt?: string;
}) => aiApi<AiCreditLedgerRow>(`/v1/admin/ai/users/${userId}/credits/grant`, {
  method: 'POST', body: JSON.stringify(body),
});
// ═════════════════════════════════════════════════════════════════════════
// Admin — per-user AI quota / policy overrides
// ═════════════════════════════════════════════════════════════════════════

export interface AiUserQuotaOverride {
  userId: string;
  monthlyTokenCapOverride: number | null;
  dailyTokenCapOverride: number | null;
  forcePlanCode: string | null;
  aiDisabled: boolean;
  reason: string | null;
  expiresAt: string | null;
  grantedByAdminId: string | null;
  createdAt: string;
  updatedAt: string;
}

export const fetchAiUserOverride = (userId: string) =>
  aiApi<AiUserQuotaOverride | null>(`/v1/admin/ai/users/${userId}/override`);

export const upsertAiUserOverride = (userId: string, body: {
  monthlyTokenCapOverride?: number | null;
  dailyTokenCapOverride?: number | null;
  forcePlanCode?: string | null;
  aiDisabled?: boolean;
  reason?: string | null;
  expiresAt?: string | null;
}) => aiApi<AiUserQuotaOverride>(`/v1/admin/ai/users/${userId}/override`, {
  method: 'PUT', body: JSON.stringify(body),
});

export const removeAiUserOverride = (userId: string) =>
  aiApi<void>(`/v1/admin/ai/users/${userId}/override`, { method: 'DELETE' });

// ═════════════════════════════════════════════════════════════════════════
// Learner — credentials, preferences, usage snapshot, credits
// ═════════════════════════════════════════════════════════════════════════

export const fetchMyAiCredentials = () => aiApi<AiCredentialItem[]>('/v1/me/ai/credentials');
export const saveMyAiCredential = (body: { providerCode: string; apiKey: string; modelAllowlistCsv?: string }) =>
  aiApi<{ id: string; keyHint: string; providerCode: string }>('/v1/me/ai/credentials', {
    method: 'POST', body: JSON.stringify(body),
  });
export const revokeMyAiCredential = (id: string) =>
  aiApi<void>(`/v1/me/ai/credentials/${id}`, { method: 'DELETE' });

export const fetchMyAiUsage = () => aiApi<AiUserPolicySnapshot>('/v1/me/ai/usage');
export const fetchMyAiCredits = () =>
  aiApi<{ balance: AiCreditBalance; entries: AiCreditLedgerRow[] }>('/v1/me/ai/credits');

export const fetchMyAiPreferences = () =>
  aiApi<{ userId: string; mode: AiCredentialMode; allowPlatformFallback: boolean; perFeatureOverridesJson: string; updatedAt: string }>(
    '/v1/me/ai/preferences',
  );
export const updateMyAiPreferences = (body: { mode: AiCredentialMode; allowPlatformFallback: boolean; perFeatureOverridesJson?: string }) =>
  aiApi<unknown>('/v1/me/ai/preferences', { method: 'PUT', body: JSON.stringify(body) });
