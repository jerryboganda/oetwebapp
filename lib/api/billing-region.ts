/**
 * Typed API client for the international billing expansion (Phases 1-10).
 *
 * Mirrors:
 *   - BillingRegionEndpoints   (profile, admin region pricing, gateway routes)
 *   - ManualPaymentEndpoints   (Phase 4)
 *   - ScholarshipEndpoints     (Phase 7)
 *   - AffiliateEndpoints       (Phase 8)
 *   - BillingMetricEndpoints   (Phase 10)
 *
 * Uses the shared apiClient from lib/api.ts so retry / CSRF / auth headers
 * stay consistent with the rest of the app.
 */
import { apiClient } from '@/lib/api';

export type BillingRegion = 'UK' | 'GULF' | 'EGYPT' | 'PK' | 'ROW';

export interface BillingProfile {
  country: string | null;
  preferredCurrency: string | null;
  preferredRegion: BillingRegion | null;
  detectedRegion: BillingRegion;
  detectedCountry: string;
  detectedCurrency: string;
  detectionSource: 'stored' | 'geo_header' | 'accept_language' | 'default';
}

export interface RegionDetection {
  region: BillingRegion;
  country: string;
  currency: string;
  source: string;
}

export interface BillingProfileUpdateRequest {
  country?: string | null;
  preferredCurrency?: string | null;
  preferredRegion?: BillingRegion | null;
}

export function fetchBillingProfile(): Promise<BillingProfile> {
  return apiClient.get<BillingProfile>('/v1/billing/profile');
}

export function updateBillingProfile(payload: BillingProfileUpdateRequest): Promise<BillingProfile> {
  return apiClient.put<BillingProfile>('/v1/billing/profile', payload);
}

export function detectBillingRegion(): Promise<RegionDetection> {
  return apiClient.get<RegionDetection>('/v1/billing/region');
}

// ── Admin: Region pricing ─────────────────────────────────────────

export interface RegionPricingDto {
  id: string;
  targetType: 'plan' | 'addon' | 'wallet_topup_tier';
  targetId: string;
  region: BillingRegion;
  currency: string;
  priceAmount: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface RegionPricingUpsertRequest {
  targetType: string;
  targetId: string;
  region: string;
  currency: string;
  priceAmount: number;
  isActive: boolean;
}

export function listRegionPricings(filters?: { targetType?: string; targetId?: string; region?: string }): Promise<RegionPricingDto[]> {
  const params = new URLSearchParams();
  if (filters?.targetType) params.set('targetType', filters.targetType);
  if (filters?.targetId) params.set('targetId', filters.targetId);
  if (filters?.region) params.set('region', filters.region);
  const qs = params.toString();
  return apiClient.get<RegionPricingDto[]>(`/v1/admin/billing/region-pricings${qs ? `?${qs}` : ''}`);
}

export function upsertRegionPricing(payload: RegionPricingUpsertRequest): Promise<RegionPricingDto> {
  return apiClient.post<RegionPricingDto>('/v1/admin/billing/region-pricings', payload);
}

export function deleteRegionPricing(id: string): Promise<void> {
  return apiClient.delete<void>(`/v1/admin/billing/region-pricings/${encodeURIComponent(id)}`);
}

// ── Admin: Gateway routing ───────────────────────────────────────

export interface GatewayRouteDto {
  id: string;
  region: string;
  currency: string;
  productType: string;
  gatewayName: string;
  priority: number;
  isEnabled: boolean;
}

export interface GatewayRouteUpsertRequest {
  region: string;
  currency: string;
  productType: string;
  gatewayName: string;
  priority: number;
  isEnabled: boolean;
}

export function listGatewayRoutes(): Promise<GatewayRouteDto[]> {
  return apiClient.get<GatewayRouteDto[]>('/v1/admin/billing/gateway-routes');
}

export function upsertGatewayRoute(payload: GatewayRouteUpsertRequest): Promise<GatewayRouteDto> {
  return apiClient.post<GatewayRouteDto>('/v1/admin/billing/gateway-routes', payload);
}

export function deleteGatewayRoute(id: string): Promise<void> {
  return apiClient.delete<void>(`/v1/admin/billing/gateway-routes/${encodeURIComponent(id)}`);
}
