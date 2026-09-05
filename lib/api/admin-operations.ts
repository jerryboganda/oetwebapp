/**
 * Admin operations: billing subscriptions/invoices, review-ops, analytics,
 * content bulk actions, AI config, flags, freeze — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest } from './client';
import type { FreezePolicy } from '../types/freeze';

export async function adminCreateSubscription(payload: { userId: string; planCode: string; grantIncludedCredits?: boolean; reason?: string }) {
  return apiRequest('/v1/admin/billing/subscriptions', {
    method: 'POST',
    body: JSON.stringify({
      userId: payload.userId,
      planCode: payload.planCode,
      grantIncludedCredits: payload.grantIncludedCredits ?? false,
      reason: payload.reason ?? null,
    }),
  });
}

export async function adminChangeSubscriptionPlan(subscriptionId: string, payload: { planCode: string; resetRenewalDate?: boolean; grantIncludedCredits?: boolean; reason?: string }) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/change-plan`, {
    method: 'POST',
    body: JSON.stringify({
      planCode: payload.planCode,
      resetRenewalDate: payload.resetRenewalDate ?? true,
      grantIncludedCredits: payload.grantIncludedCredits ?? false,
      reason: payload.reason ?? null,
    }),
  });
}

export async function adminExtendSubscription(subscriptionId: string, payload: { addDays?: number; addMonths?: number; newRenewalAt?: string; reason?: string }) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/extend`, {
    method: 'POST',
    body: JSON.stringify({
      addDays: payload.addDays ?? null,
      addMonths: payload.addMonths ?? null,
      newRenewalAt: payload.newRenewalAt ?? null,
      reason: payload.reason ?? null,
    }),
  });
}

export async function adminCancelSubscription(subscriptionId: string, payload: { immediate?: boolean; reason?: string }) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/cancel`, {
    method: 'POST',
    body: JSON.stringify({
      immediate: payload.immediate ?? false,
      reason: payload.reason ?? null,
    }),
  });
}

export async function adminReactivateSubscription(subscriptionId: string, payload: { resetRenewalDate?: boolean; reason?: string } = {}) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/reactivate`, {
    method: 'POST',
    body: JSON.stringify({
      resetRenewalDate: payload.resetRenewalDate ?? true,
      reason: payload.reason ?? null,
    }),
  });
}

export async function adminSetSubscriptionStatus(subscriptionId: string, payload: { status: string; reason?: string }) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/status`, {
    method: 'POST',
    body: JSON.stringify({
      status: payload.status,
      reason: payload.reason ?? null,
    }),
  });
}

export async function adminApproveSubscriptionFreeze(subscriptionId: string, payload: { reason?: string; internalNotes?: string } = {}) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/approve-freeze`, {
    method: 'POST',
    body: JSON.stringify({
      reason: payload.reason ?? null,
      internalNotes: payload.internalNotes ?? null,
    }),
  });
}

export async function adminRejectSubscriptionFreeze(subscriptionId: string, payload: { reason?: string; internalNotes?: string } = {}) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/reject-freeze`, {
    method: 'POST',
    body: JSON.stringify({
      reason: payload.reason ?? null,
      internalNotes: payload.internalNotes ?? null,
    }),
  });
}

export async function adminFreezeSubscription(subscriptionId: string, payload: { reason?: string; internalNotes?: string } = {}) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/freeze`, {
    method: 'POST',
    body: JSON.stringify({
      reason: payload.reason ?? null,
      internalNotes: payload.internalNotes ?? null,
    }),
  });
}

export async function adminResumeSubscription(subscriptionId: string, payload: { reason?: string; internalNotes?: string } = {}) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/resume`, {
    method: 'POST',
    body: JSON.stringify({
      reason: payload.reason ?? null,
      internalNotes: payload.internalNotes ?? null,
    }),
  });
}

export async function fetchAdminBillingEntitlementDiagnostics() {
  return apiRequest('/v1/admin/billing/entitlement-diagnostics');
}

export async function fetchAdminBillingCouponRedemptions(params?: { couponCode?: string; userId?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.couponCode) qs.set('couponCode', params.couponCode);
  if (params?.userId) qs.set('userId', params.userId);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/billing/redemptions${q ? `?${q}` : ''}`);
}

export async function fetchAdminBillingInvoices(params?: { status?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/billing/invoices${q ? `?${q}` : ''}`);
}

export async function fetchAdminBillingInvoiceEvidence(invoiceId: string) {
  return apiRequest(`/v1/admin/billing/invoices/${encodeURIComponent(invoiceId)}/evidence`);
}

export async function fetchAdminBillingPaymentTransactions(params?: { status?: string; gateway?: string; transactionType?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.gateway) qs.set('gateway', params.gateway);
  if (params?.transactionType) qs.set('transactionType', params.transactionType);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/billing/payment-transactions${q ? `?${q}` : ''}`);
}

export async function fetchAdminBillingProviderLifecycleSignals(params?: { gateway?: string; category?: string; processingStatus?: string; verificationStatus?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.gateway) qs.set('gateway', params.gateway);
  if (params?.category) qs.set('category', params.category);
  if (params?.processingStatus) qs.set('processingStatus', params.processingStatus);
  if (params?.verificationStatus) qs.set('verificationStatus', params.verificationStatus);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/billing/provider-lifecycle-signals${q ? `?${q}` : ''}`);
}

export async function fetchAdminReviewOpsSummary() {
  return apiRequest('/v1/admin/review-ops/summary');
}

export async function fetchAdminReviewOpsQueue(params?: { status?: string; priority?: string }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.priority) qs.set('priority', params.priority);
  const q = qs.toString();
  return apiRequest(`/v1/admin/review-ops/queue${q ? `?${q}` : ''}`);
}

export async function assignAdminReview(reviewRequestId: string, payload: { expertId: string; reason?: string }) {
  return apiRequest(`/v1/admin/review-ops/${encodeURIComponent(reviewRequestId)}/assign`, { method: 'POST', body: JSON.stringify(payload) });
}

export async function fetchAdminQualityAnalytics(params?: { timeRange?: string; subtest?: string; profession?: string }) {
  const qs = new URLSearchParams();
  if (params?.timeRange) qs.set('timeRange', params.timeRange);
  if (params?.subtest) qs.set('subtest', params.subtest);
  if (params?.profession) qs.set('profession', params.profession);
  const q = qs.toString();
  return apiRequest(`/v1/admin/quality-analytics${q ? `?${q}` : ''}`);
}

export async function fetchAdminCohortAnalysis(params?: { groupBy?: string }) {
  const qs = params?.groupBy ? `?groupBy=${encodeURIComponent(params.groupBy)}` : '';
  return apiRequest(`/v1/admin/analytics/cohort${qs}`);
}

export async function fetchAdminContentEffectiveness(params?: { subtestCode?: string; top?: number }) {
  const qs = new URLSearchParams();
  if (params?.subtestCode) qs.set('subtestCode', params.subtestCode);
  if (params?.top) qs.set('top', String(params.top));
  const q = qs.toString();
  return apiRequest(`/v1/admin/analytics/content-effectiveness${q ? `?${q}` : ''}`);
}

export async function fetchAdminExpertEfficiency(params?: { days?: number }) {
  const qs = params?.days ? `?days=${encodeURIComponent(String(params.days))}` : '';
  return apiRequest(`/v1/admin/analytics/expert-efficiency${qs}`);
}

export async function fetchAdminSubscriptionHealth() {
  return apiRequest('/v1/admin/analytics/subscription-health');
}

export async function bulkAdminContentAction(payload: { action: string; contentIds: string[]; dryRun?: boolean }) {
  return apiRequest('/v1/admin/content/bulk-action', { method: 'POST', body: JSON.stringify(payload) });
}

export async function fetchAdminContentImpact(contentId: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/impact`);
}

export async function fetchAdminTaxonomyImpact(professionId: string) {
  return apiRequest(`/v1/admin/taxonomy/${encodeURIComponent(professionId)}/impact`);
}

export async function activateAdminAIConfig(configId: string) {
  return apiRequest(`/v1/admin/ai-config/${encodeURIComponent(configId)}/activate`, { method: 'POST' });
}

export async function deleteAdminAIConfig(configId: string) {
  return apiRequest(`/v1/admin/ai-config/${encodeURIComponent(configId)}`, { method: 'DELETE' });
}

export async function activateAdminFlag(flagId: string) {
  return apiRequest(`/v1/admin/flags/${encodeURIComponent(flagId)}/activate`, { method: 'POST' });
}

export async function deactivateAdminFlag(flagId: string) {
  return apiRequest(`/v1/admin/flags/${encodeURIComponent(flagId)}/deactivate`, { method: 'POST' });
}

export async function cancelAdminReview(reviewRequestId: string, payload: { reason: string }) {
  return apiRequest(`/v1/admin/review-ops/${encodeURIComponent(reviewRequestId)}/cancel`, { method: 'POST', body: JSON.stringify(payload) });
}

export async function reopenAdminReview(reviewRequestId: string, payload?: { reason?: string }) {
  return apiRequest(`/v1/admin/review-ops/${encodeURIComponent(reviewRequestId)}/reopen`, { method: 'POST', body: JSON.stringify(payload ?? {}) });
}

export async function fetchAdminReviewFailures() {
  return apiRequest('/v1/admin/review-ops/failures');
}

export async function fetchAdminAuditLogDetail(eventId: string) {
  return apiRequest(`/v1/admin/audit-logs/${encodeURIComponent(eventId)}`);
}

export async function fetchFreezeStatus() {
  return apiRequest('/v1/freeze');
}

export async function requestFreeze(payload: {
  startAt?: string | null;
  endAt?: string | null;
  reason?: string | null;
  pauseEntitlementClock?: boolean | null;
}) {
  return apiRequest('/v1/freeze/request', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function confirmFreeze(freezeId: string) {
  return apiRequest(`/v1/freeze/${encodeURIComponent(freezeId)}/confirm`, { method: 'POST' });
}

export async function cancelFreeze(freezeId: string) {
  return apiRequest(`/v1/freeze/${encodeURIComponent(freezeId)}/cancel`, { method: 'POST' });
}

export async function fetchAdminFreezeOverview() {
  return apiRequest('/v1/admin/freeze/overview');
}

export async function updateAdminFreezePolicy(payload: FreezePolicy) {
  return apiRequest('/v1/admin/freeze/policy', {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function createAdminManualFreeze(payload: {
  userId: string;
  startAt?: string | null;
  endAt?: string | null;
  reason?: string | null;
  internalNotes?: string | null;
  pauseEntitlementClock?: boolean | null;
  overrideEligibility?: boolean | null;
}) {
  return apiRequest('/v1/admin/freeze/manual', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function approveAdminFreeze(freezeId: string, payload: { reason?: string | null; internalNotes?: string | null }) {
  return apiRequest(`/v1/admin/freeze/${encodeURIComponent(freezeId)}/approve`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function rejectAdminFreeze(freezeId: string, payload: { reason?: string | null; internalNotes?: string | null }) {
  return apiRequest(`/v1/admin/freeze/${encodeURIComponent(freezeId)}/reject`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function endAdminFreeze(freezeId: string, payload: { reason?: string | null; internalNotes?: string | null }) {
  return apiRequest(`/v1/admin/freeze/${encodeURIComponent(freezeId)}/end`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function forceEndAdminFreeze(freezeId: string, payload: { reason?: string | null; internalNotes?: string | null }) {
  return apiRequest(`/v1/admin/freeze/${encodeURIComponent(freezeId)}/force-end`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}
