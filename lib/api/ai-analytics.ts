/**
 * Typed API client for AI churn / usage forecast / usage analytics / FX / pricing experiments.
 */
import { apiClient } from '@/lib/api';

// ── Usage analytics ───────────────────────────────────────────────

export interface FeatureBreakdownDto {
  featureCode: string;
  calls: number;
  totalTokens: number;
  costUsd: number;
}

export interface ProviderBreakdownDto {
  provider: string;
  calls: number;
  totalTokens: number;
  costUsd: number;
  successRate: number;
  avgLatencyMs: number;
}

export interface DailyBucketDto {
  day: string;
  calls: number;
  totalTokens: number;
  costUsd: number;
}

export interface TopUserUsageDto {
  userId: string;
  calls: number;
  totalTokens: number;
  costUsd: number;
}

export interface LearnerUsageSummaryDto {
  from: string;
  to: string;
  totalCalls: number;
  totalTokens: number;
  totalCostUsd: number;
  failedCalls: number;
  creditsUsed: number;
  walletBalance: number;
  byFeature: FeatureBreakdownDto[];
  daily: DailyBucketDto[];
  forecastCalls30d: number;
  forecastCredits30d: number;
  forecastCostUsd30d: number;
  suggestedTopUpCredits: number;
}

export interface AdminUsageSummaryDto {
  from: string;
  to: string;
  totalCalls: number;
  totalTokens: number;
  totalCostUsd: number;
  uniqueUsers: number;
  successRate: number;
  avgLatencyMs: number;
  byFeature: FeatureBreakdownDto[];
  byProvider: ProviderBreakdownDto[];
  daily: DailyBucketDto[];
  topUsers: TopUserUsageDto[];
}

export function fetchLearnerAiUsage(from?: string, to?: string): Promise<LearnerUsageSummaryDto> {
  const q = new URLSearchParams();
  if (from) q.set('from', from);
  if (to) q.set('to', to);
  const qs = q.toString();
  return apiClient.get(`/v1/ai-usage/me${qs ? `?${qs}` : ''}`);
}

export function fetchAdminAiUsage(params: { from?: string; to?: string; feature?: string; provider?: string }): Promise<AdminUsageSummaryDto> {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v) q.set(k, v);
  }
  const qs = q.toString();
  return apiClient.get(`/v1/admin/ai-analytics/summary${qs ? `?${qs}` : ''}`);
}

// ── Churn ─────────────────────────────────────────────────────────

export interface ChurnRiskSnapshotDto {
  id: string;
  userId: string;
  snapshotDate: string;
  riskScore: number;
  riskBand: 'low' | 'medium' | 'high' | string;
  factorsJson: string;
  recommendedAction: string | null;
  actionDispatched: boolean;
  computedAt: string;
}

export function fetchMyChurnRisk(): Promise<ChurnRiskSnapshotDto> {
  return apiClient.get('/v1/ai-usage/me/churn-risk');
}

export function fetchAdminChurnList(band?: string, limit?: number): Promise<ChurnRiskSnapshotDto[]> {
  const q = new URLSearchParams();
  if (band) q.set('band', band);
  if (limit) q.set('limit', String(limit));
  const qs = q.toString();
  return apiClient.get(`/v1/admin/ai-analytics/churn${qs ? `?${qs}` : ''}`);
}

export function recomputeAdminChurn(): Promise<string> {
  return apiClient.post('/v1/admin/ai-analytics/churn/recompute', {});
}

// ── Forecast ──────────────────────────────────────────────────────

export interface UsageForecastSnapshotDto {
  id: string;
  userId: string;
  snapshotDate: string;
  featureCode: string;
  windowDays: number;
  forecastCalls: number;
  forecastCredits: number;
  forecastCostUsd: number;
  ema30DailyCalls: number;
  perFeatureJson: string | null;
  suggestedTopUpCredits: number;
  computedAt: string;
}

export function fetchMyForecast(windowDays?: number): Promise<UsageForecastSnapshotDto> {
  const qs = windowDays ? `?windowDays=${windowDays}` : '';
  return apiClient.get(`/v1/ai-usage/me/forecast${qs}`);
}

export function fetchAdminForecastList(limit?: number): Promise<UsageForecastSnapshotDto[]> {
  const qs = limit ? `?limit=${limit}` : '';
  return apiClient.get(`/v1/admin/ai-analytics/forecast${qs}`);
}

export function recomputeAdminForecast(): Promise<string> {
  return apiClient.post('/v1/admin/ai-analytics/forecast/recompute', {});
}

// ── FX rates ──────────────────────────────────────────────────────

export interface ExchangeRateDto {
  id: string;
  fromCurrency: string;
  toCurrency: string;
  rate: number;
  effectiveFrom: string;
  source: string;
  createdAt: string;
}

export function fetchFxRates(from?: string, to?: string): Promise<ExchangeRateDto[]> {
  const q = new URLSearchParams();
  if (from) q.set('from', from);
  if (to) q.set('to', to);
  const qs = q.toString();
  return apiClient.get(`/v1/admin/fx/rates${qs ? `?${qs}` : ''}`);
}

export function refreshFxRates(): Promise<string> {
  return apiClient.post('/v1/admin/fx/refresh', {});
}

// ── Pricing experiments ──────────────────────────────────────────

export interface PricingExperimentDto {
  id: string;
  name: string;
  code: string;
  targetType: string;
  targetId: string;
  region: string;
  status: 'draft' | 'running' | 'paused' | 'completed' | string;
  rolloutPercent: number;
  variantsJson: string;
  startedAt: string | null;
  endedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface PricingExperimentUpsertRequest {
  code: string;
  name: string;
  targetType: string;
  targetId: string;
  region?: string | null;
  rolloutPercent: number;
  variantsJson?: string | null;
}

export interface VariantResultsDto {
  variantCode: string;
  assignments: number;
  conversions: number;
  conversionRevenue: number;
}

export interface ExperimentResultsResponseDto {
  experimentId: string;
  variants: VariantResultsDto[];
}

export function listPricingExperiments(status?: string): Promise<PricingExperimentDto[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : '';
  return apiClient.get(`/v1/admin/pricing-experiments/${qs}`);
}

export function upsertPricingExperiment(payload: PricingExperimentUpsertRequest): Promise<PricingExperimentDto> {
  return apiClient.post('/v1/admin/pricing-experiments/', payload);
}

export function startPricingExperiment(id: string): Promise<PricingExperimentDto> {
  return apiClient.post(`/v1/admin/pricing-experiments/${id}/start`, {});
}

export function stopPricingExperiment(id: string): Promise<PricingExperimentDto> {
  return apiClient.post(`/v1/admin/pricing-experiments/${id}/stop`, {});
}

export function fetchExperimentResults(id: string): Promise<ExperimentResultsResponseDto> {
  return apiClient.get(`/v1/admin/pricing-experiments/${id}/results`);
}

export interface ZTestResultDto {
  controlCode: string;
  variantCode: string;
  controlRate: number;
  variantRate: number;
  difference: number;
  z: number;
  pValueTwoTailed: number;
  ciLower95: number;
  ciUpper95: number;
  significantAt95: boolean;
}

export function fetchExperimentSignificance(id: string): Promise<ZTestResultDto[]> {
  return apiClient.get(`/v1/admin/pricing-experiments/${id}/significance`);
}

export function deletePricingExperiment(id: string): Promise<void> {
  return apiClient.delete(`/v1/admin/pricing-experiments/${id}`);
}
