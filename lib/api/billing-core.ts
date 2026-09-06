import type { Invoice } from '../billing-types';
import { resolveReviewTarget } from './attempt-cache';
import { apiRequest, asArray, asRecord, toNullableString, toStringArray, type ApiRecord } from './client';
import { formatCurrency } from '../domain/format';
import { titleCase } from './task-mappers';
import { mapAiPackageCreditSnapshot } from '../map-ai-package-credit-snapshot';
import { fetchAuthorizedObjectUrl } from './binary';
import { type AiPackage, AiPackageCreditSnapshot, AiPackagesResponse, BillingChangePreview, BillingData, BillingPaymentStatus, BillingProductType, BillingQuote } from '../billing-types';
import { type FocusArea, TurnaroundOption } from '../mock-data';

/**
 * Learner billing: plans, quote, checkout, credits, invoices, review requests.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export interface PublicBillingPlan {
  planId: string;
  code: string;
  label: string;
  tier: string;
  description: string;
  price: { amount: number; currency: string; interval: string };
  reviewCredits: number;
  mockReportsIncluded: boolean;
  includedSubtests: string[];
  trialDays: number;
  isRenewable: boolean;
  changeDirection: string;
}

export async function fetchPublicPlans(): Promise<{ items: PublicBillingPlan[] }> {
  return apiRequest('/v1/public/plans');
}

export async function fetchBilling(): Promise<BillingData> {
  const [summary, invoices, plans, extras] = await Promise.all([
    apiRequest<ApiRecord>('/v1/billing/summary'),
    apiRequest<ApiRecord>('/v1/billing/invoices'),
    apiRequest<ApiRecord>('/v1/billing/plans'),
    apiRequest<ApiRecord>('/v1/billing/extras'),
  ]);
  const activeAddOns = (summary.activeAddOns ?? []).map((item: ApiRecord) => ({
    id: item.id,
    code: item.code,
    name: item.name,
    productType: item.productType,
    quantity: Number(item.quantity ?? 0),
    price: formatCurrency(item.price?.amount ?? item.price, item.price?.currency ?? item.currency ?? 'AUD'),
    currency: item.price?.currency ?? item.currency ?? 'AUD',
    interval: item.price?.interval ?? item.interval ?? 'one_time',
    status: item.status ?? 'active',
    description: item.description ?? '',
    grantCredits: Number(item.grantCredits ?? 0),
    durationDays: Number(item.durationDays ?? 0),
    isRecurring: Boolean(item.isRecurring),
    appliesToAllPlans: Boolean(item.appliesToAllPlans),
    quantityStep: Number(item.quantityStep ?? 1),
    maxQuantity: item.maxQuantity == null ? null : Number(item.maxQuantity),
    compatiblePlanCodes: toStringArray(item.compatiblePlanCodes),
  }));
  return {
    currentPlan: summary.planName ?? titleCase(summary.planId),
    currentPlanId: summary.planId,
    currentPlanCode: summary.planCode ?? summary.planId,
    planName: summary.planName ?? titleCase(summary.planId),
    planDescription: summary.planDescription ?? '',
    profession: summary.profession ?? 'all',
    price: formatCurrency(summary.price?.amount ?? 0, summary.price?.currency ?? 'GBP'),
    interval: summary.price.interval,
    // Keep the raw status token ("active" | "frozen" | "past_due" | ...). The UI
    // formats it for display via formatSubscriptionStatus; keeping it raw also
    // lets status comparisons (e.g. past_due) match reliably.
    status: summary.status,
    nextRenewal: summary.nextRenewalAt,
    reviewCredits: summary.wallet.creditBalance,
    activeAddOns,
    entitlements: {
      productiveSkillReviewsEnabled: Boolean(summary.entitlements?.productiveSkillReviewsEnabled),
      supportedReviewSubtests: summary.entitlements?.supportedReviewSubtests ?? [],
      invoiceDownloadsAvailable: Boolean(summary.entitlements?.invoiceDownloadsAvailable),
    },
    plans: (plans.items ?? []).map((plan: ApiRecord) => ({
      id: plan.planId ?? plan.code,
      code: plan.code ?? plan.planId,
      label: plan.label,
      tier: titleCase(plan.tier ?? plan.code ?? plan.planId),
      description: plan.description,
      price: formatCurrency(plan.price?.amount, plan.price?.currency),
      interval: plan.price?.interval ?? 'month',
      reviewCredits: Number(plan.reviewCredits ?? 0),
      canChangeTo: Boolean(plan.canChangeTo),
      changeDirection: plan.changeDirection ?? 'current',
      badge: plan.badge ?? '',
      status: plan.status ?? 'active',
      durationMonths: Number(plan.durationMonths ?? 1),
      isVisible: Boolean(plan.isVisible ?? true),
      isRenewable: Boolean(plan.isRenewable ?? true),
      trialDays: Number(plan.trialDays ?? 0),
      displayOrder: Number(plan.displayOrder ?? 0),
      includedSubtests: toStringArray(plan.includedSubtests),
      entitlements: asRecord(plan.entitlements),
    })),
    addOns: (extras.items ?? []).map((extra: ApiRecord) => ({
      id: extra.id,
      code: extra.code ?? extra.id,
      name: extra.name ?? extra.description ?? extra.id,
      productType: extra.productType ?? 'review_credits',
      quantity: Number(extra.quantity ?? 0),
      price: formatCurrency(extra.price?.amount ?? extra.price, extra.price?.currency ?? extra.currency ?? 'AUD'),
      currency: extra.price?.currency ?? extra.currency ?? 'AUD',
      interval: extra.price?.interval ?? extra.interval ?? 'one_time',
      status: extra.status ?? 'active',
      description: extra.description,
      grantCredits: Number(extra.grantCredits ?? 0),
      durationDays: Number(extra.durationDays ?? 0),
      isRecurring: Boolean(extra.isRecurring ?? false),
      appliesToAllPlans: Boolean(extra.appliesToAllPlans ?? true),
      quantityStep: Number(extra.quantityStep ?? 1),
      maxQuantity: extra.maxQuantity == null ? null : Number(extra.maxQuantity),
      compatiblePlanCodes: toStringArray(extra.compatiblePlanCodes),
    })),
    coupons: [],
    quote: null,
    invoices: (invoices.items ?? []).map((invoice: ApiRecord) => ({
      id: invoice.invoiceId ?? invoice.id,
      date: invoice.date,
      amount: formatCurrency(invoice.amount, invoice.currency),
      status: toBillingStatus(invoice.status),
      currency: invoice.currency,
      downloadUrl: invoice.downloadUrl,
      description: invoice.description,
    })),
  };
}

export async function fetchBillingQuote(input: {
  productType: BillingProductType;
  quantity: number;
  priceId?: string | null;
  couponCode?: string | null;
  addOnCodes?: string[];
  parentSubscriptionId?: string | null;
}): Promise<BillingQuote> {
  const params = new URLSearchParams();
  params.set('productType', input.productType);
  params.set('quantity', String(input.quantity));
  if (input.priceId) params.set('priceId', input.priceId);
  if (input.couponCode) params.set('couponCode', input.couponCode);
  if (input.addOnCodes && input.addOnCodes.length > 0) params.set('addOnCodes', input.addOnCodes.join(','));
  if (input.parentSubscriptionId) params.set('parentSubscriptionId', input.parentSubscriptionId);
  const quote = await apiRequest<ApiRecord>(`/v1/billing/quote?${params.toString()}`);
  // The backend reports the plan's delivery method inside the quote's `validation`
  // bag rather than as a top-level field. Prefer a top-level value if one ever
  // appears, so promoting it server-side does not need a change here.
  const validation = asRecord(quote.validation);
  return {
    quoteId: quote.quoteId,
    status: quote.status,
    currency: quote.currency,
    subtotalAmount: Number(quote.subtotalAmount ?? 0),
    discountAmount: Number(quote.discountAmount ?? 0),
    totalAmount: Number(quote.totalAmount ?? 0),
    planCode: quote.planCode ?? null,
    couponCode: quote.couponCode ?? null,
    addOnCodes: toStringArray(quote.addOnCodes),
    items: asArray(quote.items).map((item: ApiRecord) => ({
      kind: String(item.kind ?? 'item'),
      code: String(item.code ?? item.id ?? ''),
      name: String(item.name ?? item.code ?? ''),
      amount: Number(item.amount ?? 0),
      currency: String(item.currency ?? 'AUD'),
      quantity: Number(item.quantity ?? 1),
      description: toNullableString(item.description),
    })),
    expiresAt: quote.expiresAt,
    summary: String(quote.summary ?? ''),
    deliveryMethod: toNullableString(quote.deliveryMethod) ?? toNullableString(validation.deliveryMethod),
    validation,
  };
}

export async function purchaseReviewCredits(count: number): Promise<{ success: boolean; newBalance: number }> {
  const billing = await fetchBilling();
  await apiRequest('/v1/billing/checkout-sessions', {
    method: 'POST',
    body: JSON.stringify({
      productType: 'review_credits',
      quantity: count,
      priceId: null,
      couponCode: null,
      addOnCodes: null,
      quoteId: null,
      idempotencyKey: crypto.randomUUID?.() ?? String(Date.now()),
    }),
  });
  return { success: true, newBalance: billing.reviewCredits + count };
}

export async function fetchBillingChangePreview(targetPlanId: string): Promise<BillingChangePreview> {
  const preview = await apiRequest<ApiRecord>(`/v1/billing/change-preview?targetPlanId=${encodeURIComponent(targetPlanId)}`);
  return {
    currentPlanId: preview.currentPlanId,
    targetPlanId: preview.targetPlanId,
    direction: preview.direction,
    proratedAmount: formatCurrency(preview.proratedAmount),
    effectiveAt: preview.effectiveAt,
    summary: preview.summary,
    currentCreditsIncluded: Number(preview.currentCreditsIncluded ?? 0),
    targetCreditsIncluded: Number(preview.targetCreditsIncluded ?? 0),
  };
}

export async function createBillingCheckoutSession(input: {
  productType: BillingProductType;
  quantity: number;
  priceId?: string | null;
  couponCode?: string | null;
  addOnCodes?: string[];
  parentSubscriptionId?: string | null;
  quoteId?: string | null;
  gateway?: string;
  idempotencyKey?: string;
}): Promise<{ checkoutUrl: string; checkoutSessionId: string; quoteId?: string | null; totalAmount?: number; currency?: string; clientSecret?: string | null; gateway?: string }> {
  const response = await apiRequest<ApiRecord>('/v1/billing/checkout-sessions', {
    method: 'POST',
    body: JSON.stringify({
      productType: input.productType,
      quantity: input.quantity,
      priceId: input.priceId ?? null,
      couponCode: input.couponCode ?? null,
      addOnCodes: input.addOnCodes ?? null,
      parentSubscriptionId: input.parentSubscriptionId ?? null,
      quoteId: input.quoteId ?? null,
      gateway: input.gateway ?? null,
      idempotencyKey: input.idempotencyKey ?? crypto.randomUUID?.() ?? String(Date.now()),
    }),
  });
  return {
    checkoutUrl: response.checkoutUrl,
    checkoutSessionId: response.checkoutSessionId,
    quoteId: response.quoteId ?? null,
    totalAmount: response.totalAmount != null ? Number(response.totalAmount) : undefined,
    currency: response.currency ?? undefined,
    clientSecret: response.clientSecret ?? null,
    gateway: response.gateway ?? undefined,
  };
}

export async function fetchBillingPaymentStatus(input: {
  quoteId?: string | null;
  sessionId?: string | null;
}): Promise<BillingPaymentStatus> {
  const params = new URLSearchParams();
  if (input.quoteId) params.set('quoteId', input.quoteId);
  if (input.sessionId) params.set('sessionId', input.sessionId);
  const response = await apiRequest<ApiRecord>(`/v1/billing/payment-status?${params.toString()}`);
  return {
    status: String(response.status ?? 'pending'),
    quoteId: toNullableString(response.quoteId),
    checkoutSessionId: toNullableString(response.checkoutSessionId),
    productType: toNullableString(response.productType),
    targetPlanId: toNullableString(response.targetPlanId),
    addOnCodes: toStringArray(response.addOnCodes),
    items: asArray(response.items).map((item: ApiRecord) => ({
      kind: String(item.kind ?? 'item'),
      code: String(item.code ?? ''),
      name: String(item.name ?? item.code ?? ''),
      amount: Number(item.amount ?? 0),
      currency: String(item.currency ?? response.currency ?? 'GBP'),
      quantity: Number(item.quantity ?? 1),
      description: toNullableString(item.description),
    })),
    totalAmount: Number(response.totalAmount ?? 0),
    currency: String(response.currency ?? 'GBP'),
    invoiceId: toNullableString(response.invoiceId),
    subscriptionId: toNullableString(response.subscriptionId),
    failureReason: toNullableString(response.failureReason),
    fulfilledAt: toNullableString(response.fulfilledAt),
    expiresAt: toNullableString(response.expiresAt),
    manualDeliveryRequired: response.manualDeliveryRequired === true,
    whatsAppUrl: toNullableString(response.whatsAppUrl),
  };
}

export async function fetchAiPackages(): Promise<AiPackagesResponse> {
  const data = await apiRequest<ApiRecord>('/v1/billing/ai-packages');
  const mapPackage = (p: ApiRecord): AiPackage => ({
    code: String(p.code ?? ''),
    name: String(p.name ?? ''),
    description: String(p.description ?? ''),
    price: Number(p.price ?? 0),
    currency: String(p.currency ?? 'GBP'),
    credits: Number(p.credits ?? 0),
    sharedCredits: Number(p.sharedCredits ?? 0),
    writingCredits: Number(p.writingCredits ?? 0),
    speakingCredits: Number(p.speakingCredits ?? 0),
    mocks: Number(p.mocks ?? 0),
    validityDays: Number(p.validityDays ?? 0),
    priorityQueue: Boolean(p.priorityQueue ?? false),
    group: String(p.group ?? 'full') as AiPackage['group'],
    features: toStringArray(p.features),
  });
  const separate = asRecord(data.separate);
  return {
    currency: String(data.currency ?? 'GBP'),
    full: asArray(data.full).map(mapPackage),
    separate: {
      listening: asArray(separate.listening).map(mapPackage),
      reading: asArray(separate.reading).map(mapPackage),
      writing: asArray(separate.writing).map(mapPackage),
      speaking: asArray(separate.speaking).map(mapPackage),
    },
    mock: asArray(data.mock).map(mapPackage),
  };
}

export async function fetchMyAiPackageCredits(): Promise<AiPackageCreditSnapshot> {
  const data = await apiRequest<ApiRecord>('/v1/me/ai-package-credits');
  return mapAiPackageCreditSnapshot(data);
}

export interface LearnerAttemptHistoryItem {
  attemptId: string;
  subtest: string;
  title: string;
  contentRef?: string | null;
  startedAt: string;
  submittedAt?: string | null;
  status: 'in_progress' | 'completed';
  balanceSource?: string | null;
  creditsUsed: number;
  route: string;
}

/** Unified all-four-subtest activity history (Master Catalogue §2). */
export async function fetchMyAttemptHistory(limit = 100): Promise<LearnerAttemptHistoryItem[]> {
  const data = await apiRequest<ApiRecord>(`/v1/me/attempts?limit=${limit}`);
  return asArray((data as ApiRecord).items).map((item) => ({
    attemptId: String(item.attemptId ?? ''),
    subtest: String(item.subtest ?? ''),
    title: String(item.title ?? ''),
    contentRef: item.contentRef == null ? null : String(item.contentRef),
    startedAt: String(item.startedAt ?? ''),
    submittedAt: item.submittedAt == null ? null : String(item.submittedAt),
    status: item.status === 'completed' ? 'completed' : 'in_progress',
    balanceSource: item.balanceSource == null ? null : String(item.balanceSource),
    creditsUsed: Number(item.creditsUsed ?? 0),
    route: String(item.route ?? '/submissions'),
  }));
}

export async function fetchAdminUserAiCredits(userId: string): Promise<AiPackageCreditSnapshot> {
  const data = await apiRequest<ApiRecord>(
    `/v1/admin/users/${encodeURIComponent(userId)}/ai-credits?pageSize=100`,
  );
  return mapAiPackageCreditSnapshot(data);
}

export interface AiPackageCreditAdjustmentPayload {
  sharedCreditsDelta?: number;
  sharedCreditsSet?: number;
  flexibleCreditsDelta?: number;
  flexibleCreditsSet?: number;
  writingOnlyCreditsDelta?: number;
  writingOnlyCreditsSet?: number;
  speakingOnlyCreditsDelta?: number;
  speakingOnlyCreditsSet?: number;
  listeningTestsDelta?: number;
  listeningTestsSet?: number;
  readingTestsDelta?: number;
  readingTestsSet?: number;
  mockExamsDelta?: number;
  mockExamsSet?: number;
  expiresAt?: string | null;
  reason?: string | null;
}

/** Per-bucket credit adjustment — every change is written to the ledger. */
export async function adjustAdminUserAiCredits(
  userId: string,
  payload: AiPackageCreditAdjustmentPayload,
): Promise<AiPackageCreditSnapshot> {
  const data = await apiRequest<ApiRecord>(
    `/v1/admin/ai-package-credits/${encodeURIComponent(userId)}/adjust`,
    { method: 'POST', body: JSON.stringify(payload) },
  );
  return mapAiPackageCreditSnapshot(data);
}

export async function adjustAdminAiPackageCredits(
  userId: string,
  payload: {
    sharedCreditsDelta?: number;
    sharedCreditsSet?: number;
    writingOnlyCreditsDelta?: number;
    writingOnlyCreditsSet?: number;
    speakingOnlyCreditsDelta?: number;
    speakingOnlyCreditsSet?: number;
    flexibleCreditsDelta?: number;
    flexibleCreditsSet?: number;
    listeningTestsDelta?: number;
    listeningTestsSet?: number;
    readingTestsDelta?: number;
    readingTestsSet?: number;
    mockExamsDelta?: number;
    mockExamsSet?: number;
    reason?: string;
  },
): Promise<AiPackageCreditSnapshot> {
  const data = await apiRequest<ApiRecord>(
    `/v1/admin/ai-package-credits/${encodeURIComponent(userId)}/adjust`,
    {
      method: 'POST',
      body: JSON.stringify({
        flexibleCreditsDelta: payload.flexibleCreditsDelta ?? 0,
        writingOnlyCreditsDelta: payload.writingOnlyCreditsDelta ?? 0,
        speakingOnlyCreditsDelta: payload.speakingOnlyCreditsDelta ?? 0,
        listeningTestsDelta: payload.listeningTestsDelta ?? 0,
        readingTestsDelta: payload.readingTestsDelta ?? 0,
        mockExamsDelta: payload.mockExamsDelta ?? 0,
        sharedCreditsDelta: payload.sharedCreditsDelta ?? 0,
        sharedCreditsSet: payload.sharedCreditsSet ?? null,
        flexibleCreditsSet: payload.flexibleCreditsSet ?? null,
        writingOnlyCreditsSet: payload.writingOnlyCreditsSet ?? null,
        speakingOnlyCreditsSet: payload.speakingOnlyCreditsSet ?? null,
        listeningTestsSet: payload.listeningTestsSet ?? null,
        readingTestsSet: payload.readingTestsSet ?? null,
        mockExamsSet: payload.mockExamsSet ?? null,
        reason: payload.reason ?? 'admin adjustment',
      }),
    },
  );
  return mapAiPackageCreditSnapshot(data);
}

export async function downloadInvoice(invoiceId: string): Promise<string> {
  return fetchAuthorizedObjectUrl(`/v1/billing/invoices/${encodeURIComponent(invoiceId)}/download`);
}

export async function pauseSubscription(days?: number, reason?: string): Promise<object> {
  return apiRequest('/v1/billing/subscription/pause', {
    method: 'POST',
    body: JSON.stringify({ days: days ?? null, reason: reason ?? null }),
  });
}

export async function resumeSubscription(): Promise<object> {
  return apiRequest('/v1/billing/subscription/resume', { method: 'POST' });
}

export interface BankAccountConfigDto {
  id: string;
  region: string;
  currency: string;
  bankName: string;
  accountHolderName: string;
  accountNumber?: string | null;
  routingOrSortCode?: string | null;
  iban?: string | null;
  swiftBic?: string | null;
  instructionsMarkdown?: string | null;
  isActive?: boolean;
}

export async function fetchMyBankAccounts(): Promise<BankAccountConfigDto[]> {
  return apiRequest<BankAccountConfigDto[]>('/v1/billing/bank-accounts/me');
}

export async function fetchTurnaroundOptions(): Promise<TurnaroundOption[]> {
  const response = await apiRequest<ApiRecord>('/v1/billing/review-options');
  return (response.items ?? []).map((item: ApiRecord) => ({
    id: item.id,
    label: item.label,
    time: item.turnaround,
    cost: item.price,
    description: item.description,
  }));
}

export async function fetchFocusAreas(subtest?: string): Promise<FocusArea[]> {
  const query = subtest ? `?subtest=${encodeURIComponent(subtest)}` : '';
  const criteria = await apiRequest<ApiRecord[]>(`/v1/reference/criteria${query}`);
  const unique = new Map<string, FocusArea>();
  for (const criterion of criteria) {
    unique.set(criterion.code, {
      id: criterion.code,
      label: criterion.label,
      description: criterion.description,
    });
  }
  return Array.from(unique.values());
}

export async function submitReviewRequest(request: { submissionId: string; turnaroundId: string; focusAreas: string[]; notes: string; }): Promise<{ reviewId: string; estimatedDelivery: string }> {
  const target = await resolveReviewTarget(request.submissionId);
  const response = await apiRequest<ApiRecord>('/v1/reviews/requests', {
    method: 'POST',
    body: JSON.stringify({
      attemptId: target.attemptId,
      subtest: target.subtest,
      turnaroundOption: request.turnaroundId,
      focusAreas: request.focusAreas,
      learnerNotes: request.notes,
      paymentSource: 'credits',
      idempotencyKey: crypto.randomUUID?.() ?? String(Date.now()),
    }),
  });
  const options = await fetchTurnaroundOptions();
  const option = options.find((item) => item.id === request.turnaroundId);
  return {
    reviewId: response.reviewRequestId,
    estimatedDelivery: option?.time ?? '48-72 hours',
  };
}


function toBillingStatus(value: string | null | undefined): Invoice['status'] {
  const normalized = (value ?? '').toLowerCase();
  if (normalized === 'pending') return 'Pending';
  if (normalized === 'failed') return 'Failed';
  return 'Paid';
}
