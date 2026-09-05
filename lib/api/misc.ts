/**
 * Orphan endpoints + sponsor dashboard — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest, type ApiRecord } from './client';

export async function fetchStudyPlanDrift() {
  return apiRequest('/v1/learner/study-plan/drift');
}

export async function regenerateStudyPlan(): Promise<ApiRecord> {
  return apiRequest<ApiRecord>('/v1/study-plan/regenerate', { method: 'POST' });
}

export async function fetchReadinessRisk() {
  return apiRequest('/v1/learner/readiness/risk');
}

export async function applyStreakFreeze(): Promise<{ applied: boolean; message: string }> {
  return apiRequest('/v1/learner/engagement/streak-freeze', { method: 'POST' });
}

export async function fetchFluencyTimeline(attemptId: string) {
  return apiRequest(`/v1/learner/speaking/${encodeURIComponent(attemptId)}/fluency-timeline`);
}

export async function fetchDiagnosticPersonalization() {
  return apiRequest('/v1/learner/diagnostic-personalization');
}

export interface SponsorDashboardData {
  sponsorName: string;
  organizationName: string | null;
  learnersSponsored: number;
  activeSponsorships: number;
  pendingSponsorships: number;
  totalSpend: number;
  currency: string | null;
}

export interface SponsoredLearner {
  id: string;
  learnerEmail: string;
  learnerUserId: string | null;
  status: string;
  createdAt: string;
  revokedAt: string | null;
}

export interface SponsorInvoice {
  id: string;
  sponsorshipId: string;
  learnerUserId: string;
  learnerEmail: string;
  gateway: string;
  gatewayTransactionId: string;
  transactionType: string;
  productType: string | null;
  productId: string | null;
  amount: number;
  currency: string;
  status: string;
  createdAt: string;
}

export interface SponsorBillingData {
  sponsorName: string;
  organizationName: string | null;
  totalSponsorships: number;
  totalSpend: number;
  currentMonthSpend: number;
  currency: string | null;
  billingCycle: string;
  invoices: SponsorInvoice[];
}

export async function fetchSponsorDashboard(): Promise<SponsorDashboardData> {
  return apiRequest<SponsorDashboardData>('/v1/sponsor/dashboard');
}

export async function fetchSponsoredLearners(params?: { page?: number; pageSize?: number }): Promise<{ items: SponsoredLearner[]; total: number; page: number; pageSize: number }> {
  const queryParams = new URLSearchParams();
  if (params?.page) queryParams.set('page', String(params.page));
  if (params?.pageSize) queryParams.set('pageSize', String(params.pageSize));
  const qs = queryParams.toString();
  return apiRequest(`/v1/sponsor/learners${qs ? `?${qs}` : ''}`);
}

export async function inviteSponsoredLearner(email: string): Promise<SponsoredLearner> {
  return apiRequest<SponsoredLearner>('/v1/sponsor/learners/invite', {
    method: 'POST',
    body: JSON.stringify({ email }),
  });
}

export async function removeSponsoredLearner(id: string): Promise<{ revoked: boolean }> {
  return apiRequest(`/v1/sponsor/learners/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  });
}

export async function fetchSponsorBilling(): Promise<SponsorBillingData> {
  return apiRequest<SponsorBillingData>('/v1/sponsor/billing');
}
