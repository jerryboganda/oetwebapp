/**
 * OET 2026 catalog, entitlement snapshot, addon quoting — extracted from
 * `lib/api.ts`. Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest } from './client';
import type {
  AdminCatalogAddOnSummary,
  AdminCatalogPlanSummary,
  CatalogCardPresentation,
  CatalogPresentation,
  CatalogPresentationRevisions,
  CatalogStorefrontConfig,
  PackageCommercialUpdate,
  PublicCatalogResponseWithPresentation,
  WebsitePackagesPresentation,
} from '../catalog-presentation';
import type {
  AddonQuoteResponse,
  EligibilityMatrixResponse,
} from '../types/admin';

export async function fetchPublicCatalog(): Promise<PublicCatalogResponseWithPresentation> {
  return apiRequest('/v1/catalog/pricing');
}

export interface AdminCatalogPresentationResponse {
  planCodes: string[];
  addOnCodes: string[];
  presentation: CatalogPresentation | null;
  revisions: CatalogPresentationRevisions;
  plans: AdminCatalogPlanSummary[];
  addOns: AdminCatalogAddOnSummary[];
}

export async function fetchAdminCatalogPresentation(): Promise<AdminCatalogPresentationResponse> {
  return apiRequest('/v1/admin/billing/catalog/presentation');
}

export interface SaveWebsitePackagesResult {
  presentation: CatalogPresentation | null;
  revisions: CatalogPresentationRevisions;
  /** Format-valid codes the server ignored because no plan or add-on carries them. */
  droppedCodes: string[];
  plans: AdminCatalogPlanSummary[];
  addOns: AdminCatalogAddOnSummary[];
  commercial: Array<{ kind: 'plan' | 'addon'; code: string; changed: boolean }>;
  /** Overlay codes whose live billing rows (name, description, features) this save wrote or restored. */
  mirroredCodes: string[];
}

export interface SaveStorefrontResult {
  presentation: CatalogPresentation | null;
  revisions: CatalogPresentationRevisions;
  droppedCodes: string[];
}

/**
 * Saves ONLY the website-packages section (plus any dirty billing rows, in the
 * same transaction). A stale `expectedRevision` is rejected with a 409
 * `catalog_presentation_conflict`. Not retried: replaying a save whose reply
 * was lost would fail against its own new revision and look like a conflict.
 */
export async function saveAdminWebsitePackages(input: {
  expectedRevision: string;
  websitePackages: WebsitePackagesPresentation;
  commercialUpdates?: PackageCommercialUpdate[];
}): Promise<SaveWebsitePackagesResult> {
  return apiRequest(
    '/v1/admin/billing/catalog/website-packages',
    { method: 'PUT', body: JSON.stringify(input) },
    { maxRetries: 0 },
  );
}

/** Saves ONLY the storefront and per-card presentation; website packages are untouched. Not retried, like the packages save. */
export async function saveAdminStorefront(input: {
  expectedRevision: string;
  storefront: Partial<CatalogStorefrontConfig>;
  byCode: Record<string, CatalogCardPresentation>;
}): Promise<SaveStorefrontResult> {
  return apiRequest(
    '/v1/admin/billing/catalog/storefront',
    { method: 'PUT', body: JSON.stringify(input) },
    { maxRetries: 0 },
  );
}

/**
 * @deprecated Whole-document save kept for stale browser tabs. Use
 * `saveAdminWebsitePackages` or `saveAdminStorefront`; no callers remain.
 */
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
