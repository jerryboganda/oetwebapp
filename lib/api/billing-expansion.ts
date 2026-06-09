/**
 * Typed API client for the Phase 4-10 billing endpoints exposed by
 * BillingExpansionEndpoints.cs:
 *  - manual payments (learner + admin)
 *  - scholarships (admin)
 *  - affiliates (admin)
 *  - dunning campaigns (admin, read-only)
 *  - metrics (admin)
 */
import { apiClient } from '@/lib/api';

// ── Manual payments ───────────────────────────────────────────────

export interface ManualPaymentDto {
  id: string;
  userId: string;
  candidateFullName: string;
  candidateEmail: string;
  candidateWhatsApp: string;
  courseName: string;
  courseId: string | null;
  paymentCategory: 'inside_egypt' | 'international' | string;
  amountAmount: number;
  currency: string;
  method: string;
  reference: string;
  proofUrl: string;
  status: 'pending' | 'needs_review' | 'approved' | 'paid' | 'rejected' | 'cancelled' | string;
  submittedAt: string;
  reviewedAt: string | null;
  adminNotes: string | null;
  accessGrantedSubscriptionId?: string | null;
}

export interface ManualPaymentSubmitRequest {
  quoteId?: string | null;
  amountAmount: number;
  currency: string;
  method: string;
  reference: string;
  proofUrl: string;
  candidateFullName: string;
  candidateEmail: string;
  candidateWhatsApp: string;
  courseName: string;
  courseId?: string | null;
  paymentCategory: 'inside_egypt' | 'international' | string;
  /** Base-64 encoded proof file. The backend SHA-256 hashes this for duplicate detection. */
  proofBase64: string;
}

export function submitManualPayment(payload: ManualPaymentSubmitRequest): Promise<ManualPaymentDto> {
  return apiClient.post<ManualPaymentDto>('/v1/billing/manual-payments', payload);
}

export function listOwnManualPayments(): Promise<ManualPaymentDto[]> {
  return apiClient.get<ManualPaymentDto[]>('/v1/billing/manual-payments/mine');
}

export function listAdminManualPayments(status?: string): Promise<ManualPaymentDto[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : '';
  return apiClient.get<ManualPaymentDto[]>(`/v1/admin/billing/manual-payments/${qs}`);
}

export function approveManualPayment(id: string, notes?: string): Promise<ManualPaymentDto> {
  return apiClient.post<ManualPaymentDto>(`/v1/admin/billing/manual-payments/${encodeURIComponent(id)}/approve`, { notes });
}

export function rejectManualPayment(id: string, notes?: string): Promise<ManualPaymentDto> {
  return apiClient.post<ManualPaymentDto>(`/v1/admin/billing/manual-payments/${encodeURIComponent(id)}/reject`, { notes });
}

// ── Scholarships ──────────────────────────────────────────────────

export interface ScholarshipDto {
  id: string;
  userId: string;
  grantedByAdminId: string;
  reason: string;
  accessTier: string;
  entitlementsJson: string;
  grantedAt: string;
  expiresAt: string | null;
  revokedAt: string | null;
  revokedByAdminId: string | null;
  adminNotes: string | null;
  status: 'pending' | 'active' | 'revoked' | 'expired' | string;
}

export interface ScholarshipGrantRequest {
  userId: string;
  reason: string;
  accessTier: string;
  entitlementsJson?: string | null;
  expiresAt?: string | null;
  adminNotes?: string | null;
}

export function listScholarships(status?: string): Promise<ScholarshipDto[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : '';
  return apiClient.get<ScholarshipDto[]>(`/v1/admin/billing/scholarships/${qs}`);
}

export function grantScholarship(payload: ScholarshipGrantRequest): Promise<ScholarshipDto> {
  return apiClient.post<ScholarshipDto>('/v1/admin/billing/scholarships/', payload);
}

export function revokeScholarship(id: string): Promise<ScholarshipDto> {
  return apiClient.post<ScholarshipDto>(`/v1/admin/billing/scholarships/${encodeURIComponent(id)}/revoke`, {});
}

// ── Affiliates ────────────────────────────────────────────────────

export interface AffiliateDto {
  id: string;
  code: string;
  ownerName: string;
  contactEmail: string;
  commissionPercent: number;
  cookieDays: number;
  payoutThresholdAmount: number;
  payoutCurrency: string;
  payoutMethod: string;
  status: 'active' | 'paused' | 'terminated' | string;
  createdAt: string;
  updatedAt: string;
}

export interface AffiliateUpsertRequest {
  code: string;
  ownerName: string;
  contactEmail: string;
  commissionPercent: number;
  cookieDays?: number | null;
  payoutThresholdAmount: number;
  payoutCurrency?: string | null;
  payoutMethod?: string | null;
  status?: string | null;
}

export function listAffiliates(): Promise<AffiliateDto[]> {
  return apiClient.get<AffiliateDto[]>('/v1/admin/billing/affiliates/');
}

export function createAffiliate(payload: AffiliateUpsertRequest): Promise<AffiliateDto> {
  return apiClient.post<AffiliateDto>('/v1/admin/billing/affiliates/', payload);
}

export function updateAffiliate(id: string, payload: AffiliateUpsertRequest): Promise<AffiliateDto> {
  return apiClient.put<AffiliateDto>(`/v1/admin/billing/affiliates/${encodeURIComponent(id)}`, payload);
}

// ── Dunning + metrics (read-only) ─────────────────────────────────

export interface DunningCampaignDto {
  id: string;
  subscriptionId: string;
  userId: string;
  status: string;
  startedAt: string;
  nextAttemptAt: string;
  attemptCount: number;
  lastFailureCode: string | null;
  lastFailureReason: string | null;
  stepsCompletedCsv: string;
}

export function listDunningCampaigns(status?: string): Promise<DunningCampaignDto[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : '';
  return apiClient.get<DunningCampaignDto[]>(`/v1/admin/billing/dunning/${qs}`);
}

export interface BillingMetricDto {
  id: string;
  metricDate: string;
  metricCode: string;
  region: string;
  currency: string;
  value: number;
  detailsJson: string | null;
  computedAt: string;
}

export function readBillingMetrics(params: { from: string; to: string; code?: string; region?: string }): Promise<BillingMetricDto[]> {
  const q = new URLSearchParams({ from: params.from, to: params.to });
  if (params.code) q.set('code', params.code);
  if (params.region) q.set('region', params.region);
  return apiClient.get<BillingMetricDto[]>(`/v1/admin/billing/metrics/?${q.toString()}`);
}

export function rollupBillingMetrics(date?: string): Promise<string> {
  const qs = date ? `?date=${encodeURIComponent(date)}` : '';
  return apiClient.post<string>(`/v1/admin/billing/metrics/rollup${qs}`, {});
}
