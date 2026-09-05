/**
 * Content Hierarchy program browser + access-aware content browser —
 * extracted from `lib/api.ts`. Re-exported there, so `@/lib/api`
 * imports keep working.
 */
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

export async function fetchContentPackages(params?: { type?: string; page?: number; pageSize?: number }) {
  const p = new URLSearchParams();
  if (params?.type) p.set('type', params.type);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/packages${qs ? `?${qs}` : ''}`);
}

export async function fetchContentPackage(packageId: string) {
  return apiRequest(`/v1/packages/${encodeURIComponent(packageId)}`);
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
