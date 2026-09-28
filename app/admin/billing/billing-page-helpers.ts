import { getAdminBillingEntitlementDiagnosticsData } from '@/lib/admin';
import { formatDateTime as formatSharedDateTime } from '@/lib/domain/datetime';
import type { AdminBillingAddOn, AdminBillingCoupon, AdminBillingPaymentTransaction, AdminBillingProviderLifecycleSignalsSummary, AdminBillingPlan } from '@/lib/types/admin';
import type { DiagnosticsLoadResult, BillingPlanFormState, BillingAddOnFormState, BillingCouponFormState } from './billing-page-types';

export const defaultPlanForm: BillingPlanFormState = {
  code: '',
  name: '',
  price: '0',
  description: '',
  currency: 'AUD',
  interval: 'month',
  durationMonths: '1',
  includedCredits: '0',
  displayOrder: '0',
  isVisible: true,
  isRenewable: true,
  trialDays: '0',
  diagnosticMockEntitlement: 'one_per_lifetime',
  status: 'active',
  includedSubtestsText: 'writing, speaking',
  entitlementsJson: '{}',
  originalPriceGbp: '',
  accessDurationDays: '180',
  writingAddonsEnabled: false,
  speakingAddonsEnabled: false,
  speakingPracticeAccessEnabled: true,
  tutorBookDiscountEnabled: false,
  profession: 'all',
  productCategory: '',
  dashboardModulesText: '',
  bundledWritingAssessments: '0',
  bundledSpeakingSessions: '0',
  bundledAiCredits: '0',
  bundledTutorBook: false,
  bundledBasicEnglish: false,
  isDraft: false,
  extensionAllowed: true,
  recallUpdatesEnabled: false,
  comparisonFeaturesText: '',
};

export const defaultAddOnForm: BillingAddOnFormState = {
  code: '',
  name: '',
  description: '',
  price: '0',
  currency: 'AUD',
  interval: 'one_time',
  durationDays: '0',
  grantCredits: '0',
  displayOrder: '0',
  isRecurring: false,
  appliesToAllPlans: true,
  isStackable: true,
  quantityStep: '1',
  maxQuantity: '',
  status: 'active',
  compatiblePlanCodesText: '',
  grantEntitlementsJson: '{}',
  originalPriceGbp: '',
  addonKind: '',
  requiresEligibleParent: false,
  eligibilityFlag: '',
  lettersGranted: '0',
  sessionsGranted: '0',
};

export const defaultCouponForm: BillingCouponFormState = {
  code: '',
  name: '',
  description: '',
  discountType: 'percentage',
  discountValue: '10',
  currency: 'AUD',
  startsAt: '',
  endsAt: '',
  usageLimitTotal: '',
  usageLimitPerUser: '',
  minimumSubtotal: '',
  isStackable: true,
  status: 'active',
  applicablePlanCodesText: '',
  applicableAddOnCodesText: '',
  notes: '',
  couponVariant: 'percent_off',
  variantMetadataJson: '',
};

export const emptyProviderLifecycleSummary: AdminBillingProviderLifecycleSignalsSummary = {
  totalSignals: 0,
  failedSignals: 0,
  unverifiedSignals: 0,
  unmatchedSignals: 0,
  refundSignals: 0,
  disputeSignals: 0,
  cancellationSignals: 0,
};

export function formatCurrency(amount: number, currency = 'AUD') {
  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
  }).format(amount);
}

export function splitList(value: string): string[] {
  return value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean);
}

export function toLocalDateTimeValue(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const offset = date.getTimezoneOffset();
  return new Date(date.getTime() - offset * 60_000).toISOString().slice(0, 16);
}

export function fromLocalDateTimeValue(value: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

export function toOptionalNumber(value: string): number | null {
  if (!value.trim()) return null;
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : null;
}

export function toNumber(value: string, fallback = 0): number {
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : fallback;
}

export async function loadEntitlementDiagnostics(): Promise<DiagnosticsLoadResult> {
  try {
    return { data: await getAdminBillingEntitlementDiagnosticsData(), status: 'success' };
  } catch (error) {
    console.error(error);
    return { data: null, status: 'error' };
  }
}

export function formatSummaryValue(value: unknown): string {
  if (value == null || value === '') return 'N/A';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (typeof value === 'number') return value.toLocaleString();
  if (Array.isArray(value)) return value.length > 0 ? value.join(', ') : 'None';
  if (typeof value === 'object') return `${Object.keys(value).length} fields`;
  return String(value);
}

export function formatDateTime(value: string | null | undefined): string {
  return formatSharedDateTime(value);
}

export function paymentStatusVariant(status: string): 'success' | 'danger' | 'warning' | 'default' | 'info' {
  if (status === 'completed') return 'success';
  if (status === 'failed' || status === 'disputed') return 'danger';
  if (status === 'pending') return 'warning';
  if (status === 'refunded') return 'info';
  return 'default';
}

export function paymentTypeLabel(value: string): string {
  const labels: Record<string, string> = {
    subscription_payment: 'Subscription payment',
    one_time_purchase: 'One-time purchase',
    wallet_top_up: 'Wallet top-up',
    refund: 'Refund',
  };
  return labels[value] ?? labelSummaryKey(value);
}

export function paymentProductLabel(payment: AdminBillingPaymentTransaction): string {
  const productType = payment.productType ? labelSummaryKey(payment.productType) : 'Not recorded';
  return payment.productId ? `${productType}: ${payment.productId}` : productType;
}

export function providerSignalStatusVariant(status: string): 'success' | 'danger' | 'warning' | 'default' | 'info' {
  if (status === 'completed') return 'success';
  if (status === 'failed') return 'danger';
  if (status === 'processing' || status === 'received') return 'warning';
  if (status === 'ignored') return 'default';
  return 'info';
}

export function providerSignalVerificationVariant(status: string): 'success' | 'danger' | 'warning' | 'default' | 'info' {
  if (status === 'verified') return 'success';
  if (status === 'failed') return 'danger';
  if (status === 'legacy') return 'warning';
  return 'default';
}

export function providerSignalCorrelationVariant(status: string): 'success' | 'danger' | 'warning' | 'default' | 'info' {
  if (status === 'linked') return 'success';
  if (status === 'ambiguous') return 'danger';
  if (status === 'unmatched') return 'warning';
  return 'default';
}

export function providerSignalCategoryVariant(category: string): 'success' | 'danger' | 'warning' | 'default' | 'info' {
  if (category === 'refund' || category === 'dispute') return 'danger';
  if (category === 'cancellation') return 'warning';
  if (category === 'unknown') return 'default';
  if (category === 'invoice') return 'info';
  return 'default';
}

export function labelSummaryKey(key: string): string {
  return key
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/_/g, ' ')
    .replace(/^./, (char) => char.toUpperCase());
}

export function jsonList(value: string): string {
  return JSON.stringify(splitList(value));
}

export function jsonLineList(value: string): string {
  const items = value
    .split('\n')
    .map((line) => line.trim().replace(/^[•\-*]\s*/, ''))
    .filter(Boolean);
  return JSON.stringify(items);
}

export function safeJsonObject(value: string, fallback: string = '{}'): string {
  const trimmed = value.trim();
  if (!trimmed) {
    return fallback;
  }

  const parsed = JSON.parse(trimmed);
  return JSON.stringify(parsed, null, 2);
}

export function normalizedCode(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 64);
}

export function toPlanForm(plan: AdminBillingPlan): BillingPlanFormState {
  return {
    code: plan.code ?? plan.id,
    name: plan.name,
    description: plan.description ?? '',
    price: String(plan.price),
    currency: plan.currency ?? 'AUD',
    interval: plan.interval,
    durationMonths: String(plan.durationMonths ?? 1),
    includedCredits: String(plan.includedCredits ?? 0),
    displayOrder: String(plan.displayOrder ?? 0),
    isVisible: plan.isVisible ?? true,
    isRenewable: plan.isRenewable ?? true,
    trialDays: String(plan.trialDays ?? 0),
    diagnosticMockEntitlement: plan.diagnosticMockEntitlement ?? 'one_per_lifetime',
    status: plan.status,
    includedSubtestsText: (plan.includedSubtests ?? []).join(', '),
    entitlementsJson: JSON.stringify(plan.entitlements ?? {}, null, 2),
    originalPriceGbp: plan.originalPriceGbp == null ? '' : String(plan.originalPriceGbp),
    accessDurationDays: String(plan.accessDurationDays ?? 180),
    writingAddonsEnabled: plan.writingAddonsEnabled ?? false,
    speakingAddonsEnabled: plan.speakingAddonsEnabled ?? false,
    speakingPracticeAccessEnabled: plan.speakingPracticeAccessEnabled ?? true,
    tutorBookDiscountEnabled: plan.tutorBookDiscountEnabled ?? false,
    profession: plan.profession ?? 'all',
    productCategory: plan.productCategory ?? '',
    dashboardModulesText: (plan.dashboardModules ?? []).join(', '),
    bundledWritingAssessments: String(plan.bundledWritingAssessments ?? 0),
    bundledSpeakingSessions: String(plan.bundledSpeakingSessions ?? 0),
    bundledAiCredits: String(plan.bundledAiCredits ?? 0),
    bundledTutorBook: plan.bundledTutorBook ?? false,
    bundledBasicEnglish: plan.bundledBasicEnglish ?? false,
    isDraft: plan.isDraft ?? false,
    extensionAllowed: plan.extensionAllowed ?? true,
    recallUpdatesEnabled: plan.recallUpdatesEnabled ?? false,
    comparisonFeaturesText: (plan.comparisonFeatures ?? []).join('\n'),
  };
}

export function toAddOnForm(addOn: AdminBillingAddOn): BillingAddOnFormState {
  return {
    code: addOn.code,
    name: addOn.name,
    description: addOn.description ?? '',
    price: String(addOn.price),
    currency: addOn.currency ?? 'AUD',
    interval: addOn.interval,
    durationDays: String(addOn.durationDays ?? 0),
    grantCredits: String(addOn.grantCredits ?? 0),
    displayOrder: String(addOn.displayOrder ?? 0),
    isRecurring: addOn.isRecurring,
    appliesToAllPlans: addOn.appliesToAllPlans,
    isStackable: addOn.isStackable,
    quantityStep: String(addOn.quantityStep ?? 1),
    maxQuantity: addOn.maxQuantity == null ? '' : String(addOn.maxQuantity),
    status: addOn.status,
    compatiblePlanCodesText: (addOn.compatiblePlanCodes ?? []).join(', '),
    grantEntitlementsJson: JSON.stringify(addOn.grantEntitlements ?? {}, null, 2),
    originalPriceGbp: addOn.originalPriceGbp == null ? '' : String(addOn.originalPriceGbp),
    addonKind: addOn.addonKind ?? '',
    requiresEligibleParent: addOn.requiresEligibleParent ?? false,
    eligibilityFlag: addOn.eligibilityFlag ?? '',
    lettersGranted: String(addOn.lettersGranted ?? 0),
    sessionsGranted: String(addOn.sessionsGranted ?? 0),
  };
}

export function toCouponForm(coupon: AdminBillingCoupon): BillingCouponFormState {
  return {
    code: coupon.code,
    name: coupon.name,
    description: coupon.description ?? '',
    discountType: coupon.discountType,
    discountValue: String(coupon.discountValue ?? 0),
    currency: coupon.currency ?? 'AUD',
    startsAt: toLocalDateTimeValue(coupon.startsAt),
    endsAt: toLocalDateTimeValue(coupon.endsAt),
    usageLimitTotal: coupon.usageLimitTotal == null ? '' : String(coupon.usageLimitTotal),
    usageLimitPerUser: coupon.usageLimitPerUser == null ? '' : String(coupon.usageLimitPerUser),
    minimumSubtotal: coupon.minimumSubtotal == null ? '' : String(coupon.minimumSubtotal),
    isStackable: coupon.isStackable,
    status: coupon.status,
    applicablePlanCodesText: (coupon.applicablePlanCodes ?? []).join(', '),
    applicableAddOnCodesText: (coupon.applicableAddOnCodes ?? []).join(', '),
    notes: coupon.notes ?? '',
    couponVariant: coupon.couponVariant ?? 'percent_off',
    variantMetadataJson: coupon.variantMetadataJson ?? '',
  };
}

// Used only while the live payment-gateway catalog (listAdminPaymentGateways) is still
// loading, came back empty, or errored — the gateway filter options normally derive from
// that fetched catalog so a newly-registered PaymentGatewayNames entry shows up on its own.
export const FALLBACK_GATEWAY_OPTIONS: { id: string; label: string }[] = [
  { id: 'whop', label: 'Whop' },
  { id: 'fawaterak', label: 'Fawaterak' },
  { id: 'stripe', label: 'Stripe' },
  { id: 'paypal', label: 'PayPal' },
  { id: 'paymob', label: 'Paymob' },
  { id: 'paytabs', label: 'PayTabs' },
  { id: 'checkoutcom', label: 'Checkout.com' },
  { id: 'easykash', label: 'EasyKash' },
];
