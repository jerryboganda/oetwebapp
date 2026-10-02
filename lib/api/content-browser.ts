/**
 * Content Hierarchy program browser + access-aware content browser —
 * extracted from `lib/api.ts`. Re-exported there, so `@/lib/api`
 * imports keep working.
 */
import type { ContentPackage, PaginatedResponse } from '@/lib/types/content-hierarchy';
import { apiRequest } from './client';

export async function fetchContentPrograms(params?: { type?: string; language?: string; page?: number; pageSize?: number }) {
  const p = new URLSearchParams();
  if (params?.type) p.set('type', params.type);
  if (params?.language) p.set('language', params.language);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/programs${qs ? `?${qs}` : ''}`);
}

export async function fetchContentProgram(programId: string) {
  return apiRequest(`/v1/programs/${encodeURIComponent(programId)}`);
}

export async function fetchContentTracks(programId: string) {
  return apiRequest(`/v1/programs/${encodeURIComponent(programId)}/tracks`);
}

type RawPackage = Record<string, unknown> & { comparisonFeatures?: unknown; comparisonFeaturesJson?: unknown };

/**
 * The packages API returns the stored entity, whose features are a JSON string
 * column (`comparisonFeaturesJson`), while `ContentPackage` promises a
 * `comparisonFeatures` array. Parse it here so every caller gets the array.
 */
function withComparisonFeatures(pkg: RawPackage): ContentPackage {
  if (Array.isArray(pkg.comparisonFeatures)) return pkg as unknown as ContentPackage;
  let comparisonFeatures: string[] = [];
  try {
    const parsed: unknown = JSON.parse(typeof pkg.comparisonFeaturesJson === 'string' ? pkg.comparisonFeaturesJson : '[]');
    if (Array.isArray(parsed)) comparisonFeatures = parsed.filter((feature): feature is string => typeof feature === 'string');
  } catch {
    // A malformed column means no features, not a crashed page.
  }
  return { ...pkg, comparisonFeatures } as unknown as ContentPackage;
}

export async function fetchContentPackages(params?: { type?: string; page?: number; pageSize?: number }): Promise<PaginatedResponse<ContentPackage>> {
  const p = new URLSearchParams();
  if (params?.type) p.set('type', params.type);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  const response = await apiRequest<Omit<PaginatedResponse<ContentPackage>, 'items'> & { items?: RawPackage[] }>(`/v1/packages${qs ? `?${qs}` : ''}`);
  return { ...response, items: Array.isArray(response?.items) ? response.items.map(withComparisonFeatures) : [] };
}

export async function fetchContentPackage(packageId: string): Promise<ContentPackage | null> {
  const pkg = await apiRequest<RawPackage | null>(`/v1/packages/${encodeURIComponent(packageId)}`);
  return pkg ? withComparisonFeatures(pkg) : pkg;
}

export async function fetchFreePreviewAssets() {
  return apiRequest('/v1/free-previews');
}

export async function fetchFoundationResources(type?: string) {
  return apiRequest(`/v1/foundation-resources${type ? `?type=${type}` : ''}`);
}

export async function fetchContentBrowser(params?: {
  subtest?: string; profession?: string; difficulty?: string; language?: string;
  provenance?: string; page?: number; pageSize?: number;
}) {
  const p = new URLSearchParams();
  if (params?.subtest) p.set('subtest', params.subtest);
  if (params?.profession) p.set('profession', params.profession);
  if (params?.difficulty) p.set('difficulty', params.difficulty);
  if (params?.language) p.set('language', params.language);
  if (params?.provenance) p.set('provenance', params.provenance);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/content-browser${qs ? `?${qs}` : ''}`);
}

export async function fetchContentAccess(contentId: string) {
  return apiRequest(`/v1/content-browser/${encodeURIComponent(contentId)}/access`);
}

export async function fetchProgramsBrowser(params?: { type?: string; language?: string; page?: number; pageSize?: number }) {
  const p = new URLSearchParams();
  if (params?.type) p.set('type', params.type);
  if (params?.language) p.set('language', params.language);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/programs-browser${qs ? `?${qs}` : ''}`);
}
