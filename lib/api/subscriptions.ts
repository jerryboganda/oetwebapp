/**
 * Learner subscription self-service — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest, maybe } from './client';

export interface SubscriptionMe {
  subscriptionId: string;
  status: string;
  planCode: string;
  planName: string;
  price: number;
  currency: string;
  interval: string;
  startedAt: string | null;
  nextRenewalAt: string | null;
  cancelledAt: string | null;
  pausedUntil: string | null;
  cancelAtPeriodEnd: boolean;
  trialEndsAt: string | null;
  walletBalance?: number;
  walletCurrency?: string;
  productCategory?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  durationDays?: number;
  remainingDays?: number;
  expiringSoon?: boolean;
  totalFreezeDaysUsed?: number;
  maxFreezeDays?: number;
  freezeAllowanceRemaining?: number;
  preservedRemainingDays?: number | null;
  pendingFreezeRequestDate?: string | null;
  frozenSince?: string | null;
}

export async function fetchSubscriptionMe(): Promise<SubscriptionMe | null> {
  return maybe<SubscriptionMe>(apiRequest<SubscriptionMe>('/v1/subscriptions/me'));
}

export interface SubscriptionMeListItem extends SubscriptionMe {
  productCategory?: string | null;
}

export async function fetchSubscriptionsMe(): Promise<SubscriptionMeListItem[]> {
  const result = await maybe<{ items: SubscriptionMeListItem[] } | SubscriptionMeListItem[]>(
    apiRequest('/v1/subscriptions/me/list'),
    [],
  );
  if (!result) {
    const one = await fetchSubscriptionMe();
    return one ? [one] : [];
  }
  if (Array.isArray(result)) return result;
  return result.items ?? [];
}

export async function createSubscriptionPortalSession(returnUrl?: string): Promise<{ url: string }> {
  return apiRequest<{ url: string }>('/v1/subscriptions/me/portal-session', {
    method: 'POST',
    body: JSON.stringify({ returnUrl: returnUrl ?? null }),
  });
}

export async function cancelSubscription(reason?: string): Promise<SubscriptionMe> {
  return apiRequest<SubscriptionMe>('/v1/subscriptions/me/cancel', {
    method: 'POST',
    body: JSON.stringify({ reason: reason ?? null }),
  });
}

export async function pauseSubscriptionSelf(days?: number, reason?: string): Promise<SubscriptionMe> {
  return apiRequest<SubscriptionMe>('/v1/subscriptions/me/pause', {
    method: 'POST',
    body: JSON.stringify({ days: days ?? null, reason: reason ?? null }),
  });
}

export async function resumeSubscriptionSelf(): Promise<SubscriptionMe> {
  return apiRequest<SubscriptionMe>('/v1/subscriptions/me/resume', { method: 'POST' });
}

export async function requestSubscriptionFreeze(subscriptionId: string): Promise<SubscriptionMe> {
  return apiRequest<SubscriptionMe>(`/v1/subscriptions/${encodeURIComponent(subscriptionId)}/request-freeze`, {
    method: 'POST',
  });
}

export async function resumeSubscriptionById(subscriptionId: string): Promise<SubscriptionMe> {
  return apiRequest<SubscriptionMe>(`/v1/subscriptions/${encodeURIComponent(subscriptionId)}/resume`, {
    method: 'POST',
  });
}

export async function changeSubscriptionPlanSelf(planCode: string, prorate?: boolean): Promise<SubscriptionMe> {
  return apiRequest<SubscriptionMe>('/v1/subscriptions/me/change-plan', {
    method: 'POST',
    body: JSON.stringify({ planCode, prorate: prorate ?? true }),
  });
}

export interface SubscriptionInvoice {
  invoiceId: string;
  number?: string | null;
  date: string;
  amount: number;
  currency: string;
  status: string;
  description?: string | null;
  pdfUrl?: string | null;
  hostedInvoiceUrl?: string | null;
}

export async function fetchSubscriptionInvoices(params?: { page?: number; pageSize?: number }): Promise<{ items: SubscriptionInvoice[]; total: number; page: number; pageSize: number }> {
  const qs = new URLSearchParams();
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  const result = await maybe<{ items: SubscriptionInvoice[]; total: number; page?: number; pageSize?: number } | SubscriptionInvoice[]>(
    apiRequest(`/v1/subscriptions/me/invoices${q ? `?${q}` : ''}`),
    null,
  );
  if (result == null) return { items: [], total: 0, page: params?.page ?? 1, pageSize: params?.pageSize ?? 20 };
  if (Array.isArray(result)) return { items: result, total: result.length, page: params?.page ?? 1, pageSize: params?.pageSize ?? 20 };
  return { items: result.items ?? [], total: result.total ?? (result.items ?? []).length, page: result.page ?? params?.page ?? 1, pageSize: result.pageSize ?? params?.pageSize ?? 20 };
}
