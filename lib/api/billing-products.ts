/**
 * Admin billing products catalog, refunds, analytics — extracted from
 * `lib/api.ts`. Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest, maybe } from './client';

export interface AdminBillingProductPrice {
  priceId: string;
  amount: number;
  currency: string;
  interval: string;
  trialDays?: number;
  isDefault?: boolean;
}

export interface AdminBillingProduct {
  productCode: string;
  name: string;
  description?: string | null;
  productType: string;
  status: string;
  imageUrl?: string | null;
  displayOrder?: number;
  prices: AdminBillingProductPrice[];
  metadata?: Record<string, unknown>;
}

export async function fetchAdminBillingProducts(params?: { status?: string; search?: string }): Promise<AdminBillingProduct[]> {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.search) qs.set('search', params.search);
  const q = qs.toString();
  const result = await maybe<{ items: AdminBillingProduct[] } | AdminBillingProduct[]>(
    apiRequest(`/v1/admin/billing/products${q ? `?${q}` : ''}`),
    [],
  );
  if (!result) return [];
  if (Array.isArray(result)) return result;
  return result.items ?? [];
}

export async function fetchAdminBillingProduct(productCode: string): Promise<AdminBillingProduct | null> {
  return maybe<AdminBillingProduct>(
    apiRequest<AdminBillingProduct>(`/v1/admin/billing/products/${encodeURIComponent(productCode)}`),
  );
}

export async function updateAdminBillingProduct(productCode: string, payload: Partial<AdminBillingProduct>): Promise<AdminBillingProduct> {
  return apiRequest<AdminBillingProduct>(`/v1/admin/billing/products/${encodeURIComponent(productCode)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export interface AdminRefundRequest {
  id: string;
  invoiceId: string;
  userId: string;
  userName: string;
  amount: number;
  currency: string;
  reason: string;
  status: 'pending' | 'approved' | 'denied' | 'issued' | string;
  requestedAt: string;
  reviewedAt?: string | null;
  reviewerNotes?: string | null;
}

export async function fetchAdminRefunds(params?: { status?: string; page?: number; pageSize?: number }): Promise<{ items: AdminRefundRequest[]; total: number }> {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  const result = await maybe<{ items: AdminRefundRequest[]; total?: number } | AdminRefundRequest[]>(
    apiRequest(`/v1/admin/refunds${q ? `?${q}` : ''}`),
    null,
  );
  if (result == null) return { items: [], total: 0 };
  if (Array.isArray(result)) return { items: result, total: result.length };
  return { items: result.items ?? [], total: result.total ?? (result.items ?? []).length };
}

export async function postAdminRefundAction(payload: { refundId: string; action: 'approve' | 'deny' | 'issue'; notes?: string | null; amount?: number | null }): Promise<AdminRefundRequest> {
  return apiRequest<AdminRefundRequest>('/v1/admin/refunds', {
    method: 'POST',
    body: JSON.stringify({
      refundId: payload.refundId,
      action: payload.action,
      notes: payload.notes ?? null,
      amount: payload.amount ?? null,
    }),
  });
}

export interface AdminBillingAnalyticsSeriesPoint {
  date: string;
  value: number;
}

export interface AdminBillingAnalyticsResponse {
  mrr: AdminBillingAnalyticsSeriesPoint[];
  churnRate: AdminBillingAnalyticsSeriesPoint[];
  ltv: AdminBillingAnalyticsSeriesPoint[];
  currency: string;
  available: boolean;
}

export async function fetchAdminBillingAnalytics(params?: { from?: string; to?: string }): Promise<AdminBillingAnalyticsResponse> {
  const qs = new URLSearchParams();
  if (params?.from) qs.set('from', params.from);
  if (params?.to) qs.set('to', params.to);
  const q = qs.toString();
  const result = await maybe<AdminBillingAnalyticsResponse>(
    apiRequest<AdminBillingAnalyticsResponse>(`/v1/admin/billing/analytics${q ? `?${q}` : ''}`),
    null,
  );
  if (result) return { ...result, available: true };
  return { mrr: [], churnRate: [], ltv: [], currency: 'AUD', available: false };
}
