/**
 * OET 2026 catalog, entitlement snapshot, addon quoting — extracted from
 * `lib/api.ts`. Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest } from './client';
import type { CatalogPresentation } from '../catalog-presentation';
import type {
  AddonQuoteResponse,
  EligibilityMatrixResponse,
  PublicCatalogResponse,
} from '../types/admin';

export async function fetchPublicCatalog(): Promise<PublicCatalogResponse> {
  return apiRequest('/v1/catalog/pricing');
}

export interface AdminCatalogPresentationResponse {
  planCodes: string[];
  addOnCodes: string[];
  presentation: CatalogPresentation | null;
}

export async function fetchAdminCatalogPresentation(): Promise<AdminCatalogPresentationResponse> {
  return apiRequest('/v1/admin/billing/catalog/presentation');
}

export async function saveAdminCatalogPresentation(
  presentation: CatalogPresentation | null,
): Promise<void> {
  await apiRequest('/v1/admin/billing/catalog/presentation', {
    method: 'PUT',
    body: JSON.stringify({ presentation }),
  });
}

export async function quoteAddonEligibility(addOnCode: string): Promise<AddonQuoteResponse> {
  return apiRequest('/v1/billing/quote/addon', {
    method: 'POST',
    body: JSON.stringify({ addOnCode }),
  });
}

export async function fetchEligibilityMatrix(): Promise<EligibilityMatrixResponse> {
  return apiRequest('/v1/admin/billing/eligibility/matrix');
}

export interface MyEntitlementSnapshot {
  hasEligibleSubscription: boolean;
  tier: string;
  planCode?: string | null;
  productCategory?: string | null;
  enabledModules: string[];
  writingAddonsEnabled: boolean;
  speakingAddonsEnabled: boolean;
  speakingPracticeAccessEnabled: boolean;
  tutorBookDiscountEnabled: boolean;
  writingAssessmentsRemaining: number;
  speakingSessionsRemaining: number;
  aiCreditsRemaining: number;
  tutorBookUnlocked: boolean;
  basicEnglishUnlocked: boolean;
  expiresAt?: string | null;
  isFrozen: boolean;
}

export async function fetchMyEntitlementSnapshot(): Promise<MyEntitlementSnapshot> {
  return apiRequest('/v1/me/entitlement-snapshot');
}

export interface Oet2026ReseedResponse {
  plansCreated: number;
  plansUpdated: number;
  addOnsCreated: number;
  addOnsUpdated: number;
  packagesCreated: number;
  packagesUpdated: number;
}

export async function reseedOet2026Catalog(): Promise<Oet2026ReseedResponse> {
  return apiRequest('/v1/admin/billing/catalog/seed-oet-2026', { method: 'POST' });
}
