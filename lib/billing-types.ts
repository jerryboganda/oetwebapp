export interface BillingEntitlements {
  productiveSkillReviewsEnabled: boolean;
  supportedReviewSubtests: string[];
  invoiceDownloadsAvailable: boolean;
}

export interface BillingPlan {
  id: string;
  code: string;
  label: string;
  tier: string;
  description: string;
  price: string;
  interval: string;
  reviewCredits: number;
  canChangeTo: boolean;
  changeDirection: 'current' | 'upgrade' | 'downgrade';
  badge: string;
  status: string;
  durationMonths: number;
  isVisible: boolean;
  isRenewable: boolean;
  trialDays: number;
  displayOrder: number;
  includedSubtests: string[];
  entitlements: Record<string, unknown>;
}

export interface BillingAddOn {
  id: string;
  code: string;
  name: string;
  productType: string;
  quantity: number;
  price: string;
  currency: string;
  interval: string;
  status: string;
  description: string;
  grantCredits: number;
  durationDays: number;
  isRecurring: boolean;
  appliesToAllPlans: boolean;
  quantityStep: number;
  maxQuantity: number | null;
  compatiblePlanCodes: string[];
}

export interface BillingCoupon {
  id: string;
  code: string;
  name: string;
  description: string;
  discountType: string;
  discountValue: number;
  currency: string;
  startsAt: string | null;
  endsAt: string | null;
  usageLimitTotal: number | null;
  usageLimitPerUser: number | null;
  minimumSubtotal: number | null;
  isStackable: boolean;
  status: string;
  redemptionCount: number;
  applicablePlanCodes: string[];
  applicableAddOnCodes: string[];
}

export interface BillingQuoteLineItem {
  kind: string;
  code: string;
  name: string;
  amount: number;
  currency: string;
  quantity: number;
  description?: string | null;
}

export interface BillingQuote {
  quoteId: string;
  status: string;
  currency: string;
  subtotalAmount: number;
  discountAmount: number;
  totalAmount: number;
  planCode: string | null;
  couponCode: string | null;
  addOnCodes: string[];
  items: BillingQuoteLineItem[];
  expiresAt: string;
  summary: string;
  validation: Record<string, unknown>;
}

export interface Invoice {
  id: string;
  date: string;
  amount: string;
  status: 'Paid' | 'Pending' | 'Failed';
  currency?: string;
  downloadUrl?: string;
  description?: string;
}

export interface BillingChangePreview {
  currentPlanId: string;
  targetPlanId: string;
  direction: 'upgrade' | 'downgrade';
  proratedAmount: string;
  effectiveAt: string;
  summary: string;
  currentCreditsIncluded: number;
  targetCreditsIncluded: number;
}

export interface BillingData {
  currentPlan: string;
  currentPlanId: string;
  currentPlanCode: string;
  planName: string;
  planDescription: string;
  price: string;
  interval: string;
  status: string;
  nextRenewal: string | null;
  reviewCredits: number;
  activeAddOns: BillingAddOn[];
  entitlements: BillingEntitlements;
  plans: BillingPlan[];
  addOns: BillingAddOn[];
  coupons: BillingCoupon[];
  quote: BillingQuote | null;
  invoices: Invoice[];
}

export type BillingProductType = 'review_credits' | 'plan_upgrade' | 'plan_downgrade' | 'addon_purchase';

export interface WalletTransactionDto {
  id: string;
  type: string;
  amount: number;
  balanceAfter: number;
  referenceType?: string | null;
  referenceId?: string | null;
  description?: string | null;
  createdAt: string;
}

export interface WalletData {
  balance: number;
  lastUpdatedAt?: string;
  transactions: WalletTransactionDto[];
}
