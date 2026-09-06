/**
 * Admin rulebooks + soft-publish/unarchive/bulk paper ops — extracted from
 * `lib/api.ts`. Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest } from './client';

// -- Admin: Rulebook Management ------------------------------------------
export interface AdminRulebookSummary {
  id: string;
  kind: string;
  profession: string;
  version: string;
  status: string;
  authoritySource: string;
  referencePdfAssetId: string | null;
  sectionCount: number;
  ruleCount: number;
  updatedByUserId: string | null;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
}
export interface AdminRulebookSection { id: string; code: string; title: string; orderIndex: number; }
export interface AdminRulebookRule {
  id: string; code: string; sectionCode: string; title: string; body: string; severity: string;
  appliesToJson: string; turnStage: string | null;
  exemplarPhrasesJson: string | null; forbiddenPatternsJson: string | null;
  checkId: string | null; paramsJson: string | null; examplesJson: string | null;
  orderIndex: number;
}
export interface AdminRulebookDetail extends Omit<AdminRulebookSummary, 'sectionCount' | 'ruleCount' | 'updatedByUserId'> {
  sections: AdminRulebookSection[];
  rules: AdminRulebookRule[];
}

export async function adminListRulebooks(filter?: { kind?: string; profession?: string }) {
  const p = new URLSearchParams();
  if (filter?.kind) p.set('kind', filter.kind);
  if (filter?.profession) p.set('profession', filter.profession);
  const qs = p.toString();
  return apiRequest<AdminRulebookSummary[]>(`/v1/admin/rulebooks${qs ? `?${qs}` : ''}`);
}
export async function adminGetRulebook(id: string) {
  return apiRequest<AdminRulebookDetail>(`/v1/admin/rulebooks/${encodeURIComponent(id)}`);
}
export async function adminUpdateRulebookMeta(id: string, body: { version?: string | null; authoritySource?: string | null }) {
  return apiRequest<AdminRulebookDetail>(`/v1/admin/rulebooks/${encodeURIComponent(id)}`, {
    method: 'PUT', body: JSON.stringify(body),
  });
}
export async function adminPublishRulebook(id: string, versionLabel?: string | null) {
  return apiRequest<AdminRulebookDetail>(`/v1/admin/rulebooks/${encodeURIComponent(id)}/publish`, {
    method: 'POST', body: JSON.stringify({ versionLabel: versionLabel ?? null }),
  });
}
export async function adminCreateRulebookSection(id: string, body: { code: string; title: string; orderIndex?: number | null }) {
  return apiRequest<AdminRulebookSection>(`/v1/admin/rulebooks/${encodeURIComponent(id)}/sections`, {
    method: 'POST', body: JSON.stringify(body),
  });
}
export async function adminUpdateRulebookSection(id: string, sectionId: string, body: { title?: string | null; orderIndex?: number | null }) {
  return apiRequest<AdminRulebookSection>(`/v1/admin/rulebooks/${encodeURIComponent(id)}/sections/${encodeURIComponent(sectionId)}`, {
    method: 'PUT', body: JSON.stringify(body),
  });
}
export async function adminDeleteRulebookSection(id: string, sectionId: string) {
  return apiRequest(`/v1/admin/rulebooks/${encodeURIComponent(id)}/sections/${encodeURIComponent(sectionId)}`, { method: 'DELETE' });
}
export async function adminCreateRulebookRule(id: string, body: {
  code: string; sectionCode: string; title: string; body: string; severity: string;
  appliesToJson?: string | null; turnStage?: string | null;
  exemplarPhrasesJson?: string | null; forbiddenPatternsJson?: string | null;
  checkId?: string | null; paramsJson?: string | null; examplesJson?: string | null;
  orderIndex?: number | null;
}) {
  return apiRequest<AdminRulebookRule>(`/v1/admin/rulebooks/${encodeURIComponent(id)}/rules`, {
    method: 'POST', body: JSON.stringify(body),
  });
}
export async function adminUpdateRulebookRule(id: string, ruleId: string, body: Partial<{
  sectionCode: string; title: string; body: string; severity: string;
  appliesToJson: string | null; turnStage: string | null;
  exemplarPhrasesJson: string | null; forbiddenPatternsJson: string | null;
  checkId: string | null; paramsJson: string | null; examplesJson: string | null;
  orderIndex: number | null;
}>) {
  return apiRequest<AdminRulebookRule>(`/v1/admin/rulebooks/${encodeURIComponent(id)}/rules/${encodeURIComponent(ruleId)}`, {
    method: 'PUT', body: JSON.stringify(body),
  });
}
export async function adminDeleteRulebookRule(id: string, ruleId: string) {
  return apiRequest(`/v1/admin/rulebooks/${encodeURIComponent(id)}/rules/${encodeURIComponent(ruleId)}`, { method: 'DELETE' });
}
export interface AdminRulebookMetadata { kinds: string[]; professions: string[]; severities: string[]; statuses: string[]; }

export async function adminGetRulebookMetadata() {
  return apiRequest<AdminRulebookMetadata>('/v1/admin/rulebooks/_metadata');
}
export async function adminCreateRulebook(body: { kind: string; profession: string; version: string; authoritySource?: string | null }) {
  return apiRequest<AdminRulebookDetail>('/v1/admin/rulebooks', { method: 'POST', body: JSON.stringify(body) });
}
export async function adminCloneRulebook(id: string, body: { version?: string | null; kind?: string | null; profession?: string | null; authoritySource?: string | null }) {
  return apiRequest<AdminRulebookDetail>(`/v1/admin/rulebooks/${encodeURIComponent(id)}/clone`, {
    method: 'POST', body: JSON.stringify(body),
  });
}
export async function adminUnpublishRulebook(id: string) {
  return apiRequest<AdminRulebookDetail>(`/v1/admin/rulebooks/${encodeURIComponent(id)}/unpublish`, { method: 'POST' });
}
export async function adminDeleteRulebook(id: string) {
  return apiRequest<AdminRulebookDetail>(`/v1/admin/rulebooks/${encodeURIComponent(id)}`, { method: 'DELETE' });
}
export async function adminExportRulebook(id: string) {
  return apiRequest<unknown>(`/v1/admin/rulebooks/${encodeURIComponent(id)}/export`);
}
export async function adminImportRulebook(json: string, mode: 'create' | 'replace') {
  return apiRequest<AdminRulebookDetail>('/v1/admin/rulebooks/import', {
    method: 'POST', body: JSON.stringify({ json, mode }),
  });
}

// === SUBAGENT_BACKEND: admin-content-management START ===
// Wrappers for the soft-publish + unarchive + bulk endpoints added so the
// admin web UI can manage every content type without server-side gate
// restrictions. Publish endpoints now always succeed and surface any
// rulebook/structural problems as `warnings` instead of throwing.

export interface AdminPublishWithWarningsResponse {
  published: boolean;
  status?: string;
  warnings?: string[];
}

export interface AdminBulkPaperPublishResult {
  paperId: string;
  ok: boolean;
  warnings?: string[];
  error?: string;
}

export interface AdminBulkPaperStatusResult {
  paperId: string;
  ok: boolean;
  status?: string;
  error?: string;
}

export async function adminPublishPaperWithWarnings(paperId: string) {
  return apiRequest<AdminPublishWithWarningsResponse>(
    `/v1/admin/papers/${encodeURIComponent(paperId)}/publish`,
    { method: 'POST' },
  );
}

export async function adminUnarchivePaper(paperId: string) {
  return apiRequest<{ id: string; status: string }>(
    `/v1/admin/papers/${encodeURIComponent(paperId)}/unarchive`,
    { method: 'POST' },
  );
}

export async function adminUnarchiveMockBundle(bundleId: string) {
  return apiRequest<{ id: string; status: string }>(
    `/v1/admin/mock-bundles/${encodeURIComponent(bundleId)}/unarchive`,
    { method: 'POST' },
  );
}

export async function adminUnarchiveGrammarLesson(lessonId: string) {
  return apiRequest<{ id: string; status: string }>(
    `/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/unarchive`,
    { method: 'POST' },
  );
}

export async function adminUnarchiveConversationTemplate(templateId: string) {
  return apiRequest<{ id: string; status: string }>(
    `/v1/admin/conversation/templates/${encodeURIComponent(templateId)}/unarchive`,
    { method: 'POST' },
  );
}

export async function adminUnarchivePronunciationDrill(drillId: string) {
  return apiRequest<{ id: string; status: string }>(
    `/v1/admin/pronunciation/drills/${encodeURIComponent(drillId)}/unarchive`,
    { method: 'POST' },
  );
}

export async function adminBulkPublishPapers(paperIds: string[]) {
  return apiRequest<{ results: AdminBulkPaperPublishResult[] }>(
    `/v1/admin/papers/bulk-publish`,
    { method: 'POST', body: JSON.stringify({ paperIds }) },
  );
}

export async function adminBulkSetPaperStatus(
  paperIds: string[],
  targetStatus: 'Draft' | 'Published' | 'Archived',
) {
  return apiRequest<{ results: AdminBulkPaperStatusResult[] }>(
    `/v1/admin/papers/bulk-status`,
    { method: 'POST', body: JSON.stringify({ paperIds, targetStatus }) },
  );
}

export interface AdminConversationAiDraftPayload {
  profession: string;
  topic?: string;
  scenario?: string;
  durationSeconds?: number;
  taskType?: 'oet-roleplay' | 'oet-handover';
}

export interface AdminConversationAiDraftResult {
  title: string;
  taskTypeCode: string;
  profession: string;
  scenario: string;
  roleDescription: string;
  patientContext: string;
  expectedOutcomes: string;
  difficulty: string;
  estimatedDurationSeconds: number;
  objectives: string[];
  expectedRedFlags: string[];
  keyVocabulary: string[];
  warning?: string | null;
}

export async function adminConversationAiDraft(payload: AdminConversationAiDraftPayload) {
  return apiRequest<AdminConversationAiDraftResult>(
    `/v1/admin/conversation/templates/ai-draft`,
    { method: 'POST', body: JSON.stringify(payload) },
  );
}
// === SUBAGENT_BACKEND: admin-content-management END ===
