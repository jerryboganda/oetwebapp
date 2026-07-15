/**
 * Typed API client for the Phase 4-10 billing endpoints exposed by
 * BillingExpansionEndpoints.cs:
 *  - manual payments (learner + admin)
 *  - scholarships (admin)
 *  - affiliates (admin)
 *  - dunning campaigns (admin, read-only)
 *  - metrics (admin)
 */
import { apiClient, fetchAuthorizedBlob } from '@/lib/api';

// ── Manual payments ───────────────────────────────────────────────

/** `learner_upload` = the buyer uploaded a file; `gateway_receipt` = the system wrote a
 * receipt row when a card gateway completed, and there is no file to view. */
export type PaymentProofKind = 'learner_upload' | 'gateway_receipt';

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
  /** True when a proof file exists. The storage key is never sent to clients —
   * fetch the bytes via {@link getManualPaymentProofBlob}. Always false for
   * `gateway_receipt` rows, whose evidence is the gateway reference. */
  hasProof: boolean;
  kind: PaymentProofKind | string;
  /** stripe/paypal/easykash/... — set only on `gateway_receipt` rows. */
  gateway: string | null;
  /** The buyer's registered profession at purchase time. */
  professionId: string | null;
  proofWaivedAt: string | null;
  proofWaiverReason: string | null;
  status: 'pending' | 'needs_review' | 'approved' | 'paid' | 'rejected' | 'cancelled' | string;
  submittedAt: string;
  reviewedAt: string | null;
  adminNotes: string | null;
  accessGrantedSubscriptionId?: string | null;
  /** Fulfilment status of the subscription this proof granted, when it granted one. */
  fulfilmentStatus?: FulfilmentStatus | string | null;
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

export interface ManualPaymentListResponse {
  total: number;
  page: number;
  pageSize: number;
  items: ManualPaymentDto[];
}

export interface AdminManualPaymentListParams {
  status?: string;
  kind?: PaymentProofKind | string;
  /** 1-based. */
  page?: number;
  /** Server clamps to 1..200. */
  pageSize?: number;
}

/** Server-paged — the dashboard must render `total` and drive `page` itself; the
 * response is never the whole table. */
export function listAdminManualPayments(params: AdminManualPaymentListParams = {}): Promise<ManualPaymentListResponse> {
  const q = new URLSearchParams();
  if (params.status) q.set('status', params.status);
  if (params.kind) q.set('kind', params.kind);
  if (params.page) q.set('page', String(params.page));
  if (params.pageSize) q.set('pageSize', String(params.pageSize));
  const qs = q.toString();
  return apiClient.get<ManualPaymentListResponse>(`/v1/admin/billing/manual-payments/${qs ? `?${qs}` : ''}`);
}

export function approveManualPayment(id: string, notes?: string): Promise<ManualPaymentDto> {
  return apiClient.post<ManualPaymentDto>(`/v1/admin/billing/manual-payments/${encodeURIComponent(id)}/approve`, { notes });
}

export function rejectManualPayment(id: string, notes?: string): Promise<ManualPaymentDto> {
  return apiClient.post<ManualPaymentDto>(`/v1/admin/billing/manual-payments/${encodeURIComponent(id)}/reject`, { notes });
}

/**
 * Move a manual payment between the two non-terminal states
 * (`pending` ↔ `needs_review`). Approve/reject handle the terminal outcomes.
 */
export function setManualPaymentStatus(id: string, status: 'pending' | 'needs_review', notes?: string): Promise<ManualPaymentDto> {
  return apiClient.post<ManualPaymentDto>(`/v1/admin/billing/manual-payments/${encodeURIComponent(id)}/status`, { status, notes });
}

/**
 * Release a pending offline order whose payment was confirmed out-of-band (the owner
 * saw the transfer land) without waiting for the learner to upload a file. Audited.
 */
export function waiveManualPaymentProof(id: string, reason: string): Promise<ManualPaymentDto> {
  return apiClient.post<ManualPaymentDto>(`/v1/admin/billing/manual-payments/${encodeURIComponent(id)}/waive-proof`, { reason });
}

/** Undo a mis-clicked Reject: `rejected` → `pending`. */
export function reopenManualPayment(id: string, notes?: string): Promise<ManualPaymentDto> {
  return apiClient.post<ManualPaymentDto>(`/v1/admin/billing/manual-payments/${encodeURIComponent(id)}/reopen`, { notes });
}

/**
 * Fetch the uploaded proof file as a Blob for the admin verification dashboard.
 * The caller inspects `blob.type` to render an inline image or embedded PDF, and
 * owns createObjectURL/revoke. Authorized via the admin session.
 */
export function getManualPaymentProofBlob(id: string): Promise<Blob> {
  return fetchAuthorizedBlob(`/v1/admin/billing/manual-payments/${encodeURIComponent(id)}/proof`);
}

// ── Manual fulfilment queue ──────────────────────────────────────

export type DeliveryMethod = 'automatic_web' | 'manual_web' | 'telegram' | 'manual_material';

export type FulfilmentStatus = 'auto' | 'pending_manual' | 'fulfilled';

/** A paid order awaiting an admin hand-over. Its subscription stays Pending, so the
 * entitlement resolver grants nothing until it is marked fulfilled. */
export interface PendingFulfilmentDto {
  subscriptionId: string;
  userId: string;
  displayName: string;
  email: string;
  planCode: string;
  planName: string;
  deliveryMethod: DeliveryMethod | string;
  telegramInviteUrl: string | null;
  deliveryInstructions: string | null;
  status: string;
  fulfilmentStatus: FulfilmentStatus | string;
  startedAt: string;
  changedAt: string;
  proof: ManualPaymentDto | null;
}

export function listPendingFulfilment(): Promise<PendingFulfilmentDto[]> {
  return apiClient.get<PendingFulfilmentDto[]>('/v1/admin/billing/fulfilment/');
}

/** Hand the package over: FulfilmentStatus → `fulfilled` and the subscription → Active,
 * which is what actually releases access. */
export function markSubscriptionFulfilled(subscriptionId: string, notes?: string): Promise<PendingFulfilmentDto> {
  return apiClient.post<PendingFulfilmentDto>(
    `/v1/admin/billing/fulfilment/subscriptions/${encodeURIComponent(subscriptionId)}/mark-fulfilled`,
    { notes },
  );
}

// ── Payment methods (admin-configurable) ─────────────────────────

export interface PaymentMethodConfigDto {
  id: string;
  key: string;
  label: string;
  category: 'inside_egypt' | 'international' | string;
  detail: string;
  meta: string | null;
  instructions: string;
  note: string | null;
  referenceRule: boolean;
  showQr: boolean;
  /** True when an admin has uploaded a QR image (fetch it via {@link fetchPaymentMethodQrBlob}). */
  hasQrImage: boolean;
  iconName: string | null;
  isActive: boolean;
  displayOrder: number;
  createdAt: string;
  updatedAt: string;
}

export interface PaymentMethodConfigUpsertRequest {
  key: string;
  label: string;
  category: string;
  detail: string;
  meta?: string | null;
  instructions: string;
  note?: string | null;
  referenceRule: boolean;
  showQr: boolean;
  iconName?: string | null;
  isActive: boolean;
  displayOrder: number;
}

/** Active payment methods shown to learners on the manual-payment page, ordered by displayOrder. */
export function listPublicPaymentMethods(): Promise<PaymentMethodConfigDto[]> {
  return apiClient.get<PaymentMethodConfigDto[]>('/v1/billing/payment-methods');
}

/**
 * Fetch a payment-method QR image as a Blob (the endpoint requires auth, so the
 * caller builds an object URL rather than putting a token in an <img src>).
 */
export function fetchPaymentMethodQrBlob(key: string): Promise<Blob> {
  return fetchAuthorizedBlob(`/v1/billing/payment-methods/${encodeURIComponent(key)}/qr`);
}

export function listAdminPaymentMethods(): Promise<PaymentMethodConfigDto[]> {
  return apiClient.get<PaymentMethodConfigDto[]>('/v1/admin/billing/payment-methods');
}

export function upsertPaymentMethod(payload: PaymentMethodConfigUpsertRequest): Promise<PaymentMethodConfigDto> {
  return apiClient.post<PaymentMethodConfigDto>('/v1/admin/billing/payment-methods', payload);
}

export function deletePaymentMethod(id: string): Promise<void> {
  return apiClient.delete(`/v1/admin/billing/payment-methods/${encodeURIComponent(id)}`);
}

/** Upload a QR image (base64, no data: prefix) for a method. Returns the updated config. */
export function uploadPaymentMethodQr(key: string, imageBase64: string): Promise<PaymentMethodConfigDto> {
  return apiClient.post<PaymentMethodConfigDto>(
    `/v1/admin/billing/payment-methods/${encodeURIComponent(key)}/qr`,
    { imageBase64 },
  );
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
