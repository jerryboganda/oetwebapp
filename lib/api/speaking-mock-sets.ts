/**
 * Admin speaking mock sets — extracted from `lib/api.ts`. Re-exported
 * there, so `@/lib/api` imports keep working.
 *
 * Wave 3 of docs/SPEAKING-MODULE-PLAN.md - admin CRUD for speaking
 * mock sets. Permissions reuse AdminContent* on the backend.
 */
import { apiRequest, asRecord, toStringArray, type ApiRecord } from './client';

export interface AdminSpeakingMockSetRow {
  mockSetId: string;
  title: string;
  description: string;
  professionId: string;
  difficulty: string;
  status: 'draft' | 'published' | 'archived';
  criteriaFocus: string[];
  tags: string[];
  sortOrder: number;
  rolePlay1: { contentId: string; title: string; isSpeaking: boolean };
  rolePlay2: { contentId: string; title: string; isSpeaking: boolean };
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
}

function mapAdminMockSetRow(rec: ApiRecord): AdminSpeakingMockSetRow {
  const r1 = asRecord(rec.rolePlay1);
  const r2 = asRecord(rec.rolePlay2);
  const status = rec.status === 'published' || rec.status === 'archived' ? rec.status : 'draft';
  return {
    mockSetId: typeof rec.mockSetId === 'string' ? rec.mockSetId : '',
    title: typeof rec.title === 'string' ? rec.title : '',
    description: typeof rec.description === 'string' ? rec.description : '',
    professionId: typeof rec.professionId === 'string' ? rec.professionId : 'nursing',
    difficulty: typeof rec.difficulty === 'string' ? rec.difficulty : 'core',
    status,
    criteriaFocus: toStringArray(rec.criteriaFocus),
    tags: toStringArray(rec.tags),
    sortOrder: typeof rec.sortOrder === 'number' ? rec.sortOrder : 0,
    rolePlay1: {
      contentId: typeof r1.contentId === 'string' ? r1.contentId : '',
      title: typeof r1.title === 'string' ? r1.title : '',
      isSpeaking: r1.isSpeaking === true,
    },
    rolePlay2: {
      contentId: typeof r2.contentId === 'string' ? r2.contentId : '',
      title: typeof r2.title === 'string' ? r2.title : '',
      isSpeaking: r2.isSpeaking === true,
    },
    createdAt: typeof rec.createdAt === 'string' ? rec.createdAt : '',
    updatedAt: typeof rec.updatedAt === 'string' ? rec.updatedAt : '',
    publishedAt: typeof rec.publishedAt === 'string' ? rec.publishedAt : null,
  };
}

export async function fetchAdminSpeakingMockSets(params?: { status?: string; professionId?: string }): Promise<AdminSpeakingMockSetRow[]> {
  const p = new URLSearchParams();
  if (params?.status) p.set('status', params.status);
  if (params?.professionId) p.set('professionId', params.professionId);
  const qs = p.toString();
  const json = await apiRequest<ApiRecord>(`/v1/admin/speaking/mock-sets${qs ? `?${qs}` : ''}`);
  const list = Array.isArray(json.mockSets) ? json.mockSets.map(asRecord) : [];
  return list.map(mapAdminMockSetRow);
}

export async function fetchAdminSpeakingMockSet(mockSetId: string): Promise<AdminSpeakingMockSetRow> {
  const json = await apiRequest<ApiRecord>(`/v1/admin/speaking/mock-sets/${encodeURIComponent(mockSetId)}`);
  return mapAdminMockSetRow(json);
}

export async function createAdminSpeakingMockSet(payload: {
  title: string;
  rolePlay1ContentId: string;
  rolePlay2ContentId: string;
  professionId?: string;
  description?: string;
  difficulty?: string;
  criteriaFocus?: string;
  tags?: string;
  sortOrder?: number;
}): Promise<AdminSpeakingMockSetRow> {
  const json = await apiRequest<ApiRecord>('/v1/admin/speaking/mock-sets', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
  return mapAdminMockSetRow(json);
}

export async function updateAdminSpeakingMockSet(mockSetId: string, payload: Record<string, unknown>): Promise<AdminSpeakingMockSetRow> {
  const json = await apiRequest<ApiRecord>(`/v1/admin/speaking/mock-sets/${encodeURIComponent(mockSetId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
  return mapAdminMockSetRow(json);
}

export async function publishAdminSpeakingMockSet(mockSetId: string): Promise<AdminSpeakingMockSetRow> {
  const json = await apiRequest<ApiRecord>(`/v1/admin/speaking/mock-sets/${encodeURIComponent(mockSetId)}/publish`, {
    method: 'POST',
  });
  return mapAdminMockSetRow(json);
}

/** Permanently deletes an archived speaking mock set + all learner mock sessions. system_admin only. */
export async function forceDeleteAdminSpeakingMockSet(mockSetId: string): Promise<void> {
  await apiRequest(`/v1/admin/speaking/mock-sets/${encodeURIComponent(mockSetId)}/force-delete`, { method: 'POST' });
}

export async function archiveAdminSpeakingMockSet(mockSetId: string): Promise<AdminSpeakingMockSetRow> {
  const json = await apiRequest<ApiRecord>(`/v1/admin/speaking/mock-sets/${encodeURIComponent(mockSetId)}/archive`, {
    method: 'POST',
  });
  return mapAdminMockSetRow(json);
}

// Admin helper: list all speaking ContentItem rows the curator can pick
// from when building a mock set. Limited to the existing /v1/admin/content
// endpoint so permissions and filtering stay in one place.
export async function fetchAdminSpeakingContentOptions(): Promise<Array<{ id: string; title: string; status: string }>> {
  const json = await apiRequest<ApiRecord>('/v1/admin/content?subtest=speaking&pageSize=200');
  const items = Array.isArray(json.items) ? json.items.map(asRecord) : [];
  return items
    .filter((it) => typeof it.status === 'string' && it.status.toLowerCase() === 'published')
    .filter((it) => String(it.subtestCode ?? '').toLowerCase() === 'speaking')
    .map((it) => ({
      id: typeof it.id === 'string' ? it.id : '',
      title: typeof it.title === 'string' ? it.title : '',
      status: typeof it.status === 'string' ? it.status : 'draft',
    }));
}
