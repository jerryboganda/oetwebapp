import type { AdminBillingEntitlementDiagnostics } from '@/lib/types/admin';

export type PageStatus = 'loading' | 'success' | 'empty' | 'error';
export type ToastState = { variant: 'success' | 'error'; message: string } | null;
export type CatalogHistoryKind = 'plan' | 'add_on' | 'coupon';
export type CatalogHistoryTarget = { kind: CatalogHistoryKind; id: string; name: string; code: string };

export type CatalogVersionMetadata = {
  activeVersionNumber?: number | null;
  latestVersionNumber?: number | null;
  versionCount?: number;
};
export type DiagnosticsLoadResult = { data: AdminBillingEntitlementDiagnostics | null; status: 'success' | 'error' };

export interface BillingPlanFormState {
  code: string;
  name: string;
  description: string;
  price: string;
  currency: string;
  interval: string;
  durationMonths: string;
  includedCredits: string;
  displayOrder: string;
  isVisible: boolean;
  isRenewable: boolean;
  trialDays: string;
  diagnosticMockEntitlement: string;
  status: string;
  includedSubtestsText: string;
  entitlementsJson: string;
  // OET 2026 catalog fields
  originalPriceGbp: string;
  accessDurationDays: string;
  writingAddonsEnabled: boolean;
  speakingAddonsEnabled: boolean;
  speakingPracticeAccessEnabled: boolean;
  tutorBookDiscountEnabled: boolean;
  profession: string;
  productCategory: string;
  dashboardModulesText: string;
  bundledWritingAssessments: string;
  bundledSpeakingSessions: string;
  bundledAiCredits: string;
  bundledTutorBook: boolean;
  bundledBasicEnglish: boolean;
  isDraft: boolean;
  extensionAllowed: boolean;
  recallUpdatesEnabled: boolean;
  // "What's included" — one bullet per line; persisted on the linked ContentPackage.
  comparisonFeaturesText: string;
  // Name/Description/"What's included" are owned by Subscriptions & Packages (read-only while editing).
  packageManaged: boolean;
}

export interface BillingAddOnFormState {
  code: string;
  name: string;
  description: string;
  price: string;
  currency: string;
  interval: string;
  durationDays: string;
  grantCredits: string;
  displayOrder: string;
  isRecurring: boolean;
  appliesToAllPlans: boolean;
  isStackable: boolean;
  quantityStep: string;
  maxQuantity: string;
  status: string;
  compatiblePlanCodesText: string;
  grantEntitlementsJson: string;
  // OET 2026 catalog fields
  originalPriceGbp: string;
  addonKind: string;
  requiresEligibleParent: boolean;
  eligibilityFlag: string;
  lettersGranted: string;
  sessionsGranted: string;
  // Name/Description are owned by Subscriptions & Packages (read-only while editing).
  packageManaged: boolean;
}

export interface BillingCouponFormState {
  code: string;
  name: string;
  description: string;
  discountType: 'percentage' | 'fixed';
  discountValue: string;
  currency: string;
  startsAt: string;
  endsAt: string;
  usageLimitTotal: string;
  usageLimitPerUser: string;
  minimumSubtotal: string;
  isStackable: boolean;
  status: string;
  applicablePlanCodesText: string;
  applicableAddOnCodesText: string;
  notes: string;
  couponVariant: string;
  variantMetadataJson: string;
}
