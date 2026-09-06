import { apiRequest } from './client';
import { normalizeRouteValues } from './route-normalizer';

/**
 * Dashboard home + wallet top-up + payment gateways + PayPal embedded checkout + capture.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export interface DashboardHomeResponse {
  freeze?: { currentFreeze?: unknown };
  cards?: {
    examDate?: { value?: string };
    pendingExpertReviews?: { count?: number };
    nextMockRecommendation?: unknown;
  };
  [key: string]: unknown;
}

export async function fetchDashboardHome(): Promise<DashboardHomeResponse> {
  const data = await apiRequest<DashboardHomeResponse>('/v1/learner/dashboard');
  return normalizeRouteValues(data) as DashboardHomeResponse;
}

export interface EngagementResponse {
  currentStreak?: number;
  longestStreak?: number;
  lastPracticeDate?: string | null;
  totalPracticeMinutes?: number;
  totalPracticeSessions?: number;
  avgSessionMinutes?: number;
  weeklyActivity?: { day: string; active: boolean }[];
  streakFreezeAvailable?: boolean;
  streakFreezeUsedThisWeek?: boolean;
}

export async function fetchEngagement(): Promise<EngagementResponse> {
  return apiRequest<EngagementResponse>('/v1/learner/engagement');
}

export interface WalletTransactionsResponse {
  balance: number;
  lastUpdatedAt?: string;
  transactions: unknown[];
}

export async function fetchWalletTransactions(limit = 20): Promise<WalletTransactionsResponse> {
  return apiRequest<WalletTransactionsResponse>(`/v1/billing/wallet/transactions?limit=${limit}`);
}

export interface WalletTopUpResponse {
  /** PayPal order id (embedded flow) / provider session id. Use as the embedded createOrder result. */
  sessionId?: string;
  gateway?: string;
  /** Present for redirect gateways (Stripe and the hosted fallback); absent for embedded PayPal. */
  checkoutUrl?: string;
  totalCredits?: number;
  status?: string;
}

export async function createWalletTopUp(
  amount: number,
  gateway: string,
  idempotencyKey?: string,
): Promise<WalletTopUpResponse> {
  return apiRequest<WalletTopUpResponse>('/v1/billing/wallet/top-up', {
    method: 'POST',
    body: JSON.stringify({ amount, gateway, idempotencyKey: idempotencyKey ?? null }),
  });
}

export interface WalletTopUpTier {
  amount: number;
  credits: number;
  bonus: number;
  totalCredits: number;
  label: string;
  isPopular: boolean;
}

export interface WalletTopUpTiersResponse {
  currency: string;
  tiers: WalletTopUpTier[];
}

export async function fetchWalletTopUpTiers(): Promise<WalletTopUpTiersResponse> {
  return apiRequest<WalletTopUpTiersResponse>('/v1/billing/wallet/top-up-tiers');
}

/** How a payment method initiates: an in-page SDK ("embedded", e.g. PayPal) or a
 *  hosted-checkout redirect ("redirect", e.g. Stripe / Checkout.com / Paymob / PayTabs). */
export type PaymentMethodMode = 'embedded' | 'iframe' | 'redirect';

export interface PaymentMethodOption {
  /** Gateway name passed back to checkout / top-up (e.g. "whop", "fawaterak"). */
  name: string;
  /** Learner-facing label for the method. */
  label: string;
  /** Icon hint (e.g. "credit-card", "paypal", "wallet"). */
  iconName: string;
  /** "embedded"/"iframe" stay on-site; "redirect" opens a hosted checkout. */
  mode: PaymentMethodMode;
  badge?: string | null;
  recommended?: boolean;
  region?: string;
}

export interface AvailablePaymentGatewaysResponse {
  gateways: string[];
  /** Rich metadata for the unified payment-method picker. Absent on older API builds. */
  methods?: PaymentMethodOption[];
}

export async function fetchAvailablePaymentGateways(): Promise<AvailablePaymentGatewaysResponse> {
  return apiRequest<AvailablePaymentGatewaysResponse>('/v1/billing/payment-gateways');
}

// ── PayPal Expanded (embedded) checkout ──────────────────────────────────────
export interface PayPalClientConfig {
  /** False when no client id is configured — the embedded UI is unavailable and the
   *  caller should fall back to the redirect flow. */
  enabled: boolean;
  /** Public PayPal client id for the browser SDK (never the secret). */
  clientId: string | null;
  currency: string;
  intent: string;
  components: string;
  environment: 'sandbox' | 'live' | string;
  /** Whether embedded Advanced Card Fields may render; when false, show buttons only. */
  advancedCardsEnabled: boolean;
}

export async function fetchPayPalClientConfig(): Promise<PayPalClientConfig> {
  return apiRequest<PayPalClientConfig>('/v1/billing/paypal/client-config');
}

export interface PaymentCaptureResult {
  status: 'completed' | 'failed' | 'pending' | string;
  orderId: string;
  captureId: string | null;
  redirectTo: string | null;
  failureReason: string | null;
}

/**
 * Resolves a safe in-app destination from a server-supplied `redirectTo`. Only same-origin
 * absolute paths are honoured: a value must start with a single `/` (not `//`, which is a
 * protocol-relative off-site URL, and not a `/\` backslash variant). Anything else falls
 * back to the provided default. Use this for every PayPal capture redirect.
 */
export function safePaymentRedirect(redirectTo: string | null | undefined, fallback: string): string {
  if (
    typeof redirectTo === 'string' &&
    redirectTo.startsWith('/') &&
    !redirectTo.startsWith('//') &&
    !redirectTo.startsWith('/\\')
  ) {
    return redirectTo;
  }
  return fallback;
}

/** Captures an approved PayPal order for the quote/wallet billing flow (onApprove). */
export async function captureBillingCheckout(orderId: string): Promise<PaymentCaptureResult> {
  return apiRequest<PaymentCaptureResult>(
    `/v1/billing/checkout-sessions/${encodeURIComponent(orderId)}/capture`,
    { method: 'POST' },
  );
}
