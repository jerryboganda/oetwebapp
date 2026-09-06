import { apiRequest, normalizeBillingCode } from './client';

/**
 * Admin billing coupons CRUD + subscriptions listing.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export async function fetchAdminBillingCoupons(params?: { status?: string }) {
  const qs = params?.status ? `?status=${encodeURIComponent(params.status)}` : '';
  return apiRequest(`/v1/admin/billing/coupons${qs}`);
}

export async function fetchAdminBillingCouponVersions(couponId: string) {
  return apiRequest(`/v1/admin/billing/coupons/${encodeURIComponent(couponId)}/versions`);
}

export async function createAdminBillingCoupon(payload: {
  code?: string;
  name: string;
  description?: string;
  discountType: string;
  discountValue: number;
  currency?: string;
  startsAt?: string | null;
  endsAt?: string | null;
  usageLimitTotal?: number | null;
  usageLimitPerUser?: number | null;
  minimumSubtotal?: number | null;
  isStackable?: boolean;
  status?: string;
  applicablePlanCodesJson?: string;
  applicableAddOnCodesJson?: string;
  notes?: string | null;
}) {
  return apiRequest('/v1/admin/billing/coupons', {
    method: 'POST',
    body: JSON.stringify({
      code: payload.code ?? normalizeBillingCode(payload.name),
      name: payload.name,
      description: payload.description ?? '',
      discountType: payload.discountType,
      discountValue: payload.discountValue,
      currency: payload.currency ?? 'AUD',
      startsAt: payload.startsAt ?? null,
      endsAt: payload.endsAt ?? null,
      usageLimitTotal: payload.usageLimitTotal ?? null,
      usageLimitPerUser: payload.usageLimitPerUser ?? null,
      minimumSubtotal: payload.minimumSubtotal ?? null,
      isStackable: payload.isStackable ?? true,
      status: payload.status ?? 'active',
      applicablePlanCodesJson: payload.applicablePlanCodesJson ?? '[]',
      applicableAddOnCodesJson: payload.applicableAddOnCodesJson ?? '[]',
      notes: payload.notes ?? null,
    }),
  });
}

export async function updateAdminBillingCoupon(couponId: string, payload: {
  code: string;
  name: string;
  description?: string;
  discountType: string;
  discountValue: number;
  currency?: string;
  startsAt?: string | null;
  endsAt?: string | null;
  usageLimitTotal?: number | null;
  usageLimitPerUser?: number | null;
  minimumSubtotal?: number | null;
  isStackable?: boolean;
  status?: string;
  applicablePlanCodesJson?: string;
  applicableAddOnCodesJson?: string;
  notes?: string | null;
}) {
  return apiRequest(`/v1/admin/billing/coupons/${encodeURIComponent(couponId)}`, {
    method: 'PUT',
    body: JSON.stringify({
      code: payload.code,
      name: payload.name,
      description: payload.description ?? '',
      discountType: payload.discountType,
      discountValue: payload.discountValue,
      currency: payload.currency ?? 'AUD',
      startsAt: payload.startsAt ?? null,
      endsAt: payload.endsAt ?? null,
      usageLimitTotal: payload.usageLimitTotal ?? null,
      usageLimitPerUser: payload.usageLimitPerUser ?? null,
      minimumSubtotal: payload.minimumSubtotal ?? null,
      isStackable: payload.isStackable ?? true,
      status: payload.status ?? 'active',
      applicablePlanCodesJson: payload.applicablePlanCodesJson ?? '[]',
      applicableAddOnCodesJson: payload.applicableAddOnCodesJson ?? '[]',
      notes: payload.notes ?? null,
    }),
  });
}

export async function fetchAdminBillingSubscriptions(params?: { status?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/billing/subscriptions${q ? `?${q}` : ''}`);
}
