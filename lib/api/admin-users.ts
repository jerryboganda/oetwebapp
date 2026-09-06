/**
 * Admin sponsors, users, billing plans/add-ons, wallet tiers — extracted
 * from `lib/api.ts`. Re-exported there, so `@/lib/api` imports keep working.
 */
import {
  ApiError,
  apiRequest,
  getHeaders,
  maybe,
  normalizeBillingCode,
  resolveApiUrl,
} from './client';
import { fetchWithTimeout } from '../network/fetch-with-timeout';
import { ensureFreshAccessToken } from '../auth-client';

export interface AdminSponsorDto {
  id: string;
  name: string;
  type: string;
  contactEmail: string;
  organizationName?: string;
  status: string;
  createdAt: string;
  learnerCount?: number;
}

export async function fetchAdminSponsors(params?: { status?: string; search?: string; page?: number; pageSize?: number }): Promise<{ items: AdminSponsorDto[]; total?: number; page?: number; pageSize?: number }> {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/sponsors${q ? `?${q}` : ''}`);
}

export async function fetchAdminUsers(params?: { role?: string; status?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.role) qs.set('role', params.role);
  if (params?.status) qs.set('status', params.status);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/users${q ? `?${q}` : ''}`);
}

export async function fetchAdminUserDetail(userId: string) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}`);
}

export async function inviteAdminUser(payload: { name: string; email: string; role: 'learner' | 'expert' | 'admin'; professionId?: string; specialties?: string[] }): Promise<{
  id: string; email: string; role: string;
  temporaryPassword?: string | null;
  invitation?: { purpose: string; deliveryChannel: string; destinationHint: string; expiresAt: string; retryAfterSeconds: number } | null;
}> {
  return apiRequest('/v1/admin/users/invite', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminUserStatus(userId: string, payload: { status: string; reason?: string }) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/status`, { method: 'PUT', body: JSON.stringify(payload) });
}

export interface AdminUserProfileUpdatePayload {
  displayName?: string;
  firstName?: string;
  lastName?: string;
  mobileNumber?: string;
  professionId?: string;
  examTypeId?: string;
  countryTarget?: string;
  timezone?: string;
  locale?: string;
  marketingOptIn?: boolean;
  agreeToTerms?: boolean;
  agreeToPrivacy?: boolean;
  specialties?: string[];
  reason?: string;
}

export async function updateAdminUserProfile(userId: string, payload: AdminUserProfileUpdatePayload) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/profile`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function deleteAdminUser(userId: string, payload?: { reason?: string }) {
  return apiRequest<{ id: string; userId: string; status: string; purgedRows: number; tables: number; detail: Record<string, number> }>(
    `/v1/admin/users/${encodeURIComponent(userId)}/delete`,
    { method: 'POST', body: JSON.stringify(payload ?? {}) },
  );
}

export async function restoreAdminUser(userId: string, payload?: { reason?: string }) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/restore`, { method: 'POST', body: JSON.stringify(payload ?? {}) });
}

/**
 * IRREVERSIBLE: permanently purges the user and every row referencing them across
 * the whole schema — including invoices, payments and audit records. system_admin only.
 */
export async function hardDeleteAdminUser(
  userId: string,
  payload?: { reason?: string },
): Promise<{ id: string; userId: string; status: string; purgedRows: number; tables: number; detail: Record<string, number> }> {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/hard-delete`, {
    method: 'POST',
    body: JSON.stringify(payload ?? {}),
  });
}

export async function adjustAdminUserCredits(userId: string, payload: { amount: number; reason?: string }) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/credits`, { method: 'POST', body: JSON.stringify(payload) });
}

export async function setAdminUserPassword(
  userId: string,
  payload: { password: string },
): Promise<{ userId: string; email: string; revoked: number }> {
  return apiRequest<{ userId: string; email: string; revoked: number }>(`/v1/admin/users/${encodeURIComponent(userId)}/password`, { method: 'POST', body: JSON.stringify(payload) });
}

export async function triggerAdminUserPasswordReset(userId: string) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/password-reset`, { method: 'POST' });
}

export async function verifyAdminUserEmail(
  userId: string,
): Promise<{ userId: string; email: string; alreadyVerified: boolean; emailVerifiedAt: string | null; revokedSessions: number }> {
  return apiRequest<{
    userId: string;
    email: string;
    alreadyVerified: boolean;
    emailVerifiedAt: string | null;
    revokedSessions: number;
  }>(`/v1/admin/users/${encodeURIComponent(userId)}/verify-email`, { method: 'POST' });
}

export async function revokeAdminUserSessions(userId: string): Promise<{ id: string; revoked: number }> {
  return apiRequest<{ id: string; revoked: number }>(`/v1/admin/users/${encodeURIComponent(userId)}/sessions/revoke`, { method: 'POST' });
}

export async function unlockAdminUser(userId: string) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/unlock`, { method: 'POST' });
}

export async function resendAdminUserInvite(userId: string) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/resend-invite`, { method: 'POST' });
}

export async function bulkImportUsers(file: File) {
  const form = new FormData();
  form.append('file', file);
  const token = await ensureFreshAccessToken();
  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  const response = await fetchWithTimeout(resolveApiUrl('/v1/admin/users/import'), {
    method: 'POST',
    headers,
    body: form,
  }, 120_000);
  if (!response.ok) {
    let code = 'unknown_error';
    let message = `Import failed: ${response.status}`;
    try {
      const error = await response.json();
      code = error.code ?? code;
      message = error.message ?? error.title ?? message;
    } catch { /* ignore parse error */ }
    throw new ApiError(response.status, code, message, false);
  }
  return response.json();
}

export async function fetchAdminBillingPlans(params?: { status?: string }) {
  const qs = params?.status ? `?status=${encodeURIComponent(params.status)}` : '';
  return apiRequest(`/v1/admin/billing/plans${qs}`);
}

export async function fetchAdminBillingPlanVersions(planId: string) {
  return apiRequest(`/v1/admin/billing/plans/${encodeURIComponent(planId)}/versions`);
}

export interface AdminWalletTierRow {
  id: string | null;
  amount: number;
  credits: number;
  bonus: number;
  totalCredits: number;
  label: string | null;
  isPopular: boolean;
  displayOrder: number;
  isActive: boolean;
  currency: string;
}

export interface AdminWalletTiersResponse {
  source: 'database' | 'appsettings';
  currency: string;
  tiers: AdminWalletTierRow[];
}

export interface AdminWalletTierInput {
  id?: string | null;
  amount: number;
  credits: number;
  bonus: number;
  label?: string | null;
  isPopular: boolean;
  displayOrder: number;
  isActive: boolean;
  currency?: string | null;
}

export async function fetchAdminWalletTiers(): Promise<AdminWalletTiersResponse> {
  return apiRequest<AdminWalletTiersResponse>('/v1/admin/billing/wallet-tiers');
}

export async function replaceAdminWalletTiers(tiers: AdminWalletTierInput[]): Promise<AdminWalletTiersResponse> {
  return apiRequest<AdminWalletTiersResponse>('/v1/admin/billing/wallet-tiers', {
    method: 'PUT',
    body: JSON.stringify({ tiers }),
  });
}

export interface AdminBillingPlanOet2026Fields {
  originalPriceGbp?: number | null;
  accessDurationDays?: number;
  writingAddonsEnabled?: boolean;
  speakingAddonsEnabled?: boolean;
  speakingPracticeAccessEnabled?: boolean;
  tutorBookDiscountEnabled?: boolean;
  profession?: string;
  productCategory?: string;
  dashboardModulesJson?: string;
  bundledWritingAssessments?: number;
  bundledSpeakingSessions?: number;
  bundledAiCredits?: number;
  bundledTutorBook?: boolean;
  bundledBasicEnglish?: boolean;
  isDraft?: boolean;
  extensionAllowed?: boolean;
  recallUpdatesEnabled?: boolean;
  // "What's included" bullet list — persisted on the linked ContentPackage.
  comparisonFeaturesJson?: string;
  // ── Delivery + content scoping (access & payment spec 2026-07-15) ──
  deliveryMethod?: string;
  telegramInviteUrl?: string;
  deliveryInstructions?: string;
  contentOverridesJson?: string;
}

export async function createAdminBillingPlan(payload: {
  code?: string;
  name: string;
  description?: string;
  price: number;
  currency?: string;
  interval: string;
  durationMonths?: number;
  includedCredits?: number;
  displayOrder?: number;
  isVisible?: boolean;
  isRenewable?: boolean;
  trialDays?: number;
  status?: string;
  includedSubtestsJson?: string;
  entitlementsJson?: string;
} & AdminBillingPlanOet2026Fields) {
  return apiRequest('/v1/admin/billing/plans', {
    method: 'POST',
    body: JSON.stringify({
      code: payload.code ?? normalizeBillingCode(payload.name),
      name: payload.name,
      description: payload.description ?? '',
      price: payload.price,
      currency: payload.currency ?? 'AUD',
      interval: payload.interval,
      durationMonths: payload.durationMonths ?? 1,
      includedCredits: payload.includedCredits ?? 0,
      displayOrder: payload.displayOrder ?? 0,
      isVisible: payload.isVisible ?? true,
      isRenewable: payload.isRenewable ?? true,
      trialDays: payload.trialDays ?? 0,
      status: payload.status ?? 'active',
      includedSubtestsJson: payload.includedSubtestsJson ?? '[]',
      entitlementsJson: payload.entitlementsJson ?? '{}',
      originalPriceGbp: payload.originalPriceGbp ?? null,
      accessDurationDays: payload.accessDurationDays ?? null,
      writingAddonsEnabled: payload.writingAddonsEnabled ?? null,
      speakingAddonsEnabled: payload.speakingAddonsEnabled ?? null,
      speakingPracticeAccessEnabled: payload.speakingPracticeAccessEnabled ?? null,
      tutorBookDiscountEnabled: payload.tutorBookDiscountEnabled ?? null,
      profession: payload.profession ?? null,
      productCategory: payload.productCategory ?? null,
      dashboardModulesJson: payload.dashboardModulesJson ?? null,
      bundledWritingAssessments: payload.bundledWritingAssessments ?? null,
      bundledSpeakingSessions: payload.bundledSpeakingSessions ?? null,
      bundledAiCredits: payload.bundledAiCredits ?? null,
      bundledTutorBook: payload.bundledTutorBook ?? null,
      bundledBasicEnglish: payload.bundledBasicEnglish ?? null,
      isDraft: payload.isDraft ?? null,
      extensionAllowed: payload.extensionAllowed ?? null,
      recallUpdatesEnabled: payload.recallUpdatesEnabled ?? null,
      comparisonFeaturesJson: payload.comparisonFeaturesJson ?? null,
      deliveryMethod: payload.deliveryMethod ?? null,
      telegramInviteUrl: payload.telegramInviteUrl ?? null,
      deliveryInstructions: payload.deliveryInstructions ?? null,
      contentOverridesJson: payload.contentOverridesJson ?? null,
    }),
  });
}

export async function updateAdminBillingPlan(planId: string, payload: {
  code: string;
  name: string;
  description?: string;
  price: number;
  currency?: string;
  interval: string;
  durationMonths?: number;
  includedCredits?: number;
  displayOrder?: number;
  isVisible?: boolean;
  isRenewable?: boolean;
  trialDays?: number;
  status?: string;
  includedSubtestsJson?: string;
  entitlementsJson?: string;
} & AdminBillingPlanOet2026Fields) {
  return apiRequest(`/v1/admin/billing/plans/${encodeURIComponent(planId)}`, {
    method: 'PUT',
    body: JSON.stringify({
      code: payload.code,
      name: payload.name,
      description: payload.description ?? '',
      price: payload.price,
      currency: payload.currency ?? 'AUD',
      interval: payload.interval,
      durationMonths: payload.durationMonths ?? 1,
      includedCredits: payload.includedCredits ?? 0,
      displayOrder: payload.displayOrder ?? 0,
      isVisible: payload.isVisible ?? true,
      isRenewable: payload.isRenewable ?? true,
      trialDays: payload.trialDays ?? 0,
      status: payload.status ?? 'active',
      includedSubtestsJson: payload.includedSubtestsJson ?? '[]',
      entitlementsJson: payload.entitlementsJson ?? '{}',
      originalPriceGbp: payload.originalPriceGbp ?? null,
      accessDurationDays: payload.accessDurationDays ?? null,
      writingAddonsEnabled: payload.writingAddonsEnabled ?? null,
      speakingAddonsEnabled: payload.speakingAddonsEnabled ?? null,
      speakingPracticeAccessEnabled: payload.speakingPracticeAccessEnabled ?? null,
      tutorBookDiscountEnabled: payload.tutorBookDiscountEnabled ?? null,
      profession: payload.profession ?? null,
      productCategory: payload.productCategory ?? null,
      dashboardModulesJson: payload.dashboardModulesJson ?? null,
      bundledWritingAssessments: payload.bundledWritingAssessments ?? null,
      bundledSpeakingSessions: payload.bundledSpeakingSessions ?? null,
      bundledAiCredits: payload.bundledAiCredits ?? null,
      bundledTutorBook: payload.bundledTutorBook ?? null,
      bundledBasicEnglish: payload.bundledBasicEnglish ?? null,
      isDraft: payload.isDraft ?? null,
      extensionAllowed: payload.extensionAllowed ?? null,
      recallUpdatesEnabled: payload.recallUpdatesEnabled ?? null,
      comparisonFeaturesJson: payload.comparisonFeaturesJson ?? null,
      deliveryMethod: payload.deliveryMethod ?? null,
      telegramInviteUrl: payload.telegramInviteUrl ?? null,
      deliveryInstructions: payload.deliveryInstructions ?? null,
      contentOverridesJson: payload.contentOverridesJson ?? null,
    }),
  });
}

export async function fetchAdminBillingAddOns(params?: { status?: string }) {
  const qs = params?.status ? `?status=${encodeURIComponent(params.status)}` : '';
  return apiRequest(`/v1/admin/billing/add-ons${qs}`);
}

export async function fetchAdminBillingAddOnVersions(addOnId: string) {
  return apiRequest(`/v1/admin/billing/add-ons/${encodeURIComponent(addOnId)}/versions`);
}

export interface AdminBillingAddOnOet2026Fields {
  originalPriceGbp?: number | null;
  addonKind?: string;
  requiresEligibleParent?: boolean;
  eligibilityFlag?: string;
  lettersGranted?: number;
  sessionsGranted?: number;
  /** AI grading package storefront group: full|listening|reading|writing|speaking|mock. */
  aiPackageGroup?: string;
  /** JSON array of admin-authored AI feature bullet strings. */
  aiFeaturesJson?: string;
}

export async function createAdminBillingAddOn(payload: {
  code?: string;
  name: string;
  description?: string;
  price: number;
  currency?: string;
  interval: string;
  durationDays?: number;
  grantCredits?: number;
  displayOrder?: number;
  isRecurring?: boolean;
  appliesToAllPlans?: boolean;
  isStackable?: boolean;
  quantityStep?: number;
  maxQuantity?: number | null;
  status?: string;
  compatiblePlanCodesJson?: string;
  grantEntitlementsJson?: string;
} & AdminBillingAddOnOet2026Fields) {
  return apiRequest('/v1/admin/billing/add-ons', {
    method: 'POST',
    body: JSON.stringify({
      code: payload.code ?? normalizeBillingCode(payload.name),
      name: payload.name,
      description: payload.description ?? '',
      price: payload.price,
      currency: payload.currency ?? 'AUD',
      interval: payload.interval,
      durationDays: payload.durationDays ?? 0,
      grantCredits: payload.grantCredits ?? 0,
      displayOrder: payload.displayOrder ?? 0,
      isRecurring: payload.isRecurring ?? false,
      appliesToAllPlans: payload.appliesToAllPlans ?? true,
      isStackable: payload.isStackable ?? true,
      quantityStep: payload.quantityStep ?? 1,
      maxQuantity: payload.maxQuantity ?? null,
      status: payload.status ?? 'active',
      compatiblePlanCodesJson: payload.compatiblePlanCodesJson ?? '[]',
      grantEntitlementsJson: payload.grantEntitlementsJson ?? '{}',
      originalPriceGbp: payload.originalPriceGbp ?? null,
      addonKind: payload.addonKind ?? null,
      requiresEligibleParent: payload.requiresEligibleParent ?? null,
      eligibilityFlag: payload.eligibilityFlag ?? null,
      lettersGranted: payload.lettersGranted ?? null,
      sessionsGranted: payload.sessionsGranted ?? null,
      aiPackageGroup: payload.aiPackageGroup ?? null,
      aiFeaturesJson: payload.aiFeaturesJson ?? null,
    }),
  });
}

export async function updateAdminBillingAddOn(addOnId: string, payload: {
  code: string;
  name: string;
  description?: string;
  price: number;
  currency?: string;
  interval: string;
  durationDays?: number;
  grantCredits?: number;
  displayOrder?: number;
  isRecurring?: boolean;
  appliesToAllPlans?: boolean;
  isStackable?: boolean;
  quantityStep?: number;
  maxQuantity?: number | null;
  status?: string;
  compatiblePlanCodesJson?: string;
  grantEntitlementsJson?: string;
} & AdminBillingAddOnOet2026Fields) {
  return apiRequest(`/v1/admin/billing/add-ons/${encodeURIComponent(addOnId)}`, {
    method: 'PUT',
    body: JSON.stringify({
      code: payload.code,
      name: payload.name,
      description: payload.description ?? '',
      price: payload.price,
      currency: payload.currency ?? 'AUD',
      interval: payload.interval,
      durationDays: payload.durationDays ?? 0,
      grantCredits: payload.grantCredits ?? 0,
      displayOrder: payload.displayOrder ?? 0,
      isRecurring: payload.isRecurring ?? false,
      appliesToAllPlans: payload.appliesToAllPlans ?? true,
      isStackable: payload.isStackable ?? true,
      quantityStep: payload.quantityStep ?? 1,
      maxQuantity: payload.maxQuantity ?? null,
      status: payload.status ?? 'active',
      compatiblePlanCodesJson: payload.compatiblePlanCodesJson ?? '[]',
      grantEntitlementsJson: payload.grantEntitlementsJson ?? '{}',
      originalPriceGbp: payload.originalPriceGbp ?? null,
      addonKind: payload.addonKind ?? null,
      requiresEligibleParent: payload.requiresEligibleParent ?? null,
      eligibilityFlag: payload.eligibilityFlag ?? null,
      lettersGranted: payload.lettersGranted ?? null,
      sessionsGranted: payload.sessionsGranted ?? null,
      aiPackageGroup: payload.aiPackageGroup ?? null,
      aiFeaturesJson: payload.aiFeaturesJson ?? null,
    }),
  });
}

// ── Hard-delete (404 + 409 handled by caller). Server returns 409 when
// the plan/add-on still has historical references — caller should fall
// back to archive (PUT status=archived) in that case.

export async function deleteAdminBillingPlan(planId: string): Promise<{ id: string; code: string; deleted: boolean }> {
  return apiRequest(`/v1/admin/billing/plans/${encodeURIComponent(planId)}`, { method: 'DELETE' });
}

export async function deleteAdminBillingAddOn(addOnId: string): Promise<{ id: string; code: string; deleted: boolean }> {
  return apiRequest(`/v1/admin/billing/add-ons/${encodeURIComponent(addOnId)}`, { method: 'DELETE' });
}
