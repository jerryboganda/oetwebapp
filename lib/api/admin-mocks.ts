/**
 * Admin mocks QC pipeline (bundles, review stages, item retire/analysis,
 * leak + answer-key reports, analytics) — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 *
 * NOTE: the booking projection helpers live in `./mock-bookings`
 * (shared `mapMockBooking`).
 */
import { apiRequest } from './client';
import type { BulkActionResultDto } from '../types/admin';

export async function fetchAdminMockBundles(params?: { status?: string; mockType?: string; subtest?: string }) {
  const q = new URLSearchParams();
  if (params?.status) q.set('status', params.status);
  if (params?.mockType) q.set('mockType', params.mockType);
  if (params?.subtest) q.set('subtest', params.subtest);
  const qs = q.toString();
  return apiRequest(`/v1/admin/mock-bundles${qs ? `?${qs}` : ''}`);
}

export async function fetchAdminMockBundle(bundleId: string) {
  return apiRequest(`/v1/admin/mock-bundles/${encodeURIComponent(bundleId)}`);
}

export async function updateAdminMockBundle(bundleId: string, body: Record<string, unknown>) {
  return apiRequest(`/v1/admin/mock-bundles/${encodeURIComponent(bundleId)}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

export async function reorderAdminMockBundleSections(bundleId: string, sectionIds: string[]) {
  return apiRequest(`/v1/admin/mock-bundles/${encodeURIComponent(bundleId)}/sections/reorder`, {
    method: 'PUT',
    body: JSON.stringify({ sectionIds }),
  });
}

export async function fetchAdminMockItemAnalysis(params?: { bundleId?: string; paperId?: string }) {
  const q = new URLSearchParams();
  if (params?.bundleId) q.set('bundleId', params.bundleId);
  if (params?.paperId) q.set('paperId', params.paperId);
  const qs = q.toString();
  return apiRequest(`/v1/admin/mocks/item-analysis${qs ? `?${qs}` : ''}`);
}

/**
 * Canonical review stages mirrored from `MockBundleReviewStages` in the
 * backend domain. Ordered from earliest editorial pass to publish.
 */
export const MOCK_REVIEW_STAGES = [
  'academic',
  'medical',
  'language',
  'technical',
  'pilot',
  'published',
] as const;

export type MockReviewStage = (typeof MOCK_REVIEW_STAGES)[number];

/** Single transition row in a bundle's editorial pipeline history. */
export interface MockBundleReviewStageEntry {
  id: string;
  stage: MockReviewStage | string;
  notes: string;
  createdAt: string;
  resolvedAt: string | null;
  resolvedByAdminId: string | null;
}

/** Summary payload returned by the review-stage endpoints. */
export interface MockBundleReviewStageSummary {
  mockBundleId: string;
  currentStage: MockReviewStage | string | null;
  isPublished: boolean;
  publishedAt: string | null;
  transitions: MockBundleReviewStageEntry[];
}

/**
 * GET the editorial review-stage summary for a bundle. Drives the admin
 * Mocks Module Phase 6 "review pipeline" page.
 */
export async function fetchMockBundleReviewStage(
  bundleId: string,
): Promise<MockBundleReviewStageSummary> {
  return apiRequest<MockBundleReviewStageSummary>(
    `/v1/admin/mocks/bundles/${encodeURIComponent(bundleId)}/review-stage/summary`,
  );
}

/**
 * POST a stage transition. Backend enforces monotonic progression and
 * publishes the bundle when the target stage is `published`.
 */
export async function advanceMockBundleReviewStage(
  bundleId: string,
  body: { targetStage: MockReviewStage | string; notes?: string },
): Promise<MockBundleReviewStageSummary> {
  return apiRequest<MockBundleReviewStageSummary>(
    `/v1/admin/mocks/bundles/${encodeURIComponent(bundleId)}/review-stage/advance`,
    {
      method: 'POST',
      // Backend property name is `nextStage`; the frontend helper exposes
      // `targetStage` to match the spec wording. We bridge them here.
      body: JSON.stringify({ nextStage: body.targetStage, notes: body.notes ?? null }),
    },
  );
}

/** Envelope returned by PATCH /v1/admin/mocks/items/{itemId}. */
export interface MockItemRetireResponse {
  itemId: string;
  affectedSnapshots: number;
  retiredAt: string | null;
  reason: string | null;
  retiredByAdminId: string | null;
}

/**
 * Soft-retire a flagged mock item from the admin item-analysis dashboard.
 * The PATCH is idempotent — repeated calls with the same item id return the
 * cached envelope and do not re-emit audit.
 */
export async function retireMockItem(
  itemId: string,
  options?: { reason?: string; bundleId?: string },
): Promise<MockItemRetireResponse> {
  return apiRequest<MockItemRetireResponse>(
    `/v1/admin/mocks/items/${encodeURIComponent(itemId)}`,
    {
      method: 'PATCH',
      body: JSON.stringify({
        retire: true,
        reason: options?.reason ?? null,
        bundleId: options?.bundleId ?? null,
      }),
    },
  );
}

export async function assignAdminMockBooking(bookingId: string, body: {
  assignedTutorId?: string | null;
  assignedInterlocutorId?: string | null;
  status?: string | null;
}) {
  return apiRequest(`/v1/admin/mock-bookings/${encodeURIComponent(bookingId)}/assign`, {
    method: 'PATCH',
    body: JSON.stringify(body),
  });
}

// Mocks Wave 8 — admin leak-report queue.
export type AdminMockLeakReportStatus = 'open' | 'investigating' | 'resolved' | 'dismissed';

export interface AdminMockLeakReport {
  id: string;
  bundleId: string | null;
  bundleTitle: string | null;
  attemptId: string | null;
  severity: string;
  status: AdminMockLeakReportStatus;
  reasonCode: string | null;
  details: string | null;
  evidenceUrl: string | null;
  pageOrQuestion: string | null;
  reportedByUserId: string | null;
  reportedByUserDisplayName: string | null;
  createdAt: string;
  resolvedAt: string | null;
  resolvedByAdminId: string | null;
  resolutionNote: string | null;
}

export async function listAdminMockLeakReports(
  params?: { status?: AdminMockLeakReportStatus | string; limit?: number },
): Promise<{ items: AdminMockLeakReport[] }> {
  const q = new URLSearchParams();
  if (params?.status) q.set('status', params.status);
  if (typeof params?.limit === 'number') q.set('limit', String(params.limit));
  const qs = q.toString();
  return apiRequest<{ items: AdminMockLeakReport[] }>(
    `/v1/admin/mocks/leak-reports${qs ? `?${qs}` : ''}`,
  );
}

export async function updateAdminMockLeakReport(
  id: string,
  body: { status: AdminMockLeakReportStatus | string; resolutionNote?: string },
): Promise<AdminMockLeakReport> {
  return apiRequest<AdminMockLeakReport>(
    `/v1/admin/mocks/leak-reports/${encodeURIComponent(id)}`,
    {
      method: 'PATCH',
      body: JSON.stringify(body),
    },
  );
}

export type AdminAnswerKeyReportStatus = 'open' | 'investigating' | 'resolved' | 'dismissed';
export type AdminAnswerKeyReportAssessment = 'reading' | 'listening';

export interface AdminAnswerKeyReport {
  id: string;
  assessment: AdminAnswerKeyReportAssessment | string;
  attemptId: string;
  paperId: string;
  paperTitle: string;
  questionId: string;
  questionNumber: number;
  partCode: string;
  questionStemSnapshot: string;
  learnerAnswerSnapshot: string;
  officialAnswerSnapshot: string;
  reasonCode: string;
  details: string | null;
  status: AdminAnswerKeyReportStatus;
  resolutionNote: string | null;
  reportedByUserId: string;
  reportedByUserDisplayName: string;
  editorUrl: string;
  scoringSystemUrl: string;
  resolvedByAdminId: string | null;
  resolvedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export async function listAdminAnswerKeyReports(
  params?: {
    status?: AdminAnswerKeyReportStatus | string;
    assessment?: AdminAnswerKeyReportAssessment | string;
    limit?: number;
  },
): Promise<{ items: AdminAnswerKeyReport[] }> {
  const q = new URLSearchParams();
  if (params?.status) q.set('status', params.status);
  if (params?.assessment) q.set('assessment', params.assessment);
  if (typeof params?.limit === 'number') q.set('limit', String(params.limit));
  const qs = q.toString();
  return apiRequest<{ items: AdminAnswerKeyReport[] }>(
    `/v1/admin/answer-key-reports${qs ? `?${qs}` : ''}`,
  );
}

export async function updateAdminAnswerKeyReport(
  id: string,
  body: { status: AdminAnswerKeyReportStatus | string; resolutionNote?: string },
): Promise<AdminAnswerKeyReport> {
  return apiRequest<AdminAnswerKeyReport>(
    `/v1/admin/answer-key-reports/${encodeURIComponent(id)}`,
    {
      method: 'PATCH',
      body: JSON.stringify(body),
    },
  );
}

export async function fetchAdminMockAnalytics() {
  return apiRequest('/v1/admin/mocks/analytics');
}

export async function fetchAdminMockRiskList() {
  return apiRequest('/v1/admin/mocks/risk-list');
}

export async function createAdminMockBundle(body: Record<string, unknown>) {
  return apiRequest('/v1/admin/mock-bundles', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export async function addAdminMockBundleSection(bundleId: string, body: Record<string, unknown>) {
  return apiRequest(`/v1/admin/mock-bundles/${encodeURIComponent(bundleId)}/sections`, {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export async function publishAdminMockBundle(bundleId: string) {
  return apiRequest(`/v1/admin/mock-bundles/${encodeURIComponent(bundleId)}/publish`, {
    method: 'POST',
  });
}

export async function archiveAdminMockBundle(bundleId: string) {
  return apiRequest(`/v1/admin/mock-bundles/${encodeURIComponent(bundleId)}`, {
    method: 'DELETE',
  });
}

export type MockBundleBulkAction = 'publish' | 'archive' | 'delete' | 'force-delete';

/**
 * Bulk action over mock bundles. `POST /v1/admin/mock-bundles/bulk`.
 * Backend record is PascalCase `(Action, Ids)`; ASP.NET binds the camelCase
 * JSON below case-insensitively (matches the shared POST style).
 */
export async function bulkAdminMockBundles(
  action: MockBundleBulkAction,
  ids: string[],
): Promise<BulkActionResultDto> {
  return apiRequest<BulkActionResultDto>('/v1/admin/mock-bundles/bulk', {
    method: 'POST',
    body: JSON.stringify({ action, ids }),
  });
}

// Mocks V2 Wave 3 — item analysis admin endpoints.
export interface AdminMockItemAnalysisRow {
  id: string;
  paperId?: string | null;
  subtest: string;
  label?: string | null;
  totalAttempts: number;
  correctCount: number;
  difficulty: number;
  discriminationIndex?: number | null;
  distractor: string;
  flag: string | null;
  generatedAt: string;
}
export interface AdminMockItemAnalysisResponse {
  bundleId: string;
  generatedAt: string | null;
  items: AdminMockItemAnalysisRow[];
}

export async function fetchAdminMockBundleItemAnalysis(bundleId: string): Promise<AdminMockItemAnalysisResponse> {
  return apiRequest<AdminMockItemAnalysisResponse>(
    `/v1/admin/mock-bundles/${encodeURIComponent(bundleId)}/item-analysis`,
  );
}

export async function fetchAdminMockBundleListeningItemAnalysis(bundleId: string): Promise<AdminMockItemAnalysisResponse> {
  return apiRequest<AdminMockItemAnalysisResponse>(
    `/v1/admin/mock-bundles/${encodeURIComponent(bundleId)}/listening-item-analysis`,
  );
}

export async function recomputeAdminMockBundleItemAnalysis(bundleId: string): Promise<AdminMockItemAnalysisResponse> {
  return apiRequest<AdminMockItemAnalysisResponse>(
    `/v1/admin/mock-bundles/${encodeURIComponent(bundleId)}/item-analysis/recompute`,
    { method: 'POST' },
  );
}
