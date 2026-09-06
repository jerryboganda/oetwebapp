import { fetchMocksHome } from './learner-home';
import { ApiError, apiRequest, asArray, asRecord, getHeaders, isApiError, resolveApiUrl, toNullableString, type ApiRecord } from './client';
import { fetchWithTimeout } from '../network/fetch-with-timeout';
import { titleCase, toSubTest } from './task-mappers';
import { mapMockBooking, normalizeMockDeliveryMode } from './mock-bookings';
import { normalizeRouteValues } from './route-normalizer';
import { type DiagnosticRecommendedLevel, DiagnosticRecommendedPlan, DiagnosticStudyPathStep, MockBooking, MockConfig, MockDeliveryMode, MockDiagnosticEntitlement, MockOptions, MockReport, MockSession, MockStrictness, MockTypeToken, ReadinessData, SubTestReadiness } from '../mock-data';

export function mockSubtestColors(name: string) {
  switch (name) {
    case 'Reading':
      return { color: '#2563eb', bg: '#dbeafe' };
    case 'Listening':
      return { color: '#4f46e5', bg: '#e0e7ff' };
    case 'Writing':
      return { color: '#e11d48', bg: '#ffe4e6' };
    case 'Speaking':
      return { color: '#7c3aed', bg: '#ede9fe' };
    default:
      return { color: '#64748b', bg: '#f1f5f9' };
  }
}

/**
 * Mock reports/options/sessions/proctoring/bookings/diagnostic/trend + shared mappers.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export async function fetchMockReports(): Promise<MockReport[]> {
  const data = await fetchMocksHome();
  return (data.reports ?? []).map((report: ApiRecord) => mapMockReport(report));
}

export async function fetchMockReport(mockId: string): Promise<MockReport> {
  const report = await apiRequest<ApiRecord>(`/v1/mock-reports/${mockId}`);
  return mapMockReport(report);
}

/**
 * Download a watermarked PDF of the learner's writing response for a mock.
 *
 * Resolves the writing section attempt server-side from the mockAttemptId so
 * the caller does not need to know the per-subtest attempt id. The browser is
 * then asked to save the file using a transient object URL.
 *
 * The PDF carries a diagonal "PRACTICE COPY" watermark plus an HMAC token in
 * the metadata and last page. Generation is rate limited (10/day/attempt) and
 * audited on the server.
 */
export async function downloadMockWritingPdf(mockAttemptId: string): Promise<void> {
  const path = `/v1/mocks/attempts/${encodeURIComponent(mockAttemptId)}/sections/writing/pdf`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path, undefined, { json: false }),
  }, 60_000);

  if (!response.ok) {
    let code = 'unknown_error';
    let message = `Download failed: ${response.status}`;
    try {
      const error = await response.json();
      code = error.code ?? code;
      message = error.message ?? error.title ?? message;
    } catch {
      // non-JSON; surface the status only
    }
    throw new ApiError(response.status, code, message, false);
  }

  const blob = await response.blob();
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const filenameMatch = /filename="?([^";]+)"?/i.exec(disposition);
  const filename = filenameMatch?.[1] ?? `writing-${mockAttemptId}-practice.pdf`;

  const objectUrl = URL.createObjectURL(blob);
  try {
    const anchor = document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = filename;
    anchor.rel = 'noopener';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  } finally {
    // Defer revoke so the browser has time to start the download.
    setTimeout(() => URL.revokeObjectURL(objectUrl), 10_000);
  }
}

export async function downloadSpeakingEvaluationPdf(evaluationId: string): Promise<void> {
  const path = `/v1/speaking/evaluations/${encodeURIComponent(evaluationId)}/pdf`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path, undefined, { json: false }),
  }, 60_000);

  if (!response.ok) {
    let code = 'unknown_error';
    let message = `Download failed: ${response.status}`;
    try {
      const error = await response.json();
      code = error.code ?? code;
      message = error.message ?? error.title ?? message;
    } catch {
      // non-JSON; surface the status only
    }
    throw new ApiError(response.status, code, message, false);
  }

  const blob = await response.blob();
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const filenameMatch = /filename="?([^";]+)"?/i.exec(disposition);
  const filename = filenameMatch?.[1] ?? `speaking-${evaluationId}-practice.pdf`;

  const objectUrl = URL.createObjectURL(blob);
  try {
    const anchor = document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = filename;
    anchor.rel = 'noopener';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  } finally {
    setTimeout(() => URL.revokeObjectURL(objectUrl), 10_000);
  }
}

function mapMockReport(report: ApiRecord): MockReport {
  return {
    id: String(report.id ?? report.reportId ?? ''),
    reportId: report.reportId ? String(report.reportId) : undefined,
    mockAttemptId: report.mockAttemptId ? String(report.mockAttemptId) : undefined,
    state: report.state ? String(report.state) : undefined,
    title: String(report.title ?? 'Mock Report'),
    date: String(report.date ?? ''),
    profession: report.profession ? String(report.profession) : null,
    targetCountry: report.targetCountry ? String(report.targetCountry) : null,
    deliveryMode: report.deliveryMode ? String(report.deliveryMode) : null,
    strictness: report.strictness ? String(report.strictness) : null,
    overallScore: String(report.overallScore ?? 'Pending'),
    overallGrade: report.overallGrade ? String(report.overallGrade) : null,
    summary: String(report.summary ?? 'Report generation is in progress.'),
    subTests: (report.subTests ?? []).map((subtest: ApiRecord) => {
      const name = toSubTest(subtest.name ?? subtest.subtest);
      return ({
      id: String(name).toLowerCase(),
      name,
      score: String(subtest.score ?? 'Pending'),
      rawScore: String(subtest.rawScore ?? 'N/A'),
      scaledScore: typeof subtest.scaledScore === 'number' ? subtest.scaledScore : null,
      grade: subtest.grade ? String(subtest.grade) : null,
      state: subtest.state ? String(subtest.state) : undefined,
      reviewRequestId: subtest.reviewRequestId ? String(subtest.reviewRequestId) : null,
      reviewState: subtest.reviewState ? String(subtest.reviewState) : null,
      scoreConversionTableVersionKey: subtest.scoreConversionTableVersionKey
        ? String(subtest.scoreConversionTableVersionKey)
        : null,
      scoreConversionPassed: typeof subtest.scoreConversionPassed === 'boolean'
        ? subtest.scoreConversionPassed
        : null,
      ...mockSubtestColors(name),
    });
    }),
    weakestCriterion: report.weakestCriterion ?? { subtest: 'Pending', criterion: 'Awaiting evidence', description: 'Complete scored sections to generate a focused recommendation.' },
    priorComparison: report.priorComparison ?? { exists: false, priorMockName: '', overallTrend: 'flat', details: 'No earlier generated mock report is available for comparison.' },
    reviewSummary: report.reviewSummary
      ? {
          queued: Number(report.reviewSummary.queued ?? 0),
          inReview: Number(report.reviewSummary.inReview ?? 0),
          completed: Number(report.reviewSummary.completed ?? 0),
          pending: Number(report.reviewSummary.pending ?? 0),
        }
      : undefined,
    perModuleReadiness: asArray(report.perModuleReadiness).map((item: ApiRecord) => ({
      subtest: String(item.subtest ?? 'Mock'),
      scaledScore: typeof item.scaledScore === 'number' ? item.scaledScore : null,
      grade: item.grade ? String(item.grade) : null,
      scoreConversionTableVersionKey: item.scoreConversionTableVersionKey
        ? String(item.scoreConversionTableVersionKey)
        : null,
      scoreConversionPassed: typeof item.scoreConversionPassed === 'boolean'
        ? item.scoreConversionPassed
        : null,
      rag: String(item.rag ?? 'pending'),
      message: String(item.message ?? 'Awaiting scored evidence.'),
      passThreshold: typeof item.passThreshold === 'number' ? item.passThreshold : null,
    })),
    partScores: asArray(report.partScores).map((item: ApiRecord) => ({
      subtest: String(item.subtest ?? 'Mock'),
      rawScore: item.rawScore ? String(item.rawScore) : null,
      scaledScore: typeof item.scaledScore === 'number' ? item.scaledScore : null,
      grade: item.grade ? String(item.grade) : null,
      state: item.state ? String(item.state) : null,
      scoreConversionTableVersionKey: item.scoreConversionTableVersionKey
        ? String(item.scoreConversionTableVersionKey)
        : null,
      scoreConversionPassed: typeof item.scoreConversionPassed === 'boolean'
        ? item.scoreConversionPassed
        : null,
    })),
    timingAnalysis: asArray(report.timingAnalysis).map((item: ApiRecord) => ({
      sectionId: String(item.sectionId ?? ''),
      subtest: String(item.subtest ?? 'Mock'),
      startedAt: item.startedAt ? String(item.startedAt) : null,
      submittedAt: item.submittedAt ? String(item.submittedAt) : null,
      completedAt: item.completedAt ? String(item.completedAt) : null,
      deadlineAt: item.deadlineAt ? String(item.deadlineAt) : null,
      secondsUsed: typeof item.secondsUsed === 'number' ? item.secondsUsed : null,
    })),
    errorCategories: asArray(report.errorCategories).map((item: ApiRecord) => ({
      category: String(item.category ?? 'Priority issue'),
      subtest: String(item.subtest ?? 'Mock'),
      severity: String(item.severity ?? 'priority'),
      description: String(item.description ?? ''),
    })),
    teacherReviewState: report.teacherReviewState
      ? {
          queued: Number(report.teacherReviewState.queued ?? 0),
          inReview: Number(report.teacherReviewState.inReview ?? 0),
          completed: Number(report.teacherReviewState.completed ?? 0),
          pending: Number(report.teacherReviewState.pending ?? 0),
        }
      : undefined,
    bookingAdvice: report.bookingAdvice
      ? {
          status: String(report.bookingAdvice.status ?? 'pending'),
          message: String(report.bookingAdvice.message ?? ''),
          route: report.bookingAdvice.route ? String(report.bookingAdvice.route) : undefined,
          score: typeof report.bookingAdvice.score === 'number' ? report.bookingAdvice.score : null,
        }
      : undefined,
    retakeAdvice: report.retakeAdvice
      ? {
          recommendedWindowDays: Number(report.retakeAdvice.recommendedWindowDays ?? 7),
          nextMockType: String(report.retakeAdvice.nextMockType ?? 'sub'),
          subtest: String(report.retakeAdvice.subtest ?? 'reading'),
          message: String(report.retakeAdvice.message ?? ''),
        }
      : undefined,
    proctoringSummary: report.proctoringSummary
      ? {
          totalEvents: Number(report.proctoringSummary.totalEvents ?? 0),
          advisoryOnly: Boolean(report.proctoringSummary.advisoryOnly ?? true),
          criticalEvents: Number(report.proctoringSummary.criticalEvents ?? 0),
          warningEvents: Number(report.proctoringSummary.warningEvents ?? 0),
          byKind: asArray(report.proctoringSummary.byKind).map((item: ApiRecord) => ({
            kind: String(item.kind ?? ''),
            count: Number(item.count ?? 0),
          })),
          message: String(report.proctoringSummary.message ?? ''),
        }
      : undefined,
    remediationPlan: asArray(report.remediationPlan).map((item: ApiRecord) => ({
      day: String(item.day ?? ''),
      title: String(item.title ?? ''),
      description: String(item.description ?? ''),
      route: String(item.route ?? '/study-plan'),
    })),
    releasePolicy: report.releasePolicy ? String(report.releasePolicy) : undefined,
  };
}

const MOCK_TYPE_TOKENS: ReadonlySet<MockTypeToken> = new Set<MockTypeToken>([
  'full', 'lrw', 'sub', 'part', 'diagnostic', 'final_readiness', 'remedial',
]);
function normalizeMockTypeToken(value: unknown): MockTypeToken {
  const v = typeof value === 'string' ? value.toLowerCase() : '';
  return MOCK_TYPE_TOKENS.has(v as MockTypeToken) ? (v as MockTypeToken) : 'full';
}
const MOCK_STRICTNESS_OPTIONS: ReadonlySet<MockStrictness> = new Set<MockStrictness>([
  'learning', 'exam', 'final_readiness',
]);
function normalizeMockStrictness(value: unknown): MockStrictness | undefined {
  const v = typeof value === 'string' ? value.toLowerCase() : '';
  return MOCK_STRICTNESS_OPTIONS.has(v as MockStrictness) ? (v as MockStrictness) : undefined;
}

function mapMockSession(session: ApiRecord): MockSession {
  const config = session.config ?? {};
  return {
    sessionId: session.mockAttemptId,
    state: session.state,
    resumeRoute: session.resumeRoute ?? `/mocks/player/${session.mockAttemptId}`,
    reportRoute: session.reportRoute ?? null,
    reportId: session.reportId ?? null,
    config: {
      id: session.mockAttemptId,
      title: config.mockType === 'full'
        ? String(config.bundleTitle ?? 'Full OET Mock')
        : String(config.bundleTitle ?? `${titleCase(String(config.subType ?? config.mockType ?? 'mock'))} Mock`),
      bundleId: config.bundleId,
      type: normalizeMockTypeToken(config.mockType),
      subType: config.subType ? toSubTest(config.subType) : undefined,
      mode: config.mode === 'practice' ? 'practice' : 'exam',
      profession: titleCase(config.profession ?? 'medicine'),
      strictTimer: Boolean(config.strictTimer),
      includeReview: Boolean(config.includeReview),
      reviewSelection: config.reviewSelection ?? 'none',
      targetCountry: config.targetCountry ?? null,
      deliveryMode: normalizeMockDeliveryMode(config.deliveryMode),
      strictness: normalizeMockStrictness(config.strictness),
    },
    sectionStates: (session.sectionStates ?? []).map((section: ApiRecord) => ({
      id: String(section.id ?? section.sectionAttemptId ?? section.subtest ?? ''),
      sectionAttemptId: section.sectionAttemptId ? String(section.sectionAttemptId) : undefined,
      bundleSectionId: section.bundleSectionId ? String(section.bundleSectionId) : undefined,
      title: String(section.title ?? 'Mock section'),
      subtest: section.subtest ? String(section.subtest) : undefined,
      partGroup: section.partGroup ? String(section.partGroup) : undefined,
      state: String(section.state ?? 'ready'),
      reviewAvailable: Boolean(section.reviewAvailable),
      reviewSelected: Boolean(section.reviewSelected),
      launchRoute: String(section.launchRoute ?? '/mocks'),
      contentPaperId: section.contentPaperId ? String(section.contentPaperId) : undefined,
      contentPaperTitle: section.contentPaperTitle ? String(section.contentPaperTitle) : undefined,
      timeLimitMinutes: section.timeLimitMinutes ? Number(section.timeLimitMinutes) : undefined,
      noReplay: typeof section.noReplay === 'boolean' ? section.noReplay : undefined,
      previewPauseSeconds: section.previewPauseSeconds ? Number(section.previewPauseSeconds) : undefined,
      readingWindowSeconds: section.readingWindowSeconds ? Number(section.readingWindowSeconds) : undefined,
      editingWindowSeconds: section.editingWindowSeconds ? Number(section.editingWindowSeconds) : undefined,
      caseNoteHtml: typeof section.caseNoteHtml === 'string' ? section.caseNoteHtml : undefined,
      startedAt: section.startedAt ?? null,
      deadlineAt: section.deadlineAt ?? null,
      submittedAt: section.submittedAt ?? null,
      completedAt: section.completedAt ?? null,
      rawScore: typeof section.rawScore === 'number' ? section.rawScore : null,
      rawScoreMax: typeof section.rawScoreMax === 'number' ? section.rawScoreMax : null,
      scaledScore: typeof section.scaledScore === 'number' ? section.scaledScore : null,
      grade: section.grade ? String(section.grade) : null,
    })),
    reviewReservation: session.reviewReservation
      ? {
          id: String(session.reviewReservation.id ?? ''),
          state: String(session.reviewReservation.state ?? 'reserved'),
          selection: session.reviewReservation.selection ?? 'none',
          reservedCredits: Number(session.reviewReservation.reservedCredits ?? 0),
          consumedCredits: Number(session.reviewReservation.consumedCredits ?? 0),
          releasedCredits: Number(session.reviewReservation.releasedCredits ?? 0),
          pendingCredits: Number(session.reviewReservation.pendingCredits ?? 0),
          reservedAt: String(session.reviewReservation.reservedAt ?? ''),
          expiresAt: String(session.reviewReservation.expiresAt ?? ''),
        }
      : null,
  };
}

export async function fetchMockOptions(): Promise<MockOptions> {
  const options = normalizeRouteValues(await apiRequest<ApiRecord>('/v1/mocks/options'));
  return {
    mockTypes: Array.isArray(options.mockTypes) ? options.mockTypes.map((item: ApiRecord) => ({
      id: normalizeMockTypeToken(item.id),
      label: String(item.label ?? item.id ?? ''),
      description: String(item.description ?? ''),
    })) : [],
    subTypes: Array.isArray(options.subTypes) ? options.subTypes.map((item: ApiRecord) => ({
      id: String(item.id ?? ''),
      label: String(item.label ?? item.id ?? ''),
    })) : [],
    modes: Array.isArray(options.modes) ? options.modes.map((item: ApiRecord) => ({
      id: item.id === 'practice' ? 'practice' : 'exam',
      label: String(item.label ?? item.id ?? ''),
    })) : [],
    professions: Array.isArray(options.professions) ? options.professions.map((item: ApiRecord) => ({
      id: String(item.id ?? ''),
      label: String(item.label ?? item.id ?? ''),
    })) : [],
    reviewSelections: Array.isArray(options.reviewSelections) ? options.reviewSelections.map((item: ApiRecord) => ({
      id: item.id ?? 'none',
      label: String(item.label ?? item.id ?? ''),
      cost: Number(item.cost ?? 0),
    })) : [],
    wallet: {
      availableCredits: Number(options.wallet?.availableCredits ?? 0),
    },
    deliveryModes: Array.isArray(options.deliveryModes) ? (options.deliveryModes as ApiRecord[])
      .map((item: ApiRecord): { id: MockDeliveryMode; label: string } | null => {
        const id = normalizeMockDeliveryMode(item.id);
        return id ? { id, label: String(item.label ?? id) } : null;
      })
      .filter((x): x is { id: MockDeliveryMode; label: string } => x !== null) : [],
    strictnessOptions: Array.isArray(options.strictnessOptions) ? (options.strictnessOptions as ApiRecord[])
      .map((item: ApiRecord): { id: MockStrictness; label: string; description?: string } | null => {
        const id = normalizeMockStrictness(item.id);
        if (!id) return null;
        const out: { id: MockStrictness; label: string; description?: string } = { id, label: String(item.label ?? id) };
        if (item.description) out.description = String(item.description);
        return out;
      })
      .filter((x): x is { id: MockStrictness; label: string; description?: string } => x !== null) : [],
    availableBundles: Array.isArray(options.availableBundles) ? options.availableBundles.map((bundle: ApiRecord) => ({
      id: String(bundle.id ?? bundle.bundleId ?? ''),
      bundleId: String(bundle.bundleId ?? bundle.id ?? ''),
      title: String(bundle.title ?? 'Mock bundle'),
      mockType: normalizeMockTypeToken(bundle.mockType),
      subtest: bundle.subtest ? String(bundle.subtest) : null,
      professionId: bundle.professionId ? String(bundle.professionId) : null,
      appliesToAllProfessions: Boolean(bundle.appliesToAllProfessions),
      estimatedDurationMinutes: Number(bundle.estimatedDurationMinutes ?? 0),
      difficulty: bundle.difficulty ? String(bundle.difficulty) : undefined,
      sourceStatus: bundle.sourceStatus ? String(bundle.sourceStatus) : undefined,
      qualityStatus: bundle.qualityStatus ? String(bundle.qualityStatus) : undefined,
      releasePolicy: bundle.releasePolicy ? String(bundle.releasePolicy) : undefined,
      topicTags: asArray(bundle.topicTags).map(String),
      skillTags: asArray(bundle.skillTags).map(String),
      watermarkEnabled: typeof bundle.watermarkEnabled === 'boolean' ? bundle.watermarkEnabled : undefined,
      randomiseQuestions: typeof bundle.randomiseQuestions === 'boolean' ? bundle.randomiseQuestions : undefined,
      sections: Array.isArray(bundle.sections) ? bundle.sections.map((section: ApiRecord) => ({
        id: String(section.id ?? ''),
        subtest: String(section.subtest ?? ''),
        title: String(section.title ?? section.subtest ?? 'Section'),
        timeLimitMinutes: Number(section.timeLimitMinutes ?? 0),
        reviewEligible: Boolean(section.reviewEligible),
        contentPaperId: String(section.contentPaperId ?? ''),
      })) : [],
    })) : [],
  };
}

export async function createMockSession(config: {
  type: MockTypeToken;
  subType?: string;
  mode: 'practice' | 'exam';
  profession: string;
  strictTimer: boolean;
  reviewSelection: MockConfig['reviewSelection'];
  bundleId?: string;
  targetCountry?: string | null;
  deliveryMode?: MockDeliveryMode;
  strictness?: MockStrictness;
}): Promise<MockSession> {
  const response = normalizeRouteValues(await apiRequest<ApiRecord>('/v1/mock-attempts', {
    method: 'POST',
    body: JSON.stringify({
      mockType: config.type,
      subType: config.subType ?? null,
      mode: config.mode,
      profession: config.profession,
      strictTimer: config.strictTimer,
      includeReview: config.reviewSelection !== 'none',
      reviewSelection: config.reviewSelection,
      bundleId: config.bundleId ?? null,
      targetCountry: config.targetCountry ?? null,
      deliveryMode: config.deliveryMode ?? null,
      strictness: config.strictness ?? null,
    }),
  }));
  void import('@/lib/credit-feedback').then((m) => m.announceCreditUsage('mock'));
  return mapMockSession(response);
}

export async function fetchMockSession(sessionId: string): Promise<MockSession> {
  const response = normalizeRouteValues(await apiRequest<ApiRecord>(`/v1/mock-attempts/${sessionId}`));
  return mapMockSession(response);
}

export async function submitMockSession(sessionId: string): Promise<{ sessionId: string; state: string }> {
  const response = await apiRequest<ApiRecord>(`/v1/mock-attempts/${sessionId}/submit`, {
    method: 'POST',
  });
  return { sessionId, state: response.state ?? 'queued' };
}

export async function startMockSection(
  sessionId: string,
  sectionId: string,
  clientState: Record<string, unknown> = {},
): Promise<MockSession['sectionStates'][number]> {
  const response = normalizeRouteValues(await apiRequest<ApiRecord>(`/v1/mock-attempts/${sessionId}/sections/${sectionId}/start`, {
    method: 'POST',
    body: JSON.stringify({ clientState }),
  }));
  return mapMockSession({ mockAttemptId: sessionId, config: {}, sectionStates: [response] }).sectionStates[0];
}

export async function completeMockSection(sessionId: string, sectionId: string, payload: {
  contentAttemptId?: string | null;
  rawScore?: number | null;
  rawScoreMax?: number | null;
  scaledScore?: number | null;
  grade?: string | null;
  evidence?: Record<string, unknown>;
  reviewTurnaroundOption?: string | null;
} = {}): Promise<MockSession['sectionStates'][number]> {
  const response = normalizeRouteValues(await apiRequest<ApiRecord>(`/v1/mock-attempts/${sessionId}/sections/${sectionId}/complete`, {
    method: 'POST',
    body: JSON.stringify({
      contentAttemptId: payload.contentAttemptId ?? null,
      rawScore: payload.rawScore ?? null,
      rawScoreMax: payload.rawScoreMax ?? null,
      scaledScore: payload.scaledScore ?? null,
      grade: payload.grade ?? null,
      evidence: payload.evidence ?? {},
      reviewTurnaroundOption: payload.reviewTurnaroundOption ?? null,
    }),
  }));
  return mapMockSession({ mockAttemptId: sessionId, config: {}, sectionStates: [response] }).sectionStates[0];
}

export async function cancelMockSession(sessionId: string): Promise<MockSession> {
  const response = normalizeRouteValues(await apiRequest<ApiRecord>(`/v1/mock-attempts/${sessionId}/cancel`, {
    method: 'POST',
  }));
  return mapMockSession(response);
}

/**
 * Mocks V2 Wave 2 — proctoring telemetry kinds. Must match backend
 * `MockProctoringKinds` constants.
 */
export const MOCK_PROCTORING_KINDS = [
  'fullscreen_exit',
  'visibility_hidden',
  'tab_switch',
  'paste_blocked',
  'copy_blocked',
  'mic_check_passed',
  'mic_check_failed',
  'cam_check_passed',
  'cam_check_failed',
  'audio_issue_reported',
  'audio_playback_passed',
  'audio_playback_failed',
  'network_drop',
  'multiple_displays_detected',
] as const;
export type MockProctoringKind = typeof MOCK_PROCTORING_KINDS[number];
export type MockProctoringSeverity = 'info' | 'warning' | 'critical';

export interface MockProctoringEventInput {
  kind: MockProctoringKind;
  occurredAt: string; // ISO
  mockSectionAttemptId?: string;
  severity?: MockProctoringSeverity;
  metadata?: Record<string, unknown>;
}

export interface MockProctoringBatchResult {
  ok: boolean;
  accepted: number;
  dropped: number;
  capacityRemaining?: number;
  reason?: string;
}

/**
 * Send a batch of proctoring events. Backend caps at 50 per request and 250 per attempt.
 */
export async function recordMockProctoringEvents(
  sessionId: string,
  events: MockProctoringEventInput[],
): Promise<MockProctoringBatchResult> {
  if (events.length === 0) return { ok: true, accepted: 0, dropped: 0 };
  const response = await apiRequest<ApiRecord>(`/v1/mock-attempts/${sessionId}/proctoring-events`, {
    method: 'POST',
    body: JSON.stringify({ events }),
  });
  return {
    ok: Boolean(response.ok),
    accepted: Number(response.accepted ?? 0),
    dropped: Number(response.dropped ?? 0),
    capacityRemaining: typeof response.capacityRemaining === 'number' ? response.capacityRemaining : undefined,
    reason: typeof response.reason === 'string' ? response.reason : undefined,
  };
}

export async function fetchMockBookings(): Promise<MockBooking[]> {
  const response = await apiRequest<ApiRecord>('/v1/mock-bookings');
  return asArray(response.items).map(mapMockBooking);
}

export async function fetchMockBookingDetail(bookingId: string): Promise<MockBooking> {
  const response = await apiRequest<ApiRecord>(`/v1/mock-bookings/${encodeURIComponent(bookingId)}`);
  return mapMockBooking(response);
}

export async function createMockBooking(payload: {
  mockBundleId: string;
  scheduledStartAt: string;
  timezoneIana?: string;
  deliveryMode?: MockDeliveryMode;
  consentToRecording?: boolean;
  learnerNotes?: string | null;
}): Promise<MockBooking> {
  const response = await apiRequest<ApiRecord>('/v1/mock-bookings', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
  return mapMockBooking(response);
}

// ----- Mocks V2 Wave 5 — availability calendar -----------------------------------
// Phase 5: learner booking page queries available slots for a given date.
// Backend contract: `GET /v1/mocks/availability?date=YYYY-MM-DD&timezone=Iana[&bundleId=...]`
// returns `{ slots: { startAt, endAt, isAvailable, blockedReason? }[] }`.

export interface MockAvailabilitySlot {
  tutorProfileId: string;
  tutorDisplayName: string;
  tutorTimezone: string;
  startAt: string;
  endAt: string;
  isAvailable: boolean;
  blockedReason?: string;
}

export async function fetchMockAvailability(
  date: string,
  timezone: string,
  bundleId?: string,
): Promise<{ slots: MockAvailabilitySlot[] }> {
  const params = new URLSearchParams({ date, timezone });
  if (bundleId) params.set('bundleId', bundleId);
  const response = await apiRequest<ApiRecord>(`/v1/mocks/availability?${params.toString()}`);
  const slots = asArray(response.slots).map((item): MockAvailabilitySlot => ({
    tutorProfileId: String(item.tutorProfileId ?? ''),
    tutorDisplayName: String(item.tutorDisplayName ?? 'Tutor'),
    tutorTimezone: String(item.tutorTimezone ?? timezone),
    startAt: String(item.startAt ?? ''),
    endAt: String(item.endAt ?? ''),
    isAvailable: Boolean(item.isAvailable),
    blockedReason: typeof item.blockedReason === 'string' ? item.blockedReason : undefined,
  }));
  return { slots };
}

// Mocks V2 Phase 5 — new bookings family hitting `/v1/mocks/bookings/*`.
// These are additive to the legacy `/v1/mock-bookings/*` family kept above
// for the existing learner list page; the Phase 5 calendar flow uses these.
export async function createMockBookingV2(payload: {
  bundleId: string;
  scheduledStartAt: string;
  timezone: string;
  consentToRecording: boolean;
  mockAttemptId?: string;
  mockSectionId?: string;
  tutorProfileId: string;
}): Promise<MockBooking> {
  const response = await apiRequest<ApiRecord>('/v1/mocks/bookings', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
  return mapMockBooking(response);
}

export async function rescheduleMockBookingV2(
  bookingId: string,
  scheduledStartAt: string,
): Promise<MockBooking> {
  const response = await apiRequest<ApiRecord>(
    `/v1/mocks/bookings/${encodeURIComponent(bookingId)}/reschedule`,
    {
      method: 'PATCH',
      body: JSON.stringify({ scheduledStartAt }),
    },
  );
  return mapMockBooking(response);
}

export async function cancelMockBookingV2(bookingId: string): Promise<void> {
  await apiRequest<ApiRecord>(
    `/v1/mocks/bookings/${encodeURIComponent(bookingId)}`,
    { method: 'DELETE' },
  );
}

export async function updateMockBooking(bookingId: string, payload: Record<string, unknown>): Promise<MockBooking> {
  const response = await apiRequest<ApiRecord>(`/v1/mock-bookings/${encodeURIComponent(bookingId)}`, {
    method: 'PATCH',
    body: JSON.stringify(payload),
  });
  return mapMockBooking(response);
}

export async function reportMockLeak(payload: {
  mockBundleId?: string | null;
  mockAttemptId?: string | null;
  reason?: string | null;
  evidenceUrl?: string | null;
  pageOrQuestion?: string | null;
}): Promise<{ id: string; status: string; severity: string }> {
  const response = await apiRequest<ApiRecord>('/v1/mocks/leak-report', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
  return {
    id: String(response.id ?? ''),
    status: String(response.status ?? 'open'),
    severity: String(response.severity ?? 'high'),
  };
}

export async function fetchMockDiagnosticStudyPath() {
  return apiRequest<ApiRecord>('/v1/mocks/diagnostic/study-path');
}

/**
 * Phase 1 P1.4 — Parse the optional `recommendedLevel + recommendedModuleIds +
 * studyPath` block that the backend `MockDiagnosticService` now attaches to
 * its readiness output. The diagnostic page calls this with whatever
 * `fetchMockDiagnosticStudyPath` returned (or any payload that wraps these
 * fields) so the new render path degrades silently when the backend response
 * is the legacy shape.
 *
 * Returns `null` when no `recommendedLevel` is present so callers can keep
 * their existing conditional rendering.
 */
export function parseDiagnosticRecommendedPlan(record: ApiRecord | null | undefined): DiagnosticRecommendedPlan | null {
  if (!record) return null;
  const level = typeof record.recommendedLevel === 'string'
    ? (record.recommendedLevel.toLowerCase() as DiagnosticRecommendedLevel)
    : null;
  const validLevels: DiagnosticRecommendedLevel[] = ['beginner', 'improver', 'intermediate', 'advanced'];
  const safeLevel = level && validLevels.includes(level) ? level : null;

  const ids = Array.isArray(record.recommendedModuleIds)
    ? (record.recommendedModuleIds as unknown[]).filter((x): x is string => typeof x === 'string')
    : [];

  const stepsRaw = Array.isArray(record.studyPath) ? (record.studyPath as ApiRecord[]) : [];
  const steps: DiagnosticStudyPathStep[] = stepsRaw.map((s, idx) => ({
    stepNumber: typeof s.stepNumber === 'number' ? s.stepNumber : idx + 1,
    title: String(s.title ?? `Week ${idx + 1}`),
    description: String(s.description ?? ''),
    routeHref: typeof s.routeHref === 'string' && s.routeHref.length > 0 ? s.routeHref : '/dashboard',
    subtestCode: typeof s.subtestCode === 'string' ? s.subtestCode : null,
    drillId: typeof s.drillId === 'string' ? s.drillId : null,
  }));

  if (!safeLevel && ids.length === 0 && steps.length === 0) return null;
  return {
    recommendedLevel: safeLevel,
    recommendedModuleIds: ids,
    studyPath: steps,
  };
}

export async function fetchMockDiagnosticEntitlement(): Promise<MockDiagnosticEntitlement> {
  const response = await apiRequest<ApiRecord>('/v1/mocks/diagnostic/entitlement');
  return {
    allowed: Boolean(response.allowed),
    entitlement: String(response.entitlement ?? 'one_per_lifetime'),
    reason: typeof response.reason === 'string' ? response.reason : null,
    message: typeof response.message === 'string' ? response.message : null,
  };
}

// ----- Mocks V2 Wave 5 — entitlement summary -------------------------------------
// Backend contract: `GET /v1/mocks/entitlements/summary` returns the per-mock-type
// breakdown of granted / consumed / remaining counts, so the setup screen can
// render a "3 of 5 Writing mocks used" widget and surface a paywall CTA when any
// bucket is fully consumed. The endpoint is being introduced alongside the
// MockEntitlementLedger; this client tolerates a 404 (returning an empty
// summary) so the UI degrades gracefully until the backend is wired.

export interface MockEntitlementSummaryItem {
  mockType: string;
  label: string;
  granted: number;
  consumed: number;
  remaining: number;
}

export interface MockEntitlementSummary {
  items: MockEntitlementSummaryItem[];
  anyExhausted: boolean;
}

export async function fetchMockEntitlementsSummary(): Promise<MockEntitlementSummary> {
  try {
    const response = await apiRequest<ApiRecord>('/v1/mocks/entitlements/summary');
    const items = asArray(response.items).map((item: ApiRecord): MockEntitlementSummaryItem => {
      const granted = Math.max(0, Number(item.granted ?? 0) || 0);
      const consumed = Math.max(0, Number(item.consumed ?? 0) || 0);
      const remainingRaw = item.remaining ?? granted - consumed;
      const remaining = Math.max(0, Number(remainingRaw) || 0);
      return {
        mockType: String(item.mockType ?? item.type ?? ''),
        label: String(item.label ?? item.mockType ?? item.type ?? 'Mock'),
        granted,
        consumed,
        remaining,
      };
    }).filter((entry) => entry.mockType.length > 0);
    const anyExhausted = typeof response.anyExhausted === 'boolean'
      ? response.anyExhausted
      : items.some((entry) => entry.granted > 0 && entry.remaining <= 0);
    return { items, anyExhausted };
  } catch (err) {
    if (isApiError(err) && (err.status === 404 || err.code === 'not_found')) {
      return { items: [], anyExhausted: false };
    }
    throw err;
  }
}

// ----- Mocks V2 Wave 4 — bookings ------------------------------------------------

export interface MockBookingListResponse {
  items: MockBooking[];
  now: string;
}

export async function fetchMockBookingList(): Promise<MockBookingListResponse> {
  const response = await apiRequest<ApiRecord>('/v1/mocks/bookings');
  return {
    items: asArray(response.items).map(mapMockBooking),
    now: typeof response.now === 'string' ? response.now : new Date().toISOString(),
  };
}

export async function rescheduleMockBooking(
  bookingId: string,
  scheduledStartAt: string,
  timezoneIana?: string,
): Promise<MockBooking> {
  const response = await apiRequest<ApiRecord>(`/v1/mock-bookings/${bookingId}/reschedule`, {
    method: 'PATCH',
    body: JSON.stringify({ scheduledStartAt, timezoneIana }),
  });
  return mapMockBooking(response);
}

export async function cancelMockBooking(bookingId: string): Promise<MockBooking> {
  const response = await apiRequest<ApiRecord>(`/v1/mock-bookings/${bookingId}/cancel`, {
    method: 'POST',
  });
  return mapMockBooking(response);
}

// ----- Mocks V2 Wave 6 — chunked recording upload --------------------------------
// Each browser MediaRecorder chunk is POSTed as a raw audio body. The backend
// writes via IFileStorage (SHA-256 content-addressed) and stores the manifest
// on the booking row. Recording is gated server-side on
// `consentToRecording === true`.

export interface MockBookingChunkAck {
  part: number;
  sha256: string;
  bytes: number;
  chunkCount: number;
  totalBytes: number;
}

export async function appendMockBookingRecordingChunk(
  bookingId: string,
  part: number,
  blob: Blob,
  options?: { signal?: AbortSignal },
): Promise<MockBookingChunkAck> {
  const path = `/v1/mock-bookings/${encodeURIComponent(bookingId)}/recording-chunk?part=${part}`;
  const headers = new Headers(await getHeaders(path, undefined, { json: false }));
  headers.set('Content-Type', blob.type || 'audio/webm');
  const response = await fetchWithTimeout(
    resolveApiUrl(path),
    {
      method: 'POST',
      headers,
      body: blob,
      signal: options?.signal,
    },
    60_000,
  );
  if (!response.ok) {
    let message = `Chunk upload failed: ${response.status}`;
    let code = 'chunk_upload_failed';
    try {
      const err = await response.json();
      message = err.message ?? err.title ?? message;
      code = err.code ?? code;
    } catch {
      /* non-JSON */
    }
    throw new ApiError(response.status, code, message, false);
  }
  const data = await response.json();
  return {
    part: Number(data.part ?? part),
    sha256: String(data.sha256 ?? ''),
    bytes: Number(data.bytes ?? 0),
    chunkCount: Number(data.chunkCount ?? 0),
    totalBytes: Number(data.totalBytes ?? 0),
  };
}

export interface MockBookingRecordingFinalizeResult {
  bookingId: string;
  recordingFinalizedAt: string | null;
  recordingDurationMs: number | null;
  consentToRecording: boolean;
}

export async function finalizeMockBookingRecording(
  bookingId: string,
  durationMs?: number,
): Promise<MockBookingRecordingFinalizeResult> {
  const response = await apiRequest<ApiRecord>(
    `/v1/mock-bookings/${encodeURIComponent(bookingId)}/recording/finalize`,
    {
      method: 'POST',
      body: JSON.stringify({ durationMs: durationMs ?? null }),
    },
  );
  return {
    bookingId: String(response.bookingId ?? bookingId),
    recordingFinalizedAt: typeof response.recordingFinalizedAt === 'string' ? response.recordingFinalizedAt : null,
    recordingDurationMs: typeof response.recordingDurationMs === 'number' ? response.recordingDurationMs : null,
    consentToRecording: Boolean(response.consentToRecording),
  };
}

// ----- Mocks V2 Wave 5 — remediation plan ----------------------------------------

export interface RemediationTask {
  id: string;
  mockReportId: string;
  subtestCode: string;
  weaknessTag: string;
  title: string;
  description: string;
  routeHref?: string | null;
  dayIndex: number;
  status: 'pending' | 'completed' | 'skipped';
  createdAt: string;
  completedAt?: string | null;
}

export async function fetchRemediationPlan(): Promise<{ items: RemediationTask[] }> {
  const response = await apiRequest<ApiRecord>('/v1/mocks/remediation-plan');
  return { items: asArray(response.items) as unknown as RemediationTask[] };
}

export async function generateRemediationPlan(reportId: string): Promise<{ items: RemediationTask[]; generated: boolean }> {
  const response = await apiRequest<ApiRecord>(`/v1/mocks/reports/${reportId}/remediation-plan/generate`, {
    method: 'POST',
  });
  return {
    items: asArray(response.items) as unknown as RemediationTask[],
    generated: Boolean(response.generated),
  };
}

export async function completeRemediationTask(taskId: string): Promise<RemediationTask> {
  const response = await apiRequest<ApiRecord>(`/v1/remediation-tasks/${taskId}/complete`, {
    method: 'PATCH',
  });
  return response as unknown as RemediationTask;
}

export interface MockReadinessTrend {
  attemptsConsidered: number;
  overallTrend: 'up' | 'down' | 'flat';
  consistentGreen: boolean;
  message: string;
}

export async function fetchMockReadinessTrend(): Promise<MockReadinessTrend> {
  const response = await apiRequest<ApiRecord>('/v1/learner/me/readiness/trend');
  return {
    attemptsConsidered: Number(response.attemptsConsidered ?? 0),
    overallTrend: (response.overallTrend as 'up' | 'down' | 'flat') ?? 'flat',
    consistentGreen: Boolean(response.consistentGreen),
    message: String(response.message ?? ''),
  };
}

export function parseScoreValue(range: string): number {
  if (!range) return 0;
  const numeric = range.match(/\d+/)?.[0];
  return numeric ? Number(numeric) : 0;
}

export function mapReadinessResponse(readiness: ApiRecord): ReadinessData {
  const evidence = asRecord(readiness.evidence);
  const vocabulary = asRecord(readiness.vocabulary);
  const computedAt = toNullableString(readiness.computedAt);

  return {
    targetDate: readiness.targetDate,
    weeksRemaining: readiness.weeksRemaining,
    overallRisk: titleCase(readiness.overallRisk) as ReadinessData['overallRisk'],
    overallReadiness: typeof readiness.overallReadiness === 'number' ? readiness.overallReadiness : undefined,
    recommendedStudyHours: readiness.recommendedStudyHoursPerWeek ?? readiness.recommendedStudyHours,
    recommendedStudyHoursRationale: typeof readiness.recommendedStudyHoursRationale === 'string' ? readiness.recommendedStudyHoursRationale : undefined,
    weakestLink: readiness.weakestSubtest ?? readiness.weakestLink ?? 'No readiness evidence yet',
    targetDateProbability: typeof readiness.targetDateProbability === 'number' ? readiness.targetDateProbability : null,
    confidenceLevel: (readiness.confidenceLevel ?? 'Low') as ReadinessData['confidenceLevel'],
    dataPointCount: typeof readiness.dataPointCount === 'number' ? readiness.dataPointCount : 0,
    subTests: asArray(readiness.subTests).map((item: ApiRecord) => {
      const name = (item.name ?? item.code ?? '') as string;
      return {
        id: (item.code ?? item.id ?? '').toString().toLowerCase(),
        name: name as SubTestReadiness['name'],
        readiness: Number(item.readiness ?? item.current ?? 0),
        target: Number(item.target ?? 70),
        status: item.status ?? 'Unknown',
        color: name === 'Writing' ? '#e11d48' : name === 'Speaking' ? '#7c3aed' : name === 'Reading' ? '#2563eb' : '#4f46e5',
        bg: name === 'Writing' ? '#fff1f2' : name === 'Speaking' ? '#f5f3ff' : name === 'Reading' ? '#eff6ff' : '#eef2ff',
        barColor: name === 'Writing' ? '#fb7185' : name === 'Speaking' ? '#a78bfa' : name === 'Reading' ? '#60a5fa' : '#818cf8',
        isWeakest: Boolean(item.isWeakest),
        confidenceBand: typeof item.confidenceBand === 'string' ? item.confidenceBand : undefined,
        dataPoints: typeof item.dataPoints === 'number' ? item.dataPoints : undefined,
      };
    }),
    vocabulary: vocabulary && typeof vocabulary.readiness === 'number' ? {
      readiness: Number(vocabulary.readiness),
      target: Number(vocabulary.target ?? 100),
      mastered: Number(vocabulary.mastered ?? 0),
      masteryTarget: Number(vocabulary.masteryTarget ?? 600),
      accuracy30d: Number(vocabulary.accuracy30d ?? 0),
      dataPoints: Number(vocabulary.dataPoints ?? 0),
    } : undefined,
    blockers: asArray(readiness.blockers).map((b: ApiRecord) => ({
      id: b.id,
      title: b.title,
      description: b.description,
      actionLabel: typeof b.actionLabel === 'string' ? b.actionLabel : undefined,
      actionHref: typeof b.actionHref === 'string' ? b.actionHref : undefined,
      impactScore: typeof b.impactScore === 'number' ? b.impactScore : undefined,
      severity: (typeof b.severity === 'string' ? b.severity : undefined) as 'high' | 'medium' | 'low' | undefined,
    })),
    evidence: {
      source: typeof evidence.source === 'string' ? evidence.source : undefined,
      mocksCompleted: Number(evidence.mocksCompleted ?? 0),
      practiceQuestions: Number(evidence.practiceQuestions ?? 0),
      expertReviews: Number(evidence.expertReviews ?? 0),
      vocabReviewed30d: typeof evidence.vocabReviewed30d === 'number' ? evidence.vocabReviewed30d : undefined,
      recentTrend: typeof evidence.recentTrend === 'string' && evidence.recentTrend.length > 0
        ? evidence.recentTrend
        : 'Trend data will appear after more practice.',
      lastUpdated: typeof evidence.lastUpdated === 'string' && evidence.lastUpdated.length > 0
        ? evidence.lastUpdated
        : computedAt?.slice(0, 10) ?? 'Unknown',
    },
  };
}
