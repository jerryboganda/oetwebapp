import { ensureFreshAccessToken } from './auth-client';
import {
  ApiError,
  API_BASE_URL,
  apiBlobRequest,
  apiRequest,
  asArray,
  asRecord,
  getHeaders,
  isApiError,
  isRetryable,
  maybe,
  normalizeBillingCode,
  resolveApiUploadUrl,
  resolveApiUrl,
  resolveBrowserApiResourceUrl,
  toNullableString,
  toStringArray,
  type ApiRecord,
} from './api/client';
export { ApiError, isApiError } from './api/client';
import { fetchLearnerReviewResult, fetchLearnerReviewVoiceNotes } from './api/expert';
import { mapMockBooking, normalizeMockDeliveryMode } from './api/mock-bookings';
import { uploadMedia } from './api/content-discovery';
import {
  titleCase as domainTitleCase,
  minutesToLabel as domainMinutesToLabel,
  scoreRangeDisplay as domainScoreRangeDisplay,
  formatCurrency as domainFormatCurrency,
  normalizeWaveformPeaks as domainNormalizeWaveformPeaks,
  parseCriterionScore as domainParseCriterionScore,
  scoreToGrade as domainScoreToGrade,
  toExamFamilyCode,
} from './domain/format';
import { fetchWithTimeout } from './network/fetch-with-timeout';
import type { CurrentUser } from './types/auth';
import type {
  ExamFamilyCode,
  UserProfile,
  StudyPlanTask,
  WritingTask,
  WritingResult,
  WritingSubmission,
  CriteriaDelta,
  ModelAnswer,
  SpeakingTask,
  RoleCard,
  SpeakingResult,
  PhrasingSegment,
  ReadingTask,
  ReadingResult,
  ListeningTask,
  ListeningResult,
  ListeningDrill,
  ListeningReview,
  MockConfig,
  MockOptions,
  MockReport,
  MockSession,
  MockBooking,
  MockSpeakingContent,
  MockDiagnosticEntitlement,
  DiagnosticRecommendedLevel,
  DiagnosticRecommendedPlan,
  DiagnosticStudyPathStep,
  ReadinessData,
  ReadinessBlocker,
  ReadinessHistoryPoint,
  ReadinessForecast,
  SubTestReadiness,
  ProgressEvidenceSummary,
  TrendPoint,
  Submission,
  SubmissionComparison,
  SubmissionDetail,
  TurnaroundOption,
  FocusArea,
  DiagnosticSession,
  DiagnosticResult,
  CriterionFeedback,
  AnchoredComment,
  Confidence,
  SubTest,
  SettingsSectionData,
  SettingsSectionId,
  SpeakingTranscriptReview,
  MockTypeToken,
  MockDeliveryMode,
  MockStrictness,
  EvalStatus,
} from './mock-data';
import {
  WRITING_CRITERION_MAX_SCORES,
  type WritingCriterionCode,
} from './scoring';
import type {
  BillingData,
  BillingChangePreview,
  BillingQuote,
  BillingProductType,
  BillingPaymentStatus,
  Invoice,
  AiPackage,
  AiPackageCreditSnapshot,
  AiPackagesResponse,
} from './billing-types';
export type {
  BillingData,
  BillingChangePreview,
  BillingQuote,
  BillingProductType,
  BillingPaymentStatus,
  Invoice,
  AiPackage,
  AiPackageCreditSnapshot,
  AiPackagesResponse,
};
import { mapAiPackageCreditSnapshot } from './map-ai-package-credit-snapshot';
import type {
  CalibrationCaseDetail,
  CalibrationCase,
  CalibrationNote,
  ExpertDashboardData,
  ExpertMe,
  ExpertMetrics,
  ExpertLearnerDirectoryResponse,
  ExpertLearnerReviewContext,
  ExpertQueueFilterMetadata,
  ExpertReviewHistory,
  ExpertSchedule,
  LearnerProfileExpanded,
  ReviewDraft,
  ReviewQueueResponse,
  ScheduleException,
  SpeakingReviewDetail,
  ReviewVoiceNote,
  WritingPaperAsset,
  WritingReviewDetail,
  ExpertOnboardingProfile,
  ExpertOnboardingQualifications,
  ExpertOnboardingRates,
  ExpertOnboardingStatus,
} from './types/expert';
import type {
  AiGroundingContext,
  LintFinding,
  Rulebook,
  SpeakingAuditInput,
  WritingLintInput,
} from './rulebook';
import type { WeaknessDataPoint } from './writing-analytics/types';

export interface SpeakingHomeAction {
  id: string;
  title: string;
  description?: string;
  route: string;
}

export interface SpeakingHomeDrillGroup {
  id: string;
  title: string;
  items: SpeakingHomeAction[];
}

export interface SpeakingHomeAttempt {
  attemptId: string;
  state: string;
  scoreEstimate?: string | null;
  route: string;
}

export interface SpeakingHomeReviewCredits {
  available: number;
  route: string;
  billingRoute?: string;
}

export interface SpeakingHome {
  recommendedRolePlay?: SpeakingTask | null;
  commonIssuesToImprove: string[];
  drillGroups: SpeakingHomeDrillGroup[];
  pastAttempts: SpeakingHomeAttempt[];
  reviewCredits: SpeakingHomeReviewCredits;
  supportEntries: SpeakingHomeAction[];
  featuredTasks: SpeakingTask[];
  latestEvaluation?: ApiRecord | null;
  tips: string[];
  dashboardRoute?: string;
  historyRoute?: string;
  writingLibraryRoute?: string;
  writingTaskRoute?: string;
  readingTaskRoute?: string;
  listeningTaskRoute?: string;
}

function isBrowser() {
  return typeof window !== 'undefined';
}

function toSubTest(code: string): SubTest {
  switch (code?.toLowerCase()) {
    case 'writing':
      return 'Writing';
    case 'speaking':
      return 'Speaking';
    case 'reading':
      return 'Reading';
    case 'listening':
      return 'Listening';
    default:
      console.warn('[API] Unknown subtest code:', code, '- defaulting to Writing');
      return 'Writing';
  }
}

function titleCase(value: string | null | undefined): string {
  return domainTitleCase(value);
}

function minutesToLabel(minutes: number): string {
  return domainMinutesToLabel(minutes);
}

function toConfidence(value: string | null | undefined): Confidence {
  const normalized = (value ?? '').toLowerCase();
  if (normalized === 'high') return 'High';
  if (normalized === 'low') return 'Low';
  return 'Medium';
}

function toEvalStatus(value: string | null | undefined) {
  const normalized = (value ?? '').toLowerCase();
  if (normalized === 'queued' || normalized === 'processing' || normalized === 'completed' || normalized === 'failed') {
    return normalized;
  }
  return 'processing';
}

function toReviewStatus(value: string | null | undefined) {
  const normalized = (value ?? '').toLowerCase();
  if (normalized === 'completed' || normalized === 'reviewed') return 'reviewed';
  if (normalized === 'submitted' || normalized === 'queued' || normalized === 'in_review' || normalized === 'pending') return 'pending';
  return 'not_requested';
}

function scoreRangeDisplay(value: string | null | undefined): string {
  return domainScoreRangeDisplay(value);
}

function formatCurrency(amount: number | string | null | undefined, currency = 'AUD'): string {
  return domainFormatCurrency(amount, currency);
}

function toBillingStatus(value: string | null | undefined): Invoice['status'] {
  const normalized = (value ?? '').toLowerCase();
  if (normalized === 'pending') return 'Pending';
  if (normalized === 'failed') return 'Failed';
  return 'Paid';
}

function normalizeWaveformPeaks(value: unknown): number[] {
  return domainNormalizeWaveformPeaks(value);
}

function parseCriterionScore(scoreRange: string | null | undefined): number {
  return domainParseCriterionScore(scoreRange);
}

function scoreToGrade(score: number): string {
  return domainScoreToGrade(score);
}

function normalizeCriterionName(code: string | null | undefined): string {
  switch ((code ?? '').toLowerCase()) {
    case 'purpose':
      return 'Purpose';
    case 'content':
      return 'Content';
    case 'conciseness':
    case 'conciseness_clarity':
      return 'Conciseness & Clarity';
    case 'genre':
    case 'genre_style':
      return 'Genre & Style';
    case 'organization':
    case 'organisation_layout':
      return 'Organisation & Layout';
    case 'language':
      return 'Language';
    case 'intelligibility':
      return 'Intelligibility';
    case 'fluency':
      return 'Fluency';
    case 'appropriateness':
    case 'appropriateness_of_language':
      return 'Appropriateness of Language';
    case 'grammar':
    case 'grammar_expression':
    case 'resources_of_grammar_and_expression':
      return 'Resources of Grammar & Expression';
    case 'relationshipbuilding':
    case 'relationship_building':
      return 'Relationship Building';
    case 'patientperspective':
    case 'patient_perspective':
      return "Understanding & Incorporating Patient's Perspective";
    case 'providingstructure':
    case 'providing_structure':
      return 'Providing Structure';
    case 'informationgathering':
    case 'information_gathering':
      return 'Information Gathering';
    case 'informationgiving':
    case 'information_giving':
      return 'Information Giving';
    default:
      return titleCase(code ?? 'Criterion');
  }
}

export interface WritingLintResponse {
  findings: LintFinding[];
  totals: { critical: number; major: number; minor: number; info: number };
}

export interface SpeakingAuditResponse {
  findings: LintFinding[];
}

export interface AiCompletionResponse {
  completion: string;
  rulebookVersion: string;
  appliedRuleIds: string[];
  metadata: {
    rulebookVersion: string;
    rulebookKind: 'writing' | 'speaking';
    profession: string;
    scoringPassMark: number;
    scoringGrade: 'B' | 'C+';
    appliedRulesCount: number;
  };
  promptHeadSnippet?: string;
}

type ApiClientInit = Omit<RequestInit, 'body' | 'method'>;
type ApiClientBody = unknown;

function isRequestBody(value: unknown): value is BodyInit {
  return (
    typeof value === 'string'
    || (typeof FormData !== 'undefined' && value instanceof FormData)
    || (typeof URLSearchParams !== 'undefined' && value instanceof URLSearchParams)
    || (typeof Blob !== 'undefined' && value instanceof Blob)
    || (typeof ArrayBuffer !== 'undefined' && value instanceof ArrayBuffer)
    || (typeof ReadableStream !== 'undefined' && value instanceof ReadableStream)
  );
}

function toRequestBody(body: ApiClientBody): { body?: BodyInit; json: boolean } {
  if (body === undefined) {
    return { json: true };
  }

  if (isRequestBody(body)) {
    return {
      body,
      json: !(typeof FormData !== 'undefined' && body instanceof FormData),
    };
  }

  return {
    body: JSON.stringify(body),
    json: true,
  };
}

/**
 * Central API client for application code.
 *
 * All backend calls from app/components/hooks/lib code should use this client
 * (or the typed helpers in this file) so retries, auth headers, CSRF, timeout
 * handling and normalized `ApiError` behavior stay consistent.
 */
export const apiClient = {
  request: apiRequest,
  get<T = any>(path: string, init?: ApiClientInit): Promise<T> {
    return apiRequest<T>(path, { ...init, method: 'GET' });
  },
  post<T = any>(path: string, body?: ApiClientBody, init?: ApiClientInit): Promise<T> {
    const payload = toRequestBody(body);
    return apiRequest<T>(path, { ...init, method: 'POST', body: payload.body }, { json: payload.json });
  },
  postWithAcceptedStatuses<T = any>(path: string, body: ApiClientBody, acceptedStatuses: number[], init?: ApiClientInit): Promise<T> {
    const payload = toRequestBody(body);
    return apiRequest<T>(
      path,
      { ...init, method: 'POST', body: payload.body },
      { json: payload.json, acceptedStatuses },
    );
  },
  put<T = any>(path: string, body?: ApiClientBody, init?: ApiClientInit): Promise<T> {
    const payload = toRequestBody(body);
    return apiRequest<T>(path, { ...init, method: 'PUT', body: payload.body }, { json: payload.json });
  },
  patch<T = any>(path: string, body?: ApiClientBody, init?: ApiClientInit): Promise<T> {
    const payload = toRequestBody(body);
    return apiRequest<T>(path, { ...init, method: 'PATCH', body: payload.body }, { json: payload.json });
  },
  delete<T = any>(path: string, init?: ApiClientInit): Promise<T> {
    return apiRequest<T>(path, { ...init, method: 'DELETE' });
  },
  postForm<T = any>(path: string, body: FormData, init?: ApiClientInit): Promise<T> {
    return apiRequest<T>(path, { ...init, method: 'POST', body }, { json: false });
  },
};

async function uploadBinary(pathOrUrl: string, blob: Blob): Promise<void> {
  const response = await fetchWithTimeout(resolveApiUploadUrl(pathOrUrl), {
    method: 'PUT',
    headers: await getHeaders(pathOrUrl, { 'Content-Type': blob.type || 'audio/webm' }, { json: false }),
    body: blob,
  }, 90_000);

  if (!response.ok) {
    let message = `Upload failed: ${response.status}`;
    try {
      const error = await response.json();
      message = error.message ?? error.title ?? message;
    } catch (err) {
      console.error('[API] uploadBinary: failed to parse error response:', err);
    }
    throw new Error(message);
  }
}

// ═══════════════════ RULEBOOK / GROUNDED AI API ═══════════════════

export async function fetchWritingRulebook(profession = 'medicine'): Promise<Rulebook> {
  return apiRequest<Rulebook>(`/v1/rulebooks/writing/${profession}`);
}

export async function fetchSpeakingRulebook(profession = 'medicine'): Promise<Rulebook> {
  return apiRequest<Rulebook>(`/v1/rulebooks/speaking/${profession}`);
}

export async function fetchRulebookRule(
  kind: 'writing' | 'speaking',
  profession: string,
  ruleId: string,
): Promise<Record<string, unknown>> {
  return apiRequest<Record<string, unknown>>(`/v1/rulebooks/${kind}/${profession}/rule/${encodeURIComponent(ruleId)}`);
}

export async function fetchRulebookAssessment(kind: 'writing' | 'speaking'): Promise<Record<string, unknown>> {
  return apiRequest<Record<string, unknown>>(`/v1/rulebooks/assessment/${kind}`);
}

// ═══════════════════ WRITING WEAKNESS ANALYTICS (spec §14) ═══════════════════

export interface WritingWeaknessTagRow {
  tag: string;
  label: string;
  count: number;
  share: number;
}

export interface WritingWeaknessCriterionRow {
  criterion: string;
  label: string;
  count: number;
  share: number;
}

export interface WritingWeaknessTrendBucket {
  date: string;
  count: number;
}

export interface WritingGradeTrendPoint {
  date: string;
  gradeRange: string;
  scoreRange: string;
}

export interface WritingPurposeTrendPoint {
  date: string;
  score: number;
  maxScore: number;
}

export interface WritingWeaknessSummary {
  totalObservations: number;
  topTags: WritingWeaknessTagRow[];
  byCriterion: WritingWeaknessCriterionRow[];
  trend: WritingWeaknessTrendBucket[];
  firstSeenAt: string;
  lastSeenAt: string;
  gradeTrend: WritingGradeTrendPoint[];
  purposeTrend: WritingPurposeTrendPoint[];
}

export async function fetchWritingWeaknesses(days = 14): Promise<WritingWeaknessSummary> {
  const clamped = Math.max(7, Math.min(90, Math.floor(days) || 14));
  return apiRequest<WritingWeaknessSummary>(`/v1/writing/analytics/weaknesses?days=${clamped}`);
}

// ═══════════════ EXPERT — ASSIGNED-TO-ME QUEUE (spec §4 Phase 4) ═══════════════

export interface ExpertAssignedItem {
  reviewRequestId: string;
  attemptId: string;
  subtestCode: 'writing';
  professionId: string | null;
  taskTitle: string;
  learnerDisplayName: string;
  letterType: string | null;
  assignedAt: string;
  slaDueAt: string;
  slaState: 'on_track' | 'at_risk' | 'overdue';
  turnaroundOption: string;
  reviewerCompensation: number;
  claimState: string;
}

export async function fetchExpertAssignedReviews(): Promise<ExpertAssignedItem[]> {
  return apiRequest<ExpertAssignedItem[]>('/v1/expert/queue/assigned-to-me');
}

// ═══════════════ WRITING DUAL AI + TUTOR ASSESSMENT (spec §12.E) ═══════════════
//
// Dual-assessment uses its own short-form criterion codes (purpose / content /
// conciseness / genre / organization / language) to match the backend
// payload at /v1/writing/evaluations/{id}/dual-assessment. These are
// intentionally distinct from `WritingCriterionCode` in lib/scoring.ts,
// which uses the long form (conciseness_clarity / genre_style /
// organisation_layout / language) for the rulebook scoring path.

export type WritingDualCriterionCode =
  | 'purpose' | 'content' | 'conciseness'
  | 'genre' | 'organization' | 'language';

export interface WritingCriterionScore {
  score: number;
  maxScore: number;
  rationale?: string | null;
  evidenceQuotes?: string[] | null;
}

export interface WritingAiTrack {
  assessmentId: string;
  generatedAt: string;
  confidenceBand: string;
  scoreRange: string;
  gradeRange: string | null;
  criterionScores: Record<WritingDualCriterionCode, WritingCriterionScore>;
  isAdvisory: boolean;
}

export interface WritingTutorTrack {
  assessmentId: string;
  tutorId: string;
  tutorName: string;
  criterionScores: Record<WritingDualCriterionCode, WritingCriterionScore>;
  overallFeedback?: string | null;
  isFinal: boolean;
  submittedAt: string;
}

export interface WritingDualDivergence {
  perCriterion: Record<WritingDualCriterionCode, number>;
  scaledDelta: number;
  agreementBand: 'close' | 'moderate' | 'wide';
}

export interface WritingDualAssessment {
  evaluationId: string;
  attemptId: string;
  subtestCode: 'writing';
  ai: WritingAiTrack;
  tutor: WritingTutorTrack | null;
  divergence: WritingDualDivergence | null;
}

export async function fetchWritingDualAssessment(evaluationId: string): Promise<WritingDualAssessment | null> {
  try {
    return await apiRequest<WritingDualAssessment>(`/v1/writing/evaluations/${encodeURIComponent(evaluationId)}/dual-assessment`);
  } catch (error) {
    if (isApiError(error) && error.status === 404) return null;
    throw error;
  }
}

export async function lintWritingViaApi(input: WritingLintInput): Promise<WritingLintResponse> {
  return apiRequest<WritingLintResponse>('/v1/writing/lint', {
    method: 'POST',
    body: JSON.stringify({
      letterText: input.letterText,
      attemptId: input.attemptId,
      contentId: input.contentId,
      letterType: input.letterType,
      recipientSpecialty: input.recipientSpecialty,
      recipientName: input.recipientName,
      patientAge: input.patientAge,
      patientIsMinor: input.patientIsMinor,
      caseNotesMarkers: input.caseNotesMarkers,
      profession: input.profession ?? 'medicine',
    }),
  });
}

export async function auditSpeakingViaApi(input: SpeakingAuditInput): Promise<SpeakingAuditResponse> {
  return apiRequest<SpeakingAuditResponse>('/v1/speaking/audit', {
    method: 'POST',
    body: JSON.stringify({
      transcript: input.transcript,
      cardType: input.cardType,
      profession: input.profession ?? 'medicine',
      silenceAfterDiagnosisMs: input.silenceAfterDiagnosisMs,
    }),
  });
}

export async function completeGroundedAi(
  context: AiGroundingContext,
  userInput: string,
  provider = '',
  model = '',
): Promise<AiCompletionResponse> {
  return apiRequest<AiCompletionResponse>('/v1/ai/complete', {
    method: 'POST',
    body: JSON.stringify({
      kind: context.kind,
      profession: context.profession,
      task: context.task,
      letterType: context.letterType,
      cardType: context.cardType,
      candidateCountry: context.candidateCountry,
      userInput,
      provider,
      model,
    }),
  });
}

export async function fetchAuthorizedObjectUrl(pathOrUrl: string): Promise<string> {
  const response = await fetchWithTimeout(resolveBrowserApiResourceUrl(pathOrUrl) ?? resolveApiUrl(pathOrUrl), {
    headers: await getHeaders(pathOrUrl, undefined, { json: false }),
  });

  if (!response.ok) {
    let message = `Request failed: ${response.status}`;
    try {
      const error = await response.json();
      message = error.message ?? error.title ?? message;
    } catch (err) {
      console.error('[API] fetchAuthorizedObjectUrl: failed to parse error response:', err);
    }
    throw new Error(message);
  }

  const blob = await response.blob();
  return URL.createObjectURL(blob);
}

/**
 * Authorized blob fetch that returns the raw Blob (caller owns
 * createObjectURL/revoke). Use when the content type matters — e.g. the admin
 * proof viewer branches between an inline image and an embedded PDF.
 */
export async function fetchAuthorizedBlob(pathOrUrl: string): Promise<Blob> {
  const response = await fetchWithTimeout(resolveBrowserApiResourceUrl(pathOrUrl) ?? resolveApiUrl(pathOrUrl), {
    headers: await getHeaders(pathOrUrl, undefined, { json: false }),
  });

  if (!response.ok) {
    let message = `Request failed: ${response.status}`;
    try {
      const error = await response.json();
      message = error.message ?? error.title ?? message;
    } catch (err) {
      console.error('[API] fetchAuthorizedBlob: failed to parse error response:', err);
    }
    throw new Error(message);
  }

  return response.blob();
}

function cacheGet(key: string): string | null {
  if (!isBrowser()) return null;
  return window.localStorage.getItem(key);
}

function cacheSet(key: string, value: string) {
  if (!isBrowser()) return;
  window.localStorage.setItem(key, value);
}

function cacheRemove(key: string) {
  if (!isBrowser()) return;
  window.localStorage.removeItem(key);
}

function attemptCacheKey(subtest: string, contentId: string, mode = 'default') {
  return `oet.${subtest}.attempt.${mode}.${contentId}`;
}

function evaluationCacheKey(subtest: string, contentId: string) {
  return `oet.${subtest}.evaluation.${contentId}`;
}

function isReusableAttemptState(state: unknown) {
  return state === 'not_started' || state === 'in_progress' || state === 'paused';
}

async function ensureAttempt(subtest: 'writing' | 'speaking' | 'reading' | 'listening', contentId: string, mode: string) {
  const key = attemptCacheKey(subtest, contentId, mode);
  const cached = cacheGet(key);

  if (cached) {
    try {
      const existing = await apiRequest<ApiRecord>(`/v1/${subtest}/attempts/${cached}`);
      if (isReusableAttemptState(existing.state)) {
        return existing;
      }
      cacheRemove(key);
    } catch (err) {
      if (err instanceof ApiError && err.status >= 500) {
        console.error('[API] ensureAttempt: server error checking existing attempt:', err);
      } else {
        console.error('[API] ensureAttempt: failed to verify existing attempt:', err);
      }
      cacheRemove(key);
    }
  }

  const context = mode === 'diagnostic' ? 'diagnostic' : mode === 'exam' ? 'exam' : 'practice';
  const attemptMode = mode === 'diagnostic' ? 'exam' : mode;
  const created = await apiRequest<ApiRecord>(`/v1/${subtest}/attempts`, {
    method: 'POST',
    body: JSON.stringify({ contentId, context, mode: attemptMode, deviceType: 'web', parentAttemptId: null }),
  });
  cacheSet(key, created.attemptId);
  if (subtest === 'writing' || subtest === 'speaking') {
    // Graded activities consume credits at submit/card-reveal, not here.
  } else {
    void import('@/lib/credit-feedback').then((m) => m.announceCreditUsage(subtest));
  }
  return created;
}

export interface WritingAttemptSession {
  attemptId: string;
  contentId: string;
  context: string;
  mode: string;
  state: string;
  startedAt: string;
  draftVersion: number;
  draftContent: string;
  feedbackMessage?: string | null;
}

export type WritingAttemptMode = 'exam' | 'learning' | 'diagnostic';

export async function ensureWritingAttempt(taskId: string, mode: WritingAttemptMode = 'exam'): Promise<WritingAttemptSession> {
  const attempt = await ensureAttempt('writing', taskId, mode);
  const startedAt = typeof attempt.startedAt === 'string' ? attempt.startedAt : '';
  if (!startedAt || Number.isNaN(Date.parse(startedAt))) {
    throw new Error('Writing attempt start time was missing from the API response.');
  }

  return {
    attemptId: String(attempt.attemptId ?? ''),
    contentId: String(attempt.contentId ?? taskId),
    context: String(attempt.context ?? (mode === 'diagnostic' ? 'diagnostic' : mode === 'exam' ? 'exam' : 'practice')),
    mode: String(attempt.mode ?? mode),
    state: String(attempt.state ?? 'in_progress'),
    startedAt,
    draftVersion: Number(attempt.draftVersion ?? 1),
    feedbackMessage: typeof attempt.feedbackMessage === 'string' ? attempt.feedbackMessage : null,
    draftContent: typeof attempt.draftContent === 'string' ? attempt.draftContent : '',
  };
}

async function latestEvaluationIdForContent(contentId: string, subtest: string): Promise<string | null> {
  const cached = cacheGet(evaluationCacheKey(subtest, contentId));
  if (cached) return cached;

  const submissions = await apiRequest<{ items: ApiRecord[] }>('/v1/submissions');
  const match = submissions.items.find((item) => item.contentId === contentId && String(item.subtest).toLowerCase() === subtest.toLowerCase());
  if (match?.evaluationId) {
    cacheSet(evaluationCacheKey(subtest, contentId), match.evaluationId);
    return match.evaluationId;
  }
  return null;
}

async function resolveReviewTarget(submissionId: string): Promise<{ attemptId: string; subtest: 'writing' | 'speaking' }> {
  if (submissionId.startsWith('we-')) {
    const summary = await apiRequest<ApiRecord>(`/v1/writing/evaluations/${submissionId}/summary`);
    return { attemptId: summary.attemptId, subtest: 'writing' };
  }

  if (submissionId.startsWith('se-')) {
    const summary = await apiRequest<ApiRecord>(`/v1/speaking/evaluations/${submissionId}/summary`);
    return { attemptId: summary.attemptId, subtest: 'speaking' };
  }

  if (submissionId.startsWith('wa-') || submissionId.startsWith('ws-')) {
    return { attemptId: submissionId.replace(/^ws-/, 'wa-'), subtest: 'writing' };
  }

  if (submissionId.startsWith('sa-') || submissionId.startsWith('sr-')) {
    return { attemptId: submissionId.replace(/^sr-/, 'sa-'), subtest: 'speaking' };
  }

  const submissions = await apiRequest<{ items: ApiRecord[] }>('/v1/submissions');
  const match = submissions.items.find((item) => item.submissionId === submissionId || item.evaluationId === submissionId);
  if (match) {
    return { attemptId: match.submissionId, subtest: String(match.subtest).toLowerCase() === 'speaking' ? 'speaking' : 'writing' };
  }

  throw new ApiError(404, 'not_found', 'Submission not found.', false);
}

function mapWritingTask(item: ApiRecord): WritingTask {
  return {
    id: item.contentId,
    title: item.title,
    difficulty: titleCase(item.difficulty) as WritingTask['difficulty'],
    profession: titleCase(item.professionId),
    time: minutesToLabel(item.estimatedDurationMinutes),
    criteriaFocus: Array.isArray(item.criteriaFocus) ? item.criteriaFocus.map(normalizeCriterionName).join(', ') : '',
    scenarioType: titleCase(item.scenarioType),
    caseNotes: item.caseNotes ?? '',
    letterType: titleCase(item.letterType ?? item.taskType ?? item.scenarioType),
    scenario: typeof item.scenario === 'string' ? item.scenario : undefined,
    taskDate: typeof item.taskDate === 'string' ? item.taskDate : typeof item.date === 'string' ? item.date : undefined,
    writerRole: typeof item.writerRole === 'string' ? item.writerRole : undefined,
    recipient: typeof item.recipient === 'string' ? item.recipient : typeof item.recipientName === 'string' ? item.recipientName : undefined,
    purpose: typeof item.purpose === 'string' ? item.purpose : undefined,
    status: typeof item.status === 'string' ? titleCase(item.status) : undefined,
  };
}

function mapSpeakingTask(item: ApiRecord): SpeakingTask {
  const criteriaFocusTags = toStringArray(item.criteriaFocus ?? item.criteriaFocusTags);
  return {
    id: item.contentId,
    title: item.title,
    scenarioType: titleCase(item.scenarioType),
    difficulty: titleCase(item.difficulty) as SpeakingTask['difficulty'],
    profession: titleCase(item.professionId),
    criteriaFocus: criteriaFocusTags.map(normalizeCriterionName).join(', '),
    duration: minutesToLabel(item.estimatedDurationMinutes),
    prepTimeSeconds: typeof item.prepTimeSeconds === 'number' ? item.prepTimeSeconds : undefined,
    roleplayTimeSeconds: typeof item.roleplayTimeSeconds === 'number' ? item.roleplayTimeSeconds : undefined,
    patientEmotion: typeof item.patientEmotion === 'string' ? item.patientEmotion : undefined,
    communicationGoal: typeof item.communicationGoal === 'string' ? item.communicationGoal : undefined,
    clinicalTopic: typeof item.clinicalTopic === 'string' ? item.clinicalTopic : undefined,
    criteriaFocusTags,
    disclaimer: typeof item.disclaimer === 'string' ? item.disclaimer : undefined,
  };
}

function mapCriterionFeedback(criterionScores: ApiRecord[], feedbackItems: ApiRecord[]): CriterionFeedback[] {
  return criterionScores.map((criterion) => {
    const score = parseCriterionScore(criterion.scoreRange);
    const criterionCode = String(criterion.criterionCode ?? '').toLowerCase();
    const maxScore = Object.prototype.hasOwnProperty.call(WRITING_CRITERION_MAX_SCORES, criterionCode)
      ? WRITING_CRITERION_MAX_SCORES[criterionCode as WritingCriterionCode]
      : 7;
    const relatedFeedback = feedbackItems.filter((item) => item.criterionCode === criterionCode);

    return {
      name: normalizeCriterionName(criterionCode),
      score,
      maxScore,
      grade: scoreToGrade(score),
      explanation: criterion.explanation ?? '',
      anchoredComments: relatedFeedback.map((item, index) => {
        const rawSeverity = typeof item.severity === 'string' ? item.severity.toLowerCase() : '';
        const severity: AnchoredComment['severity'] | undefined =
          rawSeverity === 'critical' || rawSeverity === 'major' || rawSeverity === 'minor' || rawSeverity === 'info'
            ? (rawSeverity as AnchoredComment['severity'])
            : undefined;
        const rawSource = typeof item.source === 'string' ? item.source.toLowerCase() : '';
        const source: AnchoredComment['source'] | undefined =
          rawSource === 'rule_engine' || rawSource === 'ai' ? (rawSource as AnchoredComment['source']) : undefined;
        return {
          id: item.feedbackItemId ?? `${criterionCode}-${index}`,
          text: item.anchor?.snippet ?? item.anchor?.lineId ?? normalizeCriterionName(criterionCode),
          comment: item.message ?? '',
          ruleId: typeof item.ruleId === 'string' && item.ruleId ? item.ruleId : undefined,
          severity,
          source,
          suggestedFix:
            typeof item.suggestedFix === 'string' && item.suggestedFix ? item.suggestedFix : undefined,
        };
      }),
      omissions: [],
      unnecessaryDetails: [],
      revisionSuggestions: relatedFeedback.map((item) => item.suggestedFix).filter(Boolean),
      strengths: [],
      issues: [],
    };
  });
}

function readingErrorType(question: ApiRecord): string {
  return question.type === 'mcq' ? 'Inference' : 'Detail Extraction';
}

function parseScoreValue(range: string): number {
  if (!range) return 0;
  const numeric = range.match(/\d+/)?.[0];
  return numeric ? Number(numeric) : 0;
}

function mockSubtestColors(name: string) {
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

function toSpeakingEvaluationRouteId(value: string): string | null {
  if (!value) return null;
  if (value.startsWith('se-')) return value;
  if (value.startsWith('sa-')) return `se-${value.slice(3)}`;
  return null;
}

function rewriteLegacyLearnerRoute(pathname: string, search: string, hash: string): string {
  if (pathname === '/dashboard') return `/${search}${hash}`.replace(/\/\?/, '/?');
  if (pathname === '/history') return `/submissions${search}${hash}`;
  if (pathname === '/reviews') return `/submissions${search}${hash}`;
  if (pathname === '/speaking/tasks') return `/speaking/selection${search}${hash}`;

  if (pathname.startsWith('/speaking/review/')) {
    const legacyId = pathname.slice('/speaking/review/'.length);
    const evaluationId = toSpeakingEvaluationRouteId(legacyId);
    if (evaluationId) {
      return `/speaking/phrasing/${evaluationId}${search}${hash}`;
    }
    return `/speaking/selection${search}${hash}`;
  }

  if (pathname.startsWith('/speaking/result/')) {
    const evaluationId = pathname.slice('/speaking/result/'.length);
    return `/speaking/results/${evaluationId}${search}${hash}`;
  }

  if (pathname.startsWith('/speaking/attempt/')) {
    const legacyId = pathname.slice('/speaking/attempt/'.length);
    const evaluationId = toSpeakingEvaluationRouteId(legacyId);
    if (evaluationId) {
      return `/speaking/results/${evaluationId}${search}${hash}`;
    }
    return `/speaking/selection${search}${hash}`;
  }

  if (pathname === '/writing/tasks') {
    return `/writing/practice/library${search}${hash}`;
  }

  if (pathname.startsWith('/writing/tasks/')) {
    // Legacy V1 task IDs have no mapping into the V2 scenario library, so the
    // deep link can't be preserved. Route to the writing landing instead of the
    // retired V1 player.
    return `/writing${search}${hash}`;
  }

  if (pathname.startsWith('/reading/task/')) {
    return `/reading${search}${hash}`;
  }

  if (pathname.startsWith('/listening/task/')) {
    const taskOrEvaluationId = pathname.slice('/listening/task/'.length);
    if (taskOrEvaluationId.startsWith('lt-')) {
      return `/listening/player/${taskOrEvaluationId}${search}${hash}`;
    }
    return `/listening${search}${hash}`;
  }

  return `${pathname}${search}${hash}`;
}

function normalizeAppRoute(route: string) {
  const withoutAppPrefix = route === '/app'
    ? '/'
    : route.startsWith('/app/')
      ? route.replace('/app', '')
      : route;

  if (!withoutAppPrefix.startsWith('/')) {
    return withoutAppPrefix;
  }

  const parsed = new URL(withoutAppPrefix, 'http://localhost');
  return rewriteLegacyLearnerRoute(parsed.pathname, parsed.search, parsed.hash);
}

function normalizeRouteValues<T>(value: T): T {
  if (Array.isArray(value)) {
    return value.map((item) => normalizeRouteValues(item)) as T;
  }

  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>).map(([key, nestedValue]) => {
        if (typeof nestedValue === 'string' && (nestedValue.startsWith('/app') || key.toLowerCase().includes('route') || key.toLowerCase().includes('href'))) {
          return [key, normalizeAppRoute(nestedValue)];
        }
        return [key, normalizeRouteValues(nestedValue)];
      }),
    ) as T;
  }

  return value;
}

export async function fetchUserProfile(): Promise<UserProfile> {
  const bootstrap = await apiRequest<ApiRecord>('/v1/me/bootstrap');
  const user = bootstrap.user;
  const goals = bootstrap.goals ?? {};

  return {
    id: user.userId,
    email: user.email,
    displayName: user.displayName,
    profession: titleCase(goals.professionId ?? user.activeProfessionId),
    examFamilyCode: toExamFamilyCode(goals.examFamilyCode),
    examDate: goals.targetExamDate ?? null,
    targetScores: {
      Writing: goals.targetScoresBySubtest?.writing ?? null,
      Speaking: goals.targetScoresBySubtest?.speaking ?? null,
      Reading: goals.targetScoresBySubtest?.reading ?? null,
      Listening: goals.targetScoresBySubtest?.listening ?? null,
    },
    previousAttempts: goals.previousAttemptSummary ?? 0,
    weakSubTests: (goals.weakSubtestSelfReport ?? []).map((value: string) => toSubTest(value)),
    studyHoursPerWeek: goals.studyHoursPerWeek ?? 0,
    targetCountry: goals.targetCountry ?? '',
    targetExamMode: (goals.targetExamMode ?? null) as string | null,
    confidenceLevel: (goals.confidenceLevel ?? null) as string | null,
    onboardingComplete: Boolean(bootstrap.onboarding?.completed),
    goalsComplete: Boolean(goals.submittedAt || goals.professionId),
    diagnosticComplete: Array.isArray(bootstrap.readiness?.subTests) && bootstrap.readiness.subTests.length > 0,
    createdAt: user.createdAt,
  };
}

export async function fetchMockSpeakingAccess(): Promise<{ requiresAiOnly: boolean; daysUntilExam: number | null }> {
  const data = await apiRequest<ApiRecord>('/v1/mocks/speaking-access');
  return {
    requiresAiOnly: Boolean(data.requiresAiOnly),
    daysUntilExam: data.daysUntilExam === null || data.daysUntilExam === undefined ? null : Number(data.daysUntilExam),
  };
}

export async function fetchOnboardingState(): Promise<{ completed: boolean; currentStep: number; stepCount: number; canSkip: boolean; checkpoint: string; resumeRoute: string; examDateRequired: boolean; }> {
  const data = await apiRequest<ApiRecord>('/v1/learner/onboarding/state');
  return {
    completed: Boolean(data.completed),
    currentStep: Number(data.currentStep ?? 1),
    stepCount: Number(data.stepCount ?? 4),
    canSkip: Boolean(data.canSkip),
    checkpoint: data.checkpoint ?? 'welcome',
    resumeRoute: data.resumeRoute ?? '/onboarding',
    examDateRequired: Boolean(data.examDateRequired),
  };
}

export async function startOnboarding(): Promise<void> {
  await apiRequest('/v1/learner/onboarding/start', { method: 'POST' });
}

export async function completeOnboarding(): Promise<void> {
  await apiRequest('/v1/learner/onboarding/complete', { method: 'POST' });
}

// ── Onboarding product-tour state ───────────────────────────────────────────
// Backed by /v1/onboarding/tours (GET + PATCH). Available to every authenticated
// role so learner, expert/tutor, and admin workspaces can persist their own tours.
export interface OnboardingTourState {
  onboardingVersion: number;
  role: string;
  lastSeenTourVersion: number;
  completed: {
    intro: boolean;
    dashboard: boolean;
    listening: boolean;
    reading: boolean;
    writing: boolean;
    speaking: boolean;
    admin: boolean;
    expert: boolean;
  };
  skippedTours: string[];
  dismissedTips: string[];
}

function toOnboardingTourState(data: ApiRecord): OnboardingTourState {
  const completed = (data.completed ?? {}) as ApiRecord;
  return {
    onboardingVersion: Number(data.onboardingVersion ?? 1),
    role: typeof data.role === 'string' ? data.role : 'learner',
    lastSeenTourVersion: Number(data.lastSeenTourVersion ?? 0),
    completed: {
      intro: Boolean(completed.intro),
      dashboard: Boolean(completed.dashboard),
      listening: Boolean(completed.listening),
      reading: Boolean(completed.reading),
      writing: Boolean(completed.writing),
      speaking: Boolean(completed.speaking),
      admin: Boolean(completed.admin),
      expert: Boolean(completed.expert),
    },
    skippedTours: Array.isArray(data.skippedTours) ? data.skippedTours.map(String) : [],
    dismissedTips: Array.isArray(data.dismissedTips) ? data.dismissedTips.map(String) : [],
  };
}

export async function fetchTourState(): Promise<OnboardingTourState> {
  const data = await apiRequest<ApiRecord>('/v1/onboarding/tours');
  return toOnboardingTourState(data);
}

export async function markTour(
  tourId: string,
  status: 'completed' | 'skipped' | 'dismissed',
  role?: string,
): Promise<OnboardingTourState> {
  const data = await apiRequest<ApiRecord>('/v1/onboarding/tours', {
    method: 'PATCH',
    body: JSON.stringify({ tourId, status, role }),
  });
  return toOnboardingTourState(data);
}

export interface DiagnosticOverviewResponse {
  subtests?: { subtest: string; estimatedDurationMinutes: number }[];
  estimatedTotalMinutes?: number;
  disclaimer?: string;
}

export async function fetchDiagnosticOverview(): Promise<DiagnosticOverviewResponse> {
  return apiRequest<DiagnosticOverviewResponse>('/v1/diagnostic/overview');
}

export interface DashboardHomeResponse {
  freeze?: { currentFreeze?: unknown };
  cards?: {
    examDate?: { value?: string };
    pendingExpertReviews?: { count?: number };
    nextMockRecommendation?: unknown;
  };
  [key: string]: unknown;
}

export async function fetchDashboardHome(): Promise<DashboardHomeResponse> {
  const data = await apiRequest<DashboardHomeResponse>('/v1/learner/dashboard');
  return normalizeRouteValues(data) as DashboardHomeResponse;
}

export interface EngagementResponse {
  currentStreak?: number;
  longestStreak?: number;
  lastPracticeDate?: string | null;
  totalPracticeMinutes?: number;
  totalPracticeSessions?: number;
  avgSessionMinutes?: number;
  weeklyActivity?: { day: string; active: boolean }[];
  streakFreezeAvailable?: boolean;
  streakFreezeUsedThisWeek?: boolean;
}

export async function fetchEngagement(): Promise<EngagementResponse> {
  return apiRequest<EngagementResponse>('/v1/learner/engagement');
}

export interface WalletTransactionsResponse {
  balance: number;
  lastUpdatedAt?: string;
  transactions: unknown[];
}

export async function fetchWalletTransactions(limit = 20): Promise<WalletTransactionsResponse> {
  return apiRequest<WalletTransactionsResponse>(`/v1/billing/wallet/transactions?limit=${limit}`);
}

export interface WalletTopUpResponse {
  /** PayPal order id (embedded flow) / provider session id. Use as the embedded createOrder result. */
  sessionId?: string;
  gateway?: string;
  /** Present for redirect gateways (Stripe and the hosted fallback); absent for embedded PayPal. */
  checkoutUrl?: string;
  totalCredits?: number;
  status?: string;
}

export async function createWalletTopUp(
  amount: number,
  gateway: string,
  idempotencyKey?: string,
): Promise<WalletTopUpResponse> {
  return apiRequest<WalletTopUpResponse>('/v1/billing/wallet/top-up', {
    method: 'POST',
    body: JSON.stringify({ amount, gateway, idempotencyKey: idempotencyKey ?? null }),
  });
}

export interface WalletTopUpTier {
  amount: number;
  credits: number;
  bonus: number;
  totalCredits: number;
  label: string;
  isPopular: boolean;
}

export interface WalletTopUpTiersResponse {
  currency: string;
  tiers: WalletTopUpTier[];
}

export async function fetchWalletTopUpTiers(): Promise<WalletTopUpTiersResponse> {
  return apiRequest<WalletTopUpTiersResponse>('/v1/billing/wallet/top-up-tiers');
}

/** How a payment method initiates: an in-page SDK ("embedded", e.g. PayPal) or a
 *  hosted-checkout redirect ("redirect", e.g. Stripe / Checkout.com / Paymob / PayTabs). */
export type PaymentMethodMode = 'embedded' | 'iframe' | 'redirect';

export interface PaymentMethodOption {
  /** Gateway name passed back to checkout / top-up (e.g. "whop", "fawaterak"). */
  name: string;
  /** Learner-facing label for the method. */
  label: string;
  /** Icon hint (e.g. "credit-card", "paypal", "wallet"). */
  iconName: string;
  /** "embedded"/"iframe" stay on-site; "redirect" opens a hosted checkout. */
  mode: PaymentMethodMode;
  badge?: string | null;
  recommended?: boolean;
  region?: string;
}

export interface AvailablePaymentGatewaysResponse {
  gateways: string[];
  /** Rich metadata for the unified payment-method picker. Absent on older API builds. */
  methods?: PaymentMethodOption[];
}

export async function fetchAvailablePaymentGateways(): Promise<AvailablePaymentGatewaysResponse> {
  return apiRequest<AvailablePaymentGatewaysResponse>('/v1/billing/payment-gateways');
}

// ── PayPal Expanded (embedded) checkout ──────────────────────────────────────
export interface PayPalClientConfig {
  /** False when no client id is configured — the embedded UI is unavailable and the
   *  caller should fall back to the redirect flow. */
  enabled: boolean;
  /** Public PayPal client id for the browser SDK (never the secret). */
  clientId: string | null;
  currency: string;
  intent: string;
  components: string;
  environment: 'sandbox' | 'live' | string;
  /** Whether embedded Advanced Card Fields may render; when false, show buttons only. */
  advancedCardsEnabled: boolean;
}

export async function fetchPayPalClientConfig(): Promise<PayPalClientConfig> {
  return apiRequest<PayPalClientConfig>('/v1/billing/paypal/client-config');
}

export interface PaymentCaptureResult {
  status: 'completed' | 'failed' | 'pending' | string;
  orderId: string;
  captureId: string | null;
  redirectTo: string | null;
  failureReason: string | null;
}

/**
 * Resolves a safe in-app destination from a server-supplied `redirectTo`. Only same-origin
 * absolute paths are honoured: a value must start with a single `/` (not `//`, which is a
 * protocol-relative off-site URL, and not a `/\` backslash variant). Anything else falls
 * back to the provided default. Use this for every PayPal capture redirect.
 */
export function safePaymentRedirect(redirectTo: string | null | undefined, fallback: string): string {
  if (
    typeof redirectTo === 'string' &&
    redirectTo.startsWith('/') &&
    !redirectTo.startsWith('//') &&
    !redirectTo.startsWith('/\\')
  ) {
    return redirectTo;
  }
  return fallback;
}

/** Captures an approved PayPal order for the quote/wallet billing flow (onApprove). */
export async function captureBillingCheckout(orderId: string): Promise<PaymentCaptureResult> {
  return apiRequest<PaymentCaptureResult>(
    `/v1/billing/checkout-sessions/${encodeURIComponent(orderId)}/capture`,
    { method: 'POST' },
  );
}

export interface ExamFamiliesResponse {
  examFamilies?: { code: string; label?: string }[];
}

export async function fetchExamFamilies(): Promise<ExamFamiliesResponse> {
  return apiRequest<ExamFamiliesResponse>('/v1/reference/exam-families');
}

export interface SettingsDataResponse {
  audio?: { lowBandwidthMode?: boolean };
  [key: string]: unknown;
}

export async function fetchSettingsData(): Promise<SettingsDataResponse> {
  return apiRequest<SettingsDataResponse>('/v1/settings');
}

export async function fetchSettingsSection(section: SettingsSectionId): Promise<SettingsSectionData> {
  const data = await apiRequest<ApiRecord>(`/v1/settings/${section}`);
  return {
    section,
    values: normalizeRouteValues(data.values ?? {}),
  };
}

export interface UpdateSettingsSectionResponse {
  values?: Record<string, unknown>;
  [key: string]: unknown;
}

/**
 * `currentPassword` is only required by the backend when `section === 'profile'` and
 * `values.email` differs from the account's current email (H1 security gate — see
 * LearnerService.PatchSettingsSectionAsync). Every other section/field ignores it.
 */
export async function updateSettingsSection(section: 'profile' | 'goals' | 'notifications' | 'privacy' | 'accessibility' | 'audio' | 'study', values: Record<string, unknown>, currentPassword?: string): Promise<UpdateSettingsSectionResponse> {
  return apiRequest<UpdateSettingsSectionResponse>(`/v1/settings/${section}`, {
    method: 'PATCH',
    body: JSON.stringify(currentPassword ? { values, currentPassword } : { values }),
  });
}

/** Sets or clears (null) the learner's avatar. `avatarUrl` must already be a `/v1/media/{id}/content` path from `uploadMedia`. */
export async function updateMyAvatar(avatarUrl: string | null): Promise<CurrentUser> {
  return apiRequest<CurrentUser>('/v1/me/avatar', {
    method: 'PUT',
    body: JSON.stringify({ avatarUrl }),
  });
}

// ── Session Management ──

export interface ActiveSession {
  id: string;
  deviceInfo: string | null;
  ipAddress: string | null;
  lastUsedAt: string | null;
  createdAt: string;
  isCurrent: boolean;
  countryCode?: string | null;
  platform?: string | null;
  deviceId?: string | null;
}

/** The account's currently-trusted device (security spec §3.2) — null until
 * one is bootstrapped on first sign-in with a device id. */
export interface TrustedDeviceSelf {
  deviceName: string | null;
  platform: string | null;
  trustedAt: string;
  lastSeenAt: string | null;
  isCurrentDevice: boolean;
  activeDeviceCount?: number;
  maxDevices?: number;
}

export async function fetchActiveSessions(): Promise<ActiveSession[]> {
  const data = await apiRequest<{ sessions: ActiveSession[] }>('/v1/auth/sessions');
  return Array.isArray(data.sessions) ? data.sessions : [];
}

export async function fetchTrustedDevice(): Promise<TrustedDeviceSelf | null> {
  return (await apiRequest<TrustedDeviceSelf | null>('/v1/auth/device')) ?? null;
}

export async function revokeSession(sessionId: string): Promise<void> {
  await apiRequest<void>(`/v1/auth/sessions/${sessionId}`, { method: 'DELETE' });
}

export async function revokeAllOtherSessions(): Promise<{ revokedCount: number }> {
  return apiRequest<{ revokedCount: number }>('/v1/auth/sessions', { method: 'DELETE' });
}

export interface ReadingHomeResponse {
  [key: string]: unknown;
}

export async function fetchReadingHome(): Promise<ReadingHomeResponse> {
  const data = await apiRequest<ReadingHomeResponse>('/v1/reading-papers/home');
  return normalizeRouteValues(data) as ReadingHomeResponse;
}

export interface ListeningHomeResponse {
  [key: string]: unknown;
}

export async function fetchListeningHome(): Promise<ListeningHomeResponse> {
  const data = await apiRequest<ListeningHomeResponse>('/v1/listening/home');
  return normalizeRouteValues(data) as ListeningHomeResponse;
}

export interface WritingHomeResponse {
  recommendedTask?: Record<string, unknown> & {
    id?: string;
    contentId?: string;
    title?: string;
    criteriaFocus?: string | string[];
    scenarioType?: string;
    profession?: string;
    time?: string;
    estimatedDurationMinutes?: number;
    difficulty?: string;
  };
  reviewCredits?: { available?: number };
  fullMockEntry?: { title?: string; route?: string; rationale?: string };
  actions?: unknown[];
  latestEvaluation?: unknown | null;
  criterionDrillLibrary?: unknown[];
  [key: string]: unknown;
}

export async function fetchWritingHome(): Promise<WritingHomeResponse> {
  const data = await apiRequest<WritingHomeResponse>('/v1/writing/home');
  return normalizeRouteValues(data) as WritingHomeResponse;
}

export interface WritingWeaknessAnalyticsResponse {
  generatedAt: string;
  windowDays: number;
  points: WeaknessDataPoint[];
}

export async function fetchWritingWeaknessData(options: { days?: number } = {}): Promise<WritingWeaknessAnalyticsResponse> {
  const params = new URLSearchParams();
  if (options.days !== undefined) {
    params.set('days', String(options.days));
  }

  return apiRequest<WritingWeaknessAnalyticsResponse>(`/v1/writing/analytics/weaknesses${params.size ? `?${params}` : ''}`);
}

export async function fetchSpeakingHome(): Promise<SpeakingHome> {
  const data = await apiRequest<ApiRecord>('/v1/speaking/home');
  const normalized = normalizeRouteValues(data) as ApiRecord;
  return {
    recommendedRolePlay: normalized.recommendedRolePlay ? mapSpeakingTask(normalized.recommendedRolePlay) : null,
    commonIssuesToImprove: Array.isArray(normalized.commonIssuesToImprove) ? normalized.commonIssuesToImprove : [],
    drillGroups: Array.isArray(normalized.drillGroups)
      ? normalized.drillGroups.map((group: ApiRecord) => ({
        id: String(group.id ?? group.title ?? 'drill-group'),
        title: String(group.title ?? 'Speaking drill group'),
        items: Array.isArray(group.items)
          ? group.items.map((item: ApiRecord) => ({
            id: String(item.id ?? item.route ?? item.title ?? 'drill'),
            title: String(item.title ?? 'Open drill'),
            description: item.description,
            route: String(item.route ?? '/speaking/selection'),
          }))
          : [],
      }))
      : [],
    pastAttempts: Array.isArray(normalized.pastAttempts)
      ? normalized.pastAttempts.map((attempt: ApiRecord) => ({
        attemptId: String(attempt.attemptId ?? ''),
        state: String(attempt.state ?? 'unknown'),
        scoreEstimate: attempt.scoreEstimate ?? null,
        route: String(attempt.route ?? '/speaking/selection'),
      }))
      : [],
    reviewCredits: {
      available: Number(normalized.reviewCredits?.available ?? 0),
      route: String(normalized.reviewCredits?.route ?? '/reviews'),
      billingRoute: normalized.reviewCredits?.billingRoute,
    },
    supportEntries: Array.isArray(normalized.supportEntries)
      ? normalized.supportEntries.map((entry: ApiRecord) => ({
        id: String(entry.id ?? entry.route ?? entry.title ?? 'support-entry'),
        title: String(entry.title ?? 'Speaking support'),
        description: entry.description,
        route: String(entry.route ?? '/speaking/selection'),
      }))
      : [],
    featuredTasks: Array.isArray(normalized.featuredTasks) ? normalized.featuredTasks.map(mapSpeakingTask) : [],
    latestEvaluation: normalized.latestEvaluation ?? null,
    tips: Array.isArray(normalized.tips) ? normalized.tips : [],
    dashboardRoute: normalized.dashboardRoute,
    historyRoute: normalized.historyRoute,
    writingLibraryRoute: normalized.writingLibraryRoute,
    writingTaskRoute: normalized.writingTaskRoute,
    readingTaskRoute: normalized.readingTaskRoute,
    listeningTaskRoute: normalized.listeningTaskRoute,
  };
}

export interface MocksHomeResponse {
  reports?: Record<string, unknown>[];
  resumableAttempts?: unknown[];
  recommendedNextMock?: {
    id?: string;
    title?: string;
    rationale?: string;
    route?: string;
    latestOverallScore?: string | null;
    latestOverallGrade?: string | null;
    trend?: string | null;
    readiness?: {
      tier?: string;
      message?: string;
      passThreshold?: number;
      overallScore?: number;
    } | null;
  } | null;
  purchasedMockReviews?: unknown;
  collections?: { fullMocks?: unknown[]; subTestMocks?: unknown[] };
  emptyState?: { title?: string; description?: string; route?: string } | null;
  learnerProfession?: string | null;
  availableProfessions?: { id: string; label: string }[];
  scoreGuarantee?: unknown | null;
  cohortPercentile?: unknown | null;
  [key: string]: unknown;
}

export async function fetchMocksHome(): Promise<MocksHomeResponse> {
  const data = await apiRequest<MocksHomeResponse>('/v1/mocks');
  return normalizeRouteValues(data) as MocksHomeResponse;
}

export interface SpeakingDeviceCheckResponse {
  [key: string]: unknown;
}

export async function postSpeakingDeviceCheck(payload: {
  microphoneGranted: boolean;
  networkStable: boolean;
  deviceType?: string;
  taskId?: string;
  noiseLevel?: number;
  noiseAcceptable?: boolean;
}): Promise<SpeakingDeviceCheckResponse> {
  return apiRequest<SpeakingDeviceCheckResponse>('/v1/speaking/device-checks', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function setActiveProfession(professionId: string): Promise<void> {
  if (!professionId || typeof professionId !== 'string') {
    throw new Error('professionId is required');
  }
  await apiRequest('/v1/settings/profile', {
    method: 'PATCH',
    body: JSON.stringify({ values: { professionId } }),
  });
}

export async function updateUserProfile(updates: Partial<UserProfile>): Promise<UserProfile> {
  const profileValues: ApiRecord = {};
  if (updates.displayName) profileValues.displayName = updates.displayName;
  if (updates.email) profileValues.email = updates.email;
  if (updates.profession) profileValues.profession = updates.profession.toLowerCase();
  if (Object.keys(profileValues).length > 0) {
    await apiRequest('/v1/settings/profile', { method: 'PATCH', body: JSON.stringify({ values: profileValues }) });
  }

  const goalValues: ApiRecord = {};
  if (updates.profession) goalValues.professionId = updates.profession.toLowerCase();
  if (updates.examFamilyCode) goalValues.examFamilyCode = updates.examFamilyCode;
  if (updates.examDate !== undefined) goalValues.targetExamDate = updates.examDate;
  if (updates.studyHoursPerWeek !== undefined) goalValues.studyHoursPerWeek = updates.studyHoursPerWeek;
  if (updates.targetCountry !== undefined) goalValues.targetCountry = updates.targetCountry;
  if (updates.targetScores) {
    goalValues.targetWritingScore = updates.targetScores.Writing;
    goalValues.targetSpeakingScore = updates.targetScores.Speaking;
    goalValues.targetReadingScore = updates.targetScores.Reading;
    goalValues.targetListeningScore = updates.targetScores.Listening;
  }
  if (updates.previousAttempts !== undefined) goalValues.previousAttempts = updates.previousAttempts;
  if (updates.weakSubTests) goalValues.weakSubtests = updates.weakSubTests.map((value) => value.toLowerCase());
  if (updates.targetExamMode !== undefined) goalValues.targetExamMode = updates.targetExamMode;
  if (updates.confidenceLevel !== undefined) goalValues.confidenceLevel = updates.confidenceLevel;

  if (Object.keys(goalValues).length > 0) {
    await apiRequest('/v1/learner/goals', { method: 'PATCH', body: JSON.stringify(goalValues) });
  }

  return fetchUserProfile();
}

export async function fetchStudyPlan(): Promise<StudyPlanTask[]> {
  const plan = await apiRequest<ApiRecord>('/v1/study-plan');
  return (plan.items ?? []).map((item: ApiRecord) => ({
    id: item.itemId,
    title: item.title,
    subTest: toSubTest(item.subtest),
    duration: minutesToLabel(item.durationMinutes),
    rationale: item.rationale,
    dueDate: item.dueDate,
    status: item.status,
    section: item.section,
    contentId: item.contentId ?? undefined,
    type: item.itemType ?? undefined,
    route: typeof item.route === 'string' ? normalizeAppRoute(item.route) : undefined,
  }));
}

export interface StudyPlanTaskUpdate extends Partial<StudyPlanTask> {
  feedbackRating?: number;
  actualMinutesSpent?: number;
}

export async function updateStudyPlanTask(taskId: string, updates: StudyPlanTaskUpdate): Promise<StudyPlanTask> {
  let result: ApiRecord;
  if (updates.status === 'completed') {
    const body: Record<string, unknown> = {};
    if (updates.feedbackRating !== undefined) body.feedbackRating = updates.feedbackRating;
    if (updates.actualMinutesSpent !== undefined) body.actualMinutesSpent = updates.actualMinutesSpent;
    result = await apiRequest(`/v1/study-plan/items/${taskId}/complete`, {
      method: 'POST',
      body: Object.keys(body).length > 0 ? JSON.stringify(body) : undefined,
    });
  } else if (updates.status === 'not_started') {
    result = await apiRequest(`/v1/study-plan/items/${taskId}/reset`, { method: 'POST' });
  } else if (updates.dueDate) {
    result = await apiRequest(`/v1/study-plan/items/${taskId}/reschedule`, { method: 'POST', body: JSON.stringify({ dueDate: updates.dueDate ?? null }) });
  } else {
    result = await apiRequest(`/v1/study-plan/items/${taskId}/skip`, { method: 'POST' });
  }

  return {
    id: result.itemId,
    title: result.title,
    subTest: toSubTest(result.subtest),
    duration: minutesToLabel(result.durationMinutes),
    rationale: result.rationale,
    dueDate: result.dueDate,
    status: result.status,
    section: result.section,
    contentId: result.contentId ?? undefined,
    type: result.itemType ?? undefined,
    route: typeof result.route === 'string' ? result.route : undefined,
  };
}

export interface StudyPlanSwapCandidate {
  contentId: string | null;
  title: string;
  route: string;
  durationMinutes: number;
}

export async function fetchStudyPlanSwapCandidates(taskId: string): Promise<StudyPlanSwapCandidate[]> {
  const result = await apiRequest<ApiRecord>(`/v1/study-plan/items/${taskId}/swap`, {
    method: 'POST',
    body: JSON.stringify({}),
  });
  const candidates = (result.candidates as ApiRecord[] | undefined) ?? [];
  return candidates.map((c) => ({
    contentId: (c.contentId as string | null) ?? null,
    title: String(c.title ?? ''),
    route: String(c.route ?? ''),
    durationMinutes: Number(c.durationMinutes ?? 0),
  }));
}

export async function applyStudyPlanSwap(taskId: string, replacementContentId: string): Promise<StudyPlanTask> {
  const result = await apiRequest<ApiRecord>(`/v1/study-plan/items/${taskId}/swap`, {
    method: 'POST',
    body: JSON.stringify({ replacementContentId }),
  });
  return {
    id: result.itemId,
    title: result.title,
    subTest: toSubTest(result.subtest),
    duration: minutesToLabel(result.durationMinutes),
    rationale: result.rationale,
    dueDate: result.dueDate,
    status: result.status,
    section: result.section,
    contentId: result.contentId ?? undefined,
    type: result.itemType ?? undefined,
    route: typeof result.route === 'string' ? result.route : undefined,
  };
}

// `fetchWritingTask` is retained: it is still used by `submitWritingTask` below
// to resolve the task title after a submit. The V1 `fetchWritingTasks` (list),
// `fetchWritingChecklist`, and `submitWritingDraft` were removed with the
// retired /writing/library and /writing/player surfaces.
export async function fetchWritingTask(taskId: string): Promise<WritingTask> {
  const item = await apiRequest<ApiRecord>(`/v1/writing/tasks/${taskId}`);
  return mapWritingTask(item);
}

export type WritingExamMode = 'computer' | 'paper';
export type WritingAssessorType = 'ai' | 'instructor';

export interface WritingSubmitOptions {
  examMode?: WritingExamMode;
  assessorType?: WritingAssessorType;
  paperAssetIds?: string[];
  turnaroundOption?: 'standard' | 'express';
  focusAreas?: string[];
  learnerNotes?: string;
}

export async function submitWritingTask(taskId: string, content: string, mode: WritingAttemptMode = 'exam', options: WritingSubmitOptions = {}): Promise<WritingSubmission & { attemptId?: string; reviewRequestId?: string; assessorType?: WritingAssessorType; examMode?: WritingExamMode }> {
  const attempt = await ensureAttempt('writing', taskId, mode);
  const examMode = options.examMode ?? 'computer';
  const assessorType = options.assessorType ?? 'ai';

  if (examMode === 'computer') {
    await apiRequest(`/v1/writing/attempts/${attempt.attemptId}/draft`, {
      method: 'PATCH',
      body: JSON.stringify({ content, scratchpad: null, checklist: null, draftVersion: attempt.draftVersion ?? 1 }),
    });
  }

  const submitted = await apiRequest<ApiRecord>(`/v1/writing/attempts/${attempt.attemptId}/submit`, {
    method: 'POST',
    body: JSON.stringify({
      content: examMode === 'computer' ? content : null,
      idempotencyKey: crypto.randomUUID?.() ?? String(Date.now()),
      examMode,
      assessorType,
      paperAssetIds: options.paperAssetIds ?? [],
      turnaroundOption: options.turnaroundOption ?? 'standard',
      focusAreas: options.focusAreas ?? ['OET writing criteria', 'voice-note feedback'],
      learnerNotes: options.learnerNotes ?? null,
    }),
  });

  cacheRemove(attemptCacheKey('writing', taskId, mode));
  if (submitted.evaluationId) {
    cacheSet(evaluationCacheKey('writing', taskId), submitted.evaluationId);
  }
  const task = await fetchWritingTask(taskId);
  const id = String(submitted.evaluationId ?? submitted.reviewRequestId ?? attempt.attemptId);

  return {
    id,
    attemptId: String(submitted.attemptId ?? attempt.attemptId),
    reviewRequestId: submitted.reviewRequestId ? String(submitted.reviewRequestId) : undefined,
    assessorType,
    examMode,
    taskId,
    taskTitle: task.title,
    content,
    submittedAt: new Date().toISOString(),
    evalStatus: submitted.evaluationId ? toEvalStatus(submitted.state) : 'queued',
    reviewStatus: submitted.reviewRequestId ? 'pending' : 'not_requested',
  };
}

export async function attachWritingPaperAssets(taskId: string, mediaAssetIds: string[], mode: WritingAttemptMode = 'exam', replaceExisting = true): Promise<{ attemptId: string; assets: WritingPaperAsset[]; extractionState: string; extractedText: string; extractedCharCount: number; wordCount: number }> {
  const attempt = await ensureAttempt('writing', taskId, mode);
  return apiRequest(`/v1/writing/attempts/${encodeURIComponent(attempt.attemptId)}/paper-assets`, {
    method: 'POST',
    body: JSON.stringify({ mediaAssetIds, replaceExisting }),
  });
}

export async function fetchWritingPaperAssets(taskId: string, mode: WritingAttemptMode = 'exam'): Promise<{ attemptId: string; assets: WritingPaperAsset[]; extractionState: string; extractedText: string }> {
  const attempt = await ensureAttempt('writing', taskId, mode);
  return apiRequest(`/v1/writing/attempts/${encodeURIComponent(attempt.attemptId)}/paper-assets`);
}

export interface WritingEntitlement {
  allowed: boolean;
  tier: string;
  remaining: number | null;
  limitPerWindow: number | null;
  windowDays: number;
  resetAt: string | null;
  reason: string;
}

export async function fetchWritingEntitlement(): Promise<WritingEntitlement> {
  return apiRequest<WritingEntitlement>('/v1/writing/entitlement');
}

export async function fetchWritingResult(resultId: string): Promise<WritingResult> {
  const [summary, feedback] = await Promise.all([
    apiRequest<ApiRecord>(`/v1/writing/evaluations/${resultId}/summary`),
    apiRequest<ApiRecord>(`/v1/writing/evaluations/${resultId}/feedback`),
  ]);

  const criteria = mapCriterionFeedback(feedback.criterionScores ?? [], feedback.feedbackItems ?? []);

  return {
    id: resultId,
    taskId: summary.taskId,
    taskTitle: summary.taskTitle,
    profession: typeof summary.profession === 'string' ? summary.profession : 'medicine',
    examFamilyCode: toExamFamilyCode(summary.examFamilyCode),
    examFamilyLabel: summary.examFamilyLabel ?? titleCase(summary.examFamilyCode ?? 'oet'),
    estimatedScoreRange: scoreRangeDisplay(summary.scoreRange),
    estimatedGradeRange: scoreRangeDisplay(summary.gradeRange ?? 'Pending'),
    confidenceBand: toConfidence(summary.confidenceBand),
    confidenceLabel: summary.confidenceLabel ?? `${toConfidence(summary.confidenceBand)} confidence practice estimate`,
    learnerDisclaimer: summary.learnerDisclaimer ?? `Practice estimate only. This is not an official ${summary.examFamilyLabel ?? 'exam'} score.`,
    methodLabel: summary.methodLabel ?? 'AI-assisted practice evaluation',
    provenanceLabel: summary.provenanceLabel ?? `${summary.examFamilyLabel ?? 'Exam'} practice estimate`,
    humanReviewRecommended: Boolean(summary.humanReviewRecommended),
    escalationRecommended: Boolean(summary.escalationRecommended),
    isOfficialScore: Boolean(summary.isOfficialScore),
    topStrengths: summary.strengths ?? [],
    topIssues: summary.issues ?? [],
    criteria,
    submittedAt: summary.generatedAt ?? new Date().toISOString(),
    evalStatus: toEvalStatus(summary.state),
  };
}

export async function fetchWritingSubmissions(): Promise<WritingSubmission[]> {
  const response = await apiRequest<{ items: ApiRecord[] }>('/v1/submissions');
  return response.items
    .filter((item) => String(item.subtest).toLowerCase() === 'writing')
    .map((item) => ({
      id: item.evaluationId ?? item.submissionId,
      taskId: item.contentId,
      taskTitle: item.taskName,
      content: '',
      submittedAt: item.attemptDate,
      evalStatus: item.evaluationId ? 'completed' : 'processing',
      scoreEstimate: scoreRangeDisplay(item.scoreEstimate),
      reviewStatus: toReviewStatus(item.reviewStatus),
    }));
}

export async function fetchCriteriaDeltas(): Promise<CriteriaDelta[]> {
  const writingSubmissions = await fetchWritingSubmissions();
  const latest = writingSubmissions[0]?.id;
  if (!latest) {
    return [];
  }
  const result = await fetchWritingResult(latest);
  return result.criteria.map((criterion) => ({
    name: criterion.name,
    original: Math.max(criterion.score - 1, 0),
    revised: criterion.score,
    max: criterion.maxScore,
  }));
}

export async function fetchModelAnswer(taskId: string): Promise<ModelAnswer> {
  const response = await apiRequest<ApiRecord>(`/v1/writing/content/${taskId}/model-answer`);
  const payload = response.payload ?? {};
  return {
    taskId,
    taskTitle: response.title,
    profession: titleCase(response.professionId),
    paragraphs: (payload.paragraphs ?? []).map((paragraph: ApiRecord, index: number) => ({
      id: paragraph.id ?? `p-${index + 1}`,
      text: paragraph.text ?? '',
      rationale: paragraph.rationale ?? '',
      criteria: paragraph.criteria ?? [],
      included: paragraph.included ?? [],
      excluded: paragraph.excluded ?? [],
      languageNotes: paragraph.languageNotes ?? '',
    })),
  };
}

export async function fetchSpeakingTasks(): Promise<SpeakingTask[]> {
  const items = await apiRequest<ApiRecord[]>('/v1/speaking/tasks');
  return items.map(mapSpeakingTask);
}

// Wave 3 of docs/SPEAKING-MODULE-PLAN.md - Speaking mock-set helpers.
// These are intentionally typed as ApiRecord-shaped objects so the
// orchestrator UI can stay loose while the backend contract stabilises;
// strict types will land alongside the admin authoring UI in Wave 3b.
export interface SpeakingMockSetSummary {
  mockSetId: string;
  title: string;
  description: string;
  difficulty: string;
  criteriaFocus: string[];
  tags: string[];
  rolePlay1ContentId: string;
  rolePlay2ContentId: string;
  publishedAt: string | null;
}

export interface SpeakingMockSetEntitlement {
  cap: number;
  used: number;
  remaining: number;
  windowDays: number;
  windowStartsAt: string;
}

export interface SpeakingMockSessionRolePlay {
  attemptId: string;
  contentId: string;
  title: string;
  scenarioType: string | null;
  state: string;
  evaluationId: string | null;
  evaluationState: string | null;
  estimatedScaledScore: number | null;
  readinessBand: string;
  readinessBandLabel: string;
}

export interface SpeakingMockSession {
  mockSessionId: string;
  mockSetId: string;
  title: string;
  description: string;
  mode: 'exam' | 'self';
  state: 'inprogress' | 'completed' | 'abandoned';
  startedAt: string;
  completedAt: string | null;
  criteriaFocus: string[];
  tags: string[];
  rolePlay1: SpeakingMockSessionRolePlay;
  rolePlay2: SpeakingMockSessionRolePlay;
  combined: {
    bothCompleted: boolean;
    estimatedScaledScore: number | null;
    passThreshold: number;
    readinessBand: string;
    readinessBandLabel: string;
  };
}

function mapSpeakingMockSession(json: ApiRecord): SpeakingMockSession {
  const role = (key: string): SpeakingMockSessionRolePlay => {
    const rec = asRecord(json[key]);
    return {
      attemptId: typeof rec.attemptId === 'string' ? rec.attemptId : '',
      contentId: typeof rec.contentId === 'string' ? rec.contentId : '',
      title: typeof rec.title === 'string' ? rec.title : '',
      scenarioType: typeof rec.scenarioType === 'string' ? rec.scenarioType : null,
      state: typeof rec.state === 'string' ? rec.state : 'inprogress',
      evaluationId: typeof rec.evaluationId === 'string' ? rec.evaluationId : null,
      evaluationState: typeof rec.evaluationState === 'string' ? rec.evaluationState : null,
      estimatedScaledScore: typeof rec.estimatedScaledScore === 'number' ? rec.estimatedScaledScore : null,
      readinessBand: typeof rec.readinessBand === 'string' ? rec.readinessBand : 'not_ready',
      readinessBandLabel: typeof rec.readinessBandLabel === 'string' ? rec.readinessBandLabel : 'Not ready',
    };
  };
  const combined = asRecord(json.combined);
  return {
    mockSessionId: typeof json.mockSessionId === 'string' ? json.mockSessionId : '',
    mockSetId: typeof json.mockSetId === 'string' ? json.mockSetId : '',
    title: typeof json.title === 'string' ? json.title : '',
    description: typeof json.description === 'string' ? json.description : '',
    mode: json.mode === 'self' ? 'self' : 'exam',
    state: (json.state === 'completed' || json.state === 'abandoned') ? json.state : 'inprogress',
    startedAt: typeof json.startedAt === 'string' ? json.startedAt : new Date().toISOString(),
    completedAt: typeof json.completedAt === 'string' ? json.completedAt : null,
    criteriaFocus: toStringArray(json.criteriaFocus),
    tags: toStringArray(json.tags),
    rolePlay1: role('rolePlay1'),
    rolePlay2: role('rolePlay2'),
    combined: {
      bothCompleted: combined.bothCompleted === true,
      estimatedScaledScore: typeof combined.estimatedScaledScore === 'number' ? combined.estimatedScaledScore : null,
      passThreshold: typeof combined.passThreshold === 'number' ? combined.passThreshold : 350,
      readinessBand: typeof combined.readinessBand === 'string' ? combined.readinessBand : 'not_ready',
      readinessBandLabel: typeof combined.readinessBandLabel === 'string' ? combined.readinessBandLabel : 'Not ready',
    },
  };
}

export async function fetchSpeakingMockSets(): Promise<{ mockSets: SpeakingMockSetSummary[]; entitlement: SpeakingMockSetEntitlement }> {
  const json = await apiRequest<ApiRecord>('/v1/speaking/mock-sets');
  const list = Array.isArray(json.mockSets) ? json.mockSets.map(asRecord) : [];
  const ent = asRecord(json.entitlement);
  return {
    mockSets: list.map((rec): SpeakingMockSetSummary => ({
      mockSetId: typeof rec.mockSetId === 'string' ? rec.mockSetId : '',
      title: typeof rec.title === 'string' ? rec.title : '',
      description: typeof rec.description === 'string' ? rec.description : '',
      difficulty: typeof rec.difficulty === 'string' ? rec.difficulty : 'core',
      criteriaFocus: toStringArray(rec.criteriaFocus),
      tags: toStringArray(rec.tags),
      rolePlay1ContentId: typeof rec.rolePlay1ContentId === 'string' ? rec.rolePlay1ContentId : '',
      rolePlay2ContentId: typeof rec.rolePlay2ContentId === 'string' ? rec.rolePlay2ContentId : '',
      publishedAt: typeof rec.publishedAt === 'string' ? rec.publishedAt : null,
    })),
    entitlement: {
      cap: typeof ent.cap === 'number' ? ent.cap : 1,
      used: typeof ent.used === 'number' ? ent.used : 0,
      remaining: typeof ent.remaining === 'number' ? ent.remaining : 1,
      windowDays: typeof ent.windowDays === 'number' ? ent.windowDays : 7,
      windowStartsAt: typeof ent.windowStartsAt === 'string' ? ent.windowStartsAt : new Date().toISOString(),
    },
  };
}

export async function startSpeakingMockSet(mockSetId: string, mode: 'exam' | 'self' = 'exam'): Promise<SpeakingMockSession> {
  const json = await apiRequest<ApiRecord>(`/v1/speaking/mock-sets/${mockSetId}/start`, {
    method: 'POST',
    body: JSON.stringify({ mode }),
  });
  return mapSpeakingMockSession(json);
}

export async function fetchSpeakingMockSession(sessionId: string): Promise<SpeakingMockSession> {
  const json = await apiRequest<ApiRecord>(`/v1/speaking/mock-sessions/${sessionId}`);
  return mapSpeakingMockSession(json);
}

export async function startSpeakingMockBridge(sessionId: string): Promise<SpeakingMockSession> {
  const json = await apiRequest<ApiRecord>(`/v1/speaking/mock-sessions/${sessionId}/bridge/start`, {
    method: 'POST',
    body: '{}',
  });
  return mapSpeakingMockSession(json);
}

export async function finishSpeakingMockBridge(sessionId: string): Promise<SpeakingMockSession> {
  const json = await apiRequest<ApiRecord>(`/v1/speaking/mock-sessions/${sessionId}/bridge/finish`, {
    method: 'POST',
    body: '{}',
  });
  return mapSpeakingMockSession(json);
}

export interface SpeakingComplianceCopy {
  consentText: string;
  scoreDisclaimer: string;
  audioRetentionDays: number;
}

export async function fetchSpeakingCompliance(): Promise<SpeakingComplianceCopy> {
  const json = await apiRequest<ApiRecord>('/v1/speaking/compliance');
  return {
    consentText: typeof json.consentText === 'string' ? json.consentText : 'I consent to this speaking recording being stored and processed for feedback.',
    scoreDisclaimer: typeof json.scoreDisclaimer === 'string' ? json.scoreDisclaimer : 'Estimated score only. This is not an official OET score or result.',
    audioRetentionDays: typeof json.audioRetentionDays === 'number' ? json.audioRetentionDays : 365,
  };
}

function mapRoleCardPayload(item: ApiRecord): RoleCard {
  const candidateCard = asRecord(item.candidateCard);
  const tasks = toStringArray(candidateCard.tasks).length > 0
    ? toStringArray(candidateCard.tasks)
    : toStringArray(item.tasks);
  const criteriaFocus = toStringArray(item.criteriaFocus ?? item.criteriaFocusTags);
  return {
    id: String(item.contentId ?? item.id ?? ''),
    title: String(item.title ?? 'Speaking role play'),
    profession: item.profession ? titleCase(item.profession) : titleCase(item.professionId),
    setting: String(candidateCard.setting ?? item.setting ?? 'Clinical setting'),
    patient: String(candidateCard.patient ?? candidateCard.patientRole ?? item.patient ?? 'Patient'),
    brief: String(candidateCard.brief ?? candidateCard.task ?? item.brief ?? item.task ?? item.caseNotes ?? ''),
    tasks,
    background: String(candidateCard.background ?? item.background ?? item.caseNotes ?? ''),
    candidateCard: {
      role: typeof candidateCard.role === 'string' ? candidateCard.role : undefined,
      candidateRole: typeof candidateCard.candidateRole === 'string' ? candidateCard.candidateRole : undefined,
      setting: typeof candidateCard.setting === 'string' ? candidateCard.setting : undefined,
      patient: typeof candidateCard.patient === 'string' ? candidateCard.patient : undefined,
      patientRole: typeof candidateCard.patientRole === 'string' ? candidateCard.patientRole : undefined,
      brief: typeof candidateCard.brief === 'string' ? candidateCard.brief : undefined,
      task: typeof candidateCard.task === 'string' ? candidateCard.task : undefined,
      background: typeof candidateCard.background === 'string' ? candidateCard.background : undefined,
      tasks,
    },
    warmUpQuestions: toStringArray(item.warmUpQuestions),
    prepTimeSeconds: typeof item.prepTimeSeconds === 'number' ? item.prepTimeSeconds : undefined,
    roleplayTimeSeconds: typeof item.roleplayTimeSeconds === 'number' ? item.roleplayTimeSeconds : undefined,
    patientEmotion: typeof item.patientEmotion === 'string' ? item.patientEmotion : undefined,
    communicationGoal: typeof item.communicationGoal === 'string' ? item.communicationGoal : undefined,
    clinicalTopic: typeof item.clinicalTopic === 'string' ? item.clinicalTopic : undefined,
    criteriaFocus,
    disclaimer: typeof item.disclaimer === 'string' ? item.disclaimer : undefined,
    sourceAttribution: typeof item.sourceAttribution === 'string' && item.sourceAttribution.trim()
      ? item.sourceAttribution
      : undefined,
  };
}

export async function fetchRoleCard(taskId: string): Promise<RoleCard> {
  const item = await apiRequest<ApiRecord>(`/v1/speaking/tasks/${taskId}`);
  return mapRoleCardPayload(item);
}

export async function fetchSpeakingResult(resultId: string): Promise<SpeakingResult> {
  const summary = await apiRequest<ApiRecord>(`/v1/speaking/evaluations/${resultId}/summary`);
  // Wave 1 of docs/SPEAKING-MODULE-PLAN.md: pass through criterion-keyed
  // feedback + readiness band so the results page can render the new card
  // without re-deriving the projection in the client.
  const rawCriteria = Array.isArray(summary.criteria)
    ? (summary.criteria as ApiRecord[])
    : Array.isArray(summary.criterionScores)
      ? (summary.criterionScores as ApiRecord[])
      : [];
  const criteria = rawCriteria
    .map((entry) => {
      const family = entry.family === 'clinical' ? 'clinical' : 'linguistic';
      const score = typeof entry.score === 'number' ? entry.score : Number(entry.score ?? 0);
      const max = typeof entry.max === 'number' ? entry.max : Number(entry.max ?? (family === 'clinical' ? 3 : 6));
      return {
        criterionCode: String(entry.criterionCode ?? ''),
        family,
        score: Number.isFinite(score) ? score : 0,
        max: Number.isFinite(max) ? max : (family === 'clinical' ? 3 : 6),
        scoreRange: typeof entry.scoreRange === 'string' ? entry.scoreRange : undefined,
        descriptor: typeof entry.descriptor === 'string' ? entry.descriptor : undefined,
        confidenceBand: typeof entry.confidenceBand === 'string' ? entry.confidenceBand : undefined,
        source: entry.source === 'ai_grounded' || entry.source === 'rulebook_fallback' ? entry.source : undefined,
        linkedRuleIds: Array.isArray(entry.linkedRuleIds) ? entry.linkedRuleIds.map(String) : [],
        explanation: typeof entry.explanation === 'string' ? entry.explanation : undefined,
      } as SpeakingResult['criteria'] extends (infer U)[] | undefined ? U : never;
    })
    .filter((entry) => entry.criterionCode.length > 0);

  const readinessBand = (() => {
    const code = summary.readinessBand;
    if (code === 'not_ready' || code === 'developing' || code === 'borderline' || code === 'exam_ready' || code === 'strong') {
      return code;
    }
    return undefined;
  })();

  return {
    id: resultId,
    taskId: summary.taskId,
    taskTitle: summary.taskTitle,
    examFamilyCode: toExamFamilyCode(summary.examFamilyCode),
    examFamilyLabel: summary.examFamilyLabel ?? titleCase(summary.examFamilyCode ?? 'oet'),
    scoreRange: scoreRangeDisplay(summary.scoreRange),
    confidence: toConfidence(summary.confidenceBand),
    confidenceLabel: summary.confidenceLabel ?? `${toConfidence(summary.confidenceBand)} confidence practice estimate`,
    learnerDisclaimer: summary.learnerDisclaimer ?? summary.disclaimer ?? `Practice estimate only. This is not an official ${summary.examFamilyLabel ?? 'exam'} score.`,
    methodLabel: summary.methodLabel ?? 'AI-assisted speaking evaluation',
    provenanceLabel: summary.provenanceLabel ?? `${summary.examFamilyLabel ?? 'Exam'} practice estimate`,
    humanReviewRecommended: Boolean(summary.humanReviewRecommended),
    escalationRecommended: Boolean(summary.escalationRecommended),
    isOfficialScore: Boolean(summary.isOfficialScore),
    strengths: summary.strengths ?? [],
    improvements: summary.issues ?? [],
    evalStatus: toEvalStatus(summary.state),
    submittedAt: summary.generatedAt ?? new Date().toISOString(),
    nextDrill: summary.nextDrill ?? undefined,
    recommendedDrills: Array.isArray(summary.recommendedDrills)
      ? summary.recommendedDrills.map((drill: ApiRecord) => ({
        id: String(drill.id ?? drill.route ?? drill.title ?? 'drill'),
        title: String(drill.title ?? 'Speaking drill'),
        description: String(drill.description ?? ''),
        route: typeof drill.route === 'string' ? drill.route : undefined,
      }))
      : undefined,
    criteria: criteria.length > 0 ? criteria : undefined,
    criteriaSource: summary.criteriaSource === 'ai_grounded' || summary.criteriaSource === 'rulebook_fallback' ? summary.criteriaSource : undefined,
    readinessBand,
    readinessBandLabel: typeof summary.readinessBandLabel === 'string' ? summary.readinessBandLabel : undefined,
    estimatedScaledScore: typeof summary.estimatedScaledScore === 'number' ? summary.estimatedScaledScore : undefined,
    passThreshold: typeof summary.passThreshold === 'number' ? summary.passThreshold : undefined,
    rubricMax: typeof summary.rubricMax === 'number' ? summary.rubricMax : undefined,
    statusReasonCode: typeof summary.statusReasonCode === 'string' ? summary.statusReasonCode : undefined,
    statusMessage: typeof summary.statusMessage === 'string' ? summary.statusMessage : undefined,
    retryable: typeof summary.retryable === 'boolean' ? summary.retryable : undefined,
    retryAfterMs: typeof summary.retryAfterMs === 'number' ? summary.retryAfterMs : undefined,
    timing: summary.timing
      ? {
        prepTimeSeconds: typeof summary.timing.prepTimeSeconds === 'number' ? summary.timing.prepTimeSeconds : undefined,
        roleplayTimeSeconds: typeof summary.timing.roleplayTimeSeconds === 'number' ? summary.timing.roleplayTimeSeconds : undefined,
        recordedSeconds: typeof summary.timing.recordedSeconds === 'number' ? summary.timing.recordedSeconds : undefined,
      }
      : undefined,
  };
}

export async function fetchTranscript(resultId: string): Promise<SpeakingTranscriptReview> {
  const review = await apiRequest<ApiRecord>(`/v1/speaking/evaluations/${resultId}/review`);
  const transcript = (review.transcript ?? []).map((line: ApiRecord) => ({
    id: line.id,
    speaker: titleCase(line.speaker),
    text: line.text,
    startTime: line.startTime ?? 0,
    endTime: line.endTime ?? 0,
    markers: (line.markers ?? []).map((marker: ApiRecord) => ({
      id: marker.id,
      type: marker.type,
      startTime: marker.startTime,
      endTime: marker.endTime,
      text: marker.text,
      suggestion: marker.suggestion,
    })),
  }));

  return {
    title: review.summary?.taskTitle ?? 'Speaking Transcript',
    date: review.summary?.generatedAt ? new Date(review.summary.generatedAt).toISOString().slice(0, 10) : new Date().toISOString().slice(0, 10),
    duration: transcript[transcript.length - 1]?.endTime ?? 0,
    transcript,
    audioAvailable: Boolean(review.audioAvailable),
    audioUrl: review.audioUrl ?? undefined,
    waveformPeaks: normalizeWaveformPeaks(review.analysis?.waveformPeaks),
    disclaimer: typeof review.disclaimer === 'string'
      ? review.disclaimer
      : typeof review.summary?.learnerDisclaimer === 'string'
        ? review.summary.learnerDisclaimer
        : undefined,
    roleCard: review.roleCard ? mapRoleCardPayload(asRecord(review.roleCard)) : undefined,
  };
}

export async function fetchPhrasingData(resultId: string): Promise<{ title: string; segments: PhrasingSegment[]; disclaimer?: string; recommendedDrills?: SpeakingResult['recommendedDrills'] }> {
  const review = await apiRequest<ApiRecord>(`/v1/speaking/evaluations/${resultId}/review`);
  return {
    title: review.summary?.taskTitle ?? 'Speaking Review',
    segments: (review.analysis?.phrasing ?? []).map((segment: ApiRecord) => ({
      id: segment.id,
      originalPhrase: segment.originalPhrase,
      issueExplanation: segment.issueExplanation,
      strongerAlternative: segment.strongerAlternative,
      drillPrompt: segment.drillPrompt,
    })),
    disclaimer: typeof review.disclaimer === 'string'
      ? review.disclaimer
      : typeof review.summary?.learnerDisclaimer === 'string'
        ? review.summary.learnerDisclaimer
        : undefined,
    recommendedDrills: Array.isArray(review.summary?.recommendedDrills)
      ? review.summary.recommendedDrills.map((drill: ApiRecord) => ({
        id: String(drill.id ?? drill.route ?? drill.title ?? 'drill'),
        title: String(drill.title ?? 'Speaking drill'),
        description: String(drill.description ?? ''),
        route: typeof drill.route === 'string' ? drill.route : undefined,
      }))
      : undefined,
  };
}

export async function submitSpeakingRecording(
  taskId: string,
  recording: Blob,
  durationSeconds = 120,
  mode: 'self' | 'exam' | 'practice' | 'diagnostic' = 'self',
  consent?: { accepted: boolean; text?: string },
  options?: { attemptId?: string; mockSessionId?: string; fileName?: string; captureMethod?: string; contentType?: string },
): Promise<{ uploadUrl: string; submissionId: string }> {
  const boundAttemptId = options?.attemptId?.trim();
  const attempt = boundAttemptId ? { attemptId: boundAttemptId } : await ensureAttempt('speaking', taskId, mode);
  const bindingQuery = new URLSearchParams({ contentId: taskId });
  if (options?.mockSessionId) bindingQuery.set('mockSessionId', options.mockSessionId);
  const bindingSuffix = `?${bindingQuery.toString()}`;
  const upload = await apiRequest<ApiRecord>(`/v1/speaking/attempts/${encodeURIComponent(attempt.attemptId)}/audio/upload-session${bindingSuffix}`, { method: 'POST' });
  await uploadBinary(upload.uploadUrl, recording);
  await apiRequest(`/v1/speaking/attempts/${encodeURIComponent(attempt.attemptId)}/audio/complete${bindingSuffix}`, {
    method: 'POST',
    body: JSON.stringify({
      uploadSessionId: upload.uploadSessionId,
      storageKey: upload.storageKey,
      fileName: options?.fileName ?? `${taskId}.webm`,
      sizeBytes: recording.size,
      durationSeconds,
      captureMethod: options?.captureMethod ?? 'browser-recording',
      contentType: options?.contentType ?? (recording.type || 'audio/webm'),
      consentAccepted: consent?.accepted === true,
      consentText: consent?.text,
      consentAcceptedAt: new Date().toISOString(),
    }),
  });
  const submitted = await apiRequest<ApiRecord>(`/v1/speaking/attempts/${encodeURIComponent(attempt.attemptId)}/submit${bindingSuffix}`, { method: 'POST' });
  const evaluationId = typeof submitted.evaluationId === 'string' ? submitted.evaluationId : '';
  if (!evaluationId) {
    throw new Error('Speaking evaluation was not queued. Please try again.');
  }

  if (!boundAttemptId) {
    cacheRemove(attemptCacheKey('speaking', taskId, mode));
  }
  cacheSet(evaluationCacheKey('speaking', taskId), evaluationId);
  return { uploadUrl: upload.uploadUrl, submissionId: evaluationId };
}

export async function fetchReadingTask(taskId: string): Promise<ReadingTask> {
  throw new Error(`Legacy Reading task ${taskId} is closed. Use structured Reading papers from /reading.`);
}

export async function submitReadingAnswers(taskId: string, answers: Record<string, string>): Promise<ReadingResult> {
  void answers;
  throw new Error(`Legacy Reading task ${taskId} is closed. Submit structured Reading attempts from /reading.`);
}

export async function fetchReadingResult(taskId: string): Promise<ReadingResult> {
  throw new Error(`Legacy Reading result ${taskId} is closed. Review structured Reading attempts from /reading.`);
}

export async function fetchListeningTask(taskId: string): Promise<ListeningTask> {
  const task = await apiRequest<ApiRecord>(`/v1/listening/tasks/${taskId}`);
  return {
    id: task.contentId,
    title: task.title,
    audioSrc: task.audioUrl ?? '',
    duration: task.durationSeconds ?? task.estimatedDurationMinutes * 60,
    audioAvailable: Boolean(task.audioUrl),
    audioUnavailableReason: task.audioUrl ? undefined : 'Audio for this listening task is not available yet. Please use transcript-backed review instead.',
    transcriptPolicy: task.transcriptPolicy ?? 'per_item_post_attempt',
    questions: (task.questions ?? []).map((question: ApiRecord) => ({
      id: question.id,
      number: question.number,
      text: question.text,
      type: question.type,
      options: question.options ?? undefined,
      correctAnswer: question.correctAnswer ?? '',
    })),
  };
}

export async function submitListeningAnswers(taskId: string, answers: Record<string, string>): Promise<ListeningResult> {
  const attempt = await ensureAttempt('listening', taskId, 'exam');
  await apiRequest(`/v1/listening/attempts/${attempt.attemptId}/answers`, { method: 'PATCH', body: JSON.stringify({ answers }) });
  const submitted = await apiRequest<ApiRecord>(`/v1/listening/attempts/${attempt.attemptId}/submit`, { method: 'POST' });
  cacheRemove(attemptCacheKey('listening', taskId, 'exam'));
  cacheSet(evaluationCacheKey('listening', taskId), submitted.evaluationId);
  return fetchListeningResult(taskId);
}

export async function fetchListeningResult(taskId: string): Promise<ListeningResult> {
  const evaluationId = await latestEvaluationIdForContent(taskId, 'listening');
  if (!evaluationId) {
    throw new Error('Listening result not found');
  }

  const [evaluation, task] = await Promise.all([
    apiRequest<ApiRecord>(`/v1/listening/evaluations/${evaluationId}`),
    apiRequest<ApiRecord>(`/v1/listening/tasks/${taskId}`),
  ]);

  const questions = (evaluation.itemReview ?? []).map((itemReview: ApiRecord, index: number) => {
    const transcript = itemReview.transcript ?? null;
    const isCorrect = Boolean(itemReview.isCorrect);
    return {
      id: `lrq-${index + 1}`,
      number: itemReview.number ?? index + 1,
      text: itemReview.prompt ?? itemReview.text ?? `Question ${index + 1}`,
      userAnswer: itemReview.learnerAnswer ?? '',
      correctAnswer: itemReview.correctAnswer ?? '',
      isCorrect,
      isInvalid: itemReview.isInvalid === true,
      explanation: itemReview.explanation ?? (isCorrect ? 'Correct.' : 'Review the transcript clue and distractor pattern.'),
      allowTranscriptReveal: Boolean(transcript?.allowed),
      transcriptExcerpt: transcript?.excerpt ?? undefined,
      distractorExplanation: transcript?.distractorExplanation ?? itemReview.distractorExplanation ?? undefined,
    };
  });

  const rawScore = Number(evaluation.rawScore ?? questions.filter((question: ListeningResult['questions'][number]) => question.isCorrect).length);
  const maxRawScore = Number(evaluation.maxRawScore ?? 42);
  const hasRecommendedDrill = evaluation.recommendedNextDrill && typeof evaluation.recommendedNextDrill === 'object';

  return {
    id: taskId,
    title: task.title,
    score: rawScore,
    total: maxRawScore,
    questions,
    invalidCount: Number(evaluation.invalidCount ?? questions.filter((question: { isInvalid?: boolean }) => question.isInvalid === true).length),
    recommendedDrill: hasRecommendedDrill
      ? {
          id: (evaluation.recommendedNextDrill as ApiRecord).drillId ?? (evaluation.recommendedNextDrill as ApiRecord).id ?? 'listening-drill-detail_capture',
          title: (evaluation.recommendedNextDrill as ApiRecord).title ?? 'Exact Detail Capture Drill',
          description: (evaluation.recommendedNextDrill as ApiRecord).description ?? (evaluation.recommendedNextDrill as ApiRecord).rationale ?? 'Practise the listening error type that appeared most often in this result.',
        }
      : null,
  };
}

export async function fetchListeningDrill(drillId: string): Promise<ListeningDrill> {
  const drill = normalizeRouteValues(await apiRequest<ApiRecord>(`/v1/listening-papers/drills/${drillId}`));
  return {
    id: drill.drillId,
    title: drill.title,
    focusLabel: drill.focusLabel,
    description: drill.description,
    errorType: drill.errorType,
    estimatedMinutes: Number(drill.estimatedMinutes ?? 10),
    highlights: drill.highlights ?? [],
    launchRoute: drill.launchRoute,
    reviewRoute: drill.reviewRoute,
  };
}

export async function fetchListeningReview(taskId: string): Promise<ListeningReview> {
  const evaluationId = await latestEvaluationIdForContent(taskId, 'listening');
  if (!evaluationId) {
    throw new Error('Complete a listening task before opening transcript-backed review.');
  }

  const evaluation = await apiRequest<ApiRecord>(`/v1/listening/evaluations/${evaluationId}`);
  return {
    id: taskId,
    title: evaluation.title ?? 'Listening transcript-backed review',
    transcriptPolicy: evaluation.transcriptAccess?.policy ?? 'per_item_post_attempt',
    recommendedDrill: undefined,
    questions: (evaluation.itemReview ?? []).map((item: ApiRecord, index: number) => ({
      id: item.questionId ?? `listening-review-${index + 1}`,
      number: Number(item.number ?? index + 1),
      text: item.prompt ?? '',
      learnerAnswer: item.learnerAnswer ?? '',
      correctAnswer: item.correctAnswer ?? '',
      explanation: item.explanation ?? '',
      transcriptExcerpt: item.transcript?.excerpt ?? undefined,
      distractorExplanation: item.transcript?.distractorExplanation ?? undefined,
    })),
  };
}

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

function mapReadinessResponse(readiness: ApiRecord): ReadinessData {
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

export async function fetchReadiness(): Promise<ReadinessData> {
  const readiness = await apiRequest<ApiRecord>('/v1/readiness');
  return mapReadinessResponse(readiness);
}

export async function fetchReadinessHistory(weeks = 12): Promise<ReadinessHistoryPoint[]> {
  const rows = await apiRequest<ApiRecord[]>(`/v1/readiness/history?weeks=${weeks}`);
  return (Array.isArray(rows) ? rows : []).map((row) => ({
    weekStartDate: row.weekStartDate,
    overall: Number(row.overall ?? 0),
    writing: Number(row.writing ?? 0),
    speaking: Number(row.speaking ?? 0),
    reading: Number(row.reading ?? 0),
    listening: Number(row.listening ?? 0),
    vocabulary: Number(row.vocabulary ?? 0),
    risk: row.risk ?? 'Unknown',
    targetDateProbability: typeof row.targetDateProbability === 'number' ? row.targetDateProbability : null,
  }));
}

export async function fetchReadinessBlockers(): Promise<ReadinessBlocker[]> {
  const blockers = await apiRequest<ApiRecord[]>('/v1/readiness/blockers');
  return (Array.isArray(blockers) ? blockers : []).map((b) => ({
    id: b.id,
    title: b.title,
    description: b.description,
    actionLabel: typeof b.actionLabel === 'string' ? b.actionLabel : undefined,
    actionHref: typeof b.actionHref === 'string' ? b.actionHref : undefined,
    impactScore: typeof b.impactScore === 'number' ? b.impactScore : undefined,
    severity: (typeof b.severity === 'string' ? b.severity : undefined) as 'high' | 'medium' | 'low' | undefined,
  }));
}

export async function fetchReadinessForecast(hoursPerWeek?: number): Promise<ReadinessForecast> {
  const url = hoursPerWeek != null ? `/v1/readiness/forecast?hoursPerWeek=${hoursPerWeek}` : '/v1/readiness/forecast';
  const result = await apiRequest<ApiRecord>(url);
  return {
    probability: Number(result.probability ?? 0),
    weeksNeeded: Number(result.weeksNeeded ?? 0),
    weeksAvailable: Number(result.weeksAvailable ?? 0),
    requiredImprovement: Number(result.requiredImprovement ?? 0),
    slopePerWeek: Number(result.slopePerWeek ?? 0),
    scenarios: asArray(result.scenarios).map((s: ApiRecord) => ({
      label: String(s.label ?? ''),
      hoursPerWeek: Number(s.hoursPerWeek ?? 0),
      projectedReadinessAtTarget: Number(s.projectedReadinessAtTarget ?? 0),
      probability: Number(s.probability ?? 0),
    })),
  };
}

export async function refreshReadiness(): Promise<ReadinessData> {
  const readiness = await apiRequest<ApiRecord>('/v1/readiness/refresh', { method: 'POST' });
  return mapReadinessResponse(readiness);
}

export interface AdminReadinessLearnerRow {
  userId: string;
  displayName: string;
  targetExamDate: string | null;
  overallReadiness: number;
  overallRisk: string;
  weakestSubtest: string | null;
  targetDateProbability: number | null;
  computedAt: string;
  expiresAt: string;
}

export interface AdminReadinessLearnerList {
  page: number;
  pageSize: number;
  total: number;
  items: AdminReadinessLearnerRow[];
}

export interface AdminReadinessMetrics {
  learnersWithSnapshot: number;
  highRisk: number;
  moderateRisk: number;
  lowRisk: number;
  unknownRisk: number;
  interventionCandidates: number;
  staleSnapshots: number;
  avgWriting: number;
  avgSpeaking: number;
  avgReading: number;
  avgListening: number;
  avgVocabulary: number;
  avgOverall: number;
  generatedAt: string;
}

export async function fetchAdminReadinessLearners(params: { risk?: string; page?: number; pageSize?: number } = {}): Promise<AdminReadinessLearnerList> {
  const qs = new URLSearchParams();
  if (params.risk) qs.set('risk', params.risk);
  if (params.page) qs.set('page', String(params.page));
  if (params.pageSize) qs.set('pageSize', String(params.pageSize));
  const url = `/v1/admin/readiness/learners${qs.toString() ? `?${qs}` : ''}`;
  return apiRequest<AdminReadinessLearnerList>(url);
}

export async function fetchAdminReadinessLearner(userId: string): Promise<{ userId: string; displayName: string | null; targetExamDate: string | null; snapshot: ApiRecord; history: ReadinessHistoryPoint[]; reasoningTrace: string }> {
  const result = await apiRequest<ApiRecord>(`/v1/admin/readiness/learners/${encodeURIComponent(userId)}`);
  return {
    userId: result.userId,
    displayName: typeof result.displayName === 'string' ? result.displayName : null,
    targetExamDate: typeof result.targetExamDate === 'string' ? result.targetExamDate : null,
    snapshot: result.snapshot as ApiRecord,
    history: Array.isArray(result.history) ? result.history.map((row: ApiRecord) => ({
      weekStartDate: row.weekStartDate,
      overall: Number(row.overall ?? 0),
      writing: Number(row.writing ?? 0),
      speaking: Number(row.speaking ?? 0),
      reading: Number(row.reading ?? 0),
      listening: Number(row.listening ?? 0),
      vocabulary: Number(row.vocabulary ?? 0),
      risk: row.risk ?? 'Unknown',
      targetDateProbability: typeof row.targetDateProbability === 'number' ? row.targetDateProbability : null,
    })) : [],
    reasoningTrace: typeof result.reasoningTrace === 'string' ? result.reasoningTrace : '',
  };
}

export async function recomputeAdminReadiness(userId: string): Promise<void> {
  await apiRequest(`/v1/admin/readiness/learners/${encodeURIComponent(userId)}/recompute`, { method: 'POST' });
}

export async function fetchAdminReadinessMetrics(): Promise<AdminReadinessMetrics> {
  return apiRequest<AdminReadinessMetrics>('/v1/admin/readiness/metrics');
}

export async function fetchTrendData(): Promise<TrendPoint[]> {
  const progress = await apiRequest<ApiRecord>('/v1/progress');
  const grouped = new Map<string, TrendPoint>();
  for (const point of progress.trend ?? []) {
    const label = point.week ?? new Date(point.generatedAt).toLocaleDateString();
    const existing: TrendPoint = grouped.get(label) ?? { date: label };
    existing[String(point.subtest).toLowerCase()] = parseScoreValue(point.scoreRange);
    grouped.set(label, existing);
  }
  return Array.from(grouped.values());
}

export async function fetchCompletionData(): Promise<{ day: string; completed: number }[]> {
  const progress = await apiRequest<ApiRecord>('/v1/progress');
  return progress.completion ?? [];
}

export async function fetchSubmissionVolume(): Promise<{ week: string; submissions: number }[]> {
  const progress = await apiRequest<ApiRecord>('/v1/progress');
  return progress.submissionVolume ?? [];
}

export async function fetchProgressEvidenceSummary(): Promise<ProgressEvidenceSummary> {
  const progress = await apiRequest<ApiRecord>('/v1/progress');
  return {
    reviewUsage: {
      totalRequests: Number(progress.reviewUsage?.totalRequests ?? 0),
      completedRequests: Number(progress.reviewUsage?.completedRequests ?? 0),
      averageTurnaroundHours: progress.reviewUsage?.averageTurnaroundHours ?? null,
      creditsConsumed: Number(progress.reviewUsage?.creditsConsumed ?? 0),
    },
    freshness: {
      generatedAt: progress.freshness?.generatedAt ?? new Date().toISOString(),
      usesFallbackSeries: Boolean(progress.freshness?.usesFallbackSeries),
    },
  };
}

export async function fetchSubmissions(): Promise<Submission[]> {
  // Follow the cursor until exhausted so callers that expect the full history
  // still get it, while the wire protocol uses the real cursor pagination
  // contract documented in the learner blueprint.
  const collected: ApiRecord[] = [];
  let cursor: string | null = null;
  // Hard safety cap to prevent runaway loops if the server mis-behaves.
  for (let page = 0; page < 50; page += 1) {
    const query: string = cursor ? `?cursor=${encodeURIComponent(cursor)}&limit=100` : '?limit=100';
    const response: { items: ApiRecord[]; nextCursor?: string | null } = await apiRequest<{
      items: ApiRecord[];
      nextCursor?: string | null;
    }>(`/v1/submissions${query}`);
    collected.push(...(response.items ?? []));
    const next: string | null = response.nextCursor ?? null;
    if (!next) break;
    cursor = next;
  }
  return collected.map((item) => ({
    id: item.submissionId,
    contentId: item.contentId,
    taskName: item.taskName,
    subTest: toSubTest(item.subtest),
    attemptDate: item.attemptDate,
    scoreEstimate: scoreRangeDisplay(item.scoreEstimate ?? ''),
    reviewStatus: toReviewStatus(item.reviewStatus),
    reviewRequestId: item.reviewRequestId ?? null,
    evaluationId: item.evaluationId ?? undefined,
    state: item.state ?? undefined,
    submissionMode: item.submissionMode ?? undefined,
    assessorType: item.assessorType ?? undefined,
    voiceNoteCount: Number(item.voiceNoteCount ?? 0),
    comparisonGroupId: item.comparisonGroupId ?? null,
    canRequestReview: Boolean(item.canRequestReview),
    actions: {
      reopenFeedbackRoute: item.actions?.reopenFeedbackRoute ?? null,
      compareRoute: item.actions?.compareRoute ?? null,
      requestReviewRoute: item.actions?.requestReviewRoute ?? null,
    },
  }));
}

export async function fetchSubmissionDetail(submissionId: string): Promise<SubmissionDetail> {
  const submissions = await fetchSubmissions();
  const submission = submissions.find((item) => item.id === submissionId || item.evaluationId === submissionId);
  if (!submission) {
    throw new Error('Submission not found.');
  }

  const baseDetail: SubmissionDetail = {
    submission,
    evidenceSummary: {
      title: submission.taskName,
      scoreLabel: submission.scoreEstimate || 'Pending',
      stateLabel: titleCase(submission.state ?? 'completed'),
      reviewLabel: titleCase(submission.reviewStatus.replace(/_/g, ' ')),
      nextActionLabel: submission.canRequestReview ? 'Request review' : 'Review current evidence',
    },
    strengths: [],
    issues: [],
  };

  if (submission.reviewRequestId) {
    try {
      const voice = await fetchLearnerReviewVoiceNotes(submission.reviewRequestId);
      baseDetail.voiceNotes = (voice.items ?? []).map((note) => ({
        id: note.id,
        reviewRequestId: note.reviewRequestId,
        url: note.url,
        fileName: note.fileName,
        mimeType: note.mimeType,
        durationSeconds: note.durationSeconds,
        transcriptText: note.transcriptText,
        writtenNotes: note.writtenNotes,
        createdAt: note.createdAt,
      }));
    } catch (error) {
      console.warn('[API] Failed to load review voice notes:', error);
    }

    if (submission.reviewStatus === 'reviewed') {
      try {
        const result = await fetchLearnerReviewResult(submission.reviewRequestId);
        const criteria = result.criteria.map((criterion) => ({
          name: criterion.name,
          score: criterion.score,
          maxScore: criterion.maxScore,
          grade: '',
          explanation: criterion.explanation || 'Reviewed by Dr. Ahmed.',
          anchoredComments: [],
          omissions: [],
          unnecessaryDetails: [],
          revisionSuggestions: [],
          strengths: [],
          issues: [],
        } satisfies CriterionFeedback));
        baseDetail.expertReview = {
          reviewRequestId: result.reviewRequestId,
          finalComment: result.finalComment,
          scoreLabel: result.scoreLabel,
          completedAt: result.completedAt,
          criteria,
        };
        baseDetail.criteria = criteria;
        baseDetail.evidenceSummary.scoreLabel = result.scoreLabel || baseDetail.evidenceSummary.scoreLabel;
      } catch (error) {
        console.warn('[API] Failed to load review result:', error);
      }
    }
  }

  if (!submission.evaluationId) {
    return baseDetail;
  }

  if (submission.subTest === 'Writing') {
    const result = await fetchWritingResult(submission.evaluationId);
    return {
      ...baseDetail,
      strengths: result.topStrengths,
      issues: result.topIssues,
      criteria: result.criteria,
    };
  }

  if (submission.subTest === 'Speaking') {
    const [result, transcript] = await Promise.all([
      fetchSpeakingResult(submission.evaluationId),
      fetchTranscript(submission.evaluationId),
    ]);
    return {
      ...baseDetail,
      strengths: result.strengths,
      issues: result.improvements,
      transcript: transcript.transcript,
    };
  }

  if (submission.subTest === 'Reading') {
    const result = await fetchReadingResult(submission.contentId);
    return {
      ...baseDetail,
      strengths: [`${result.score}/${result.totalQuestions} questions answered correctly.`],
      issues: result.errorClusters.filter((cluster) => cluster.count > 0).map((cluster) => `${cluster.type}: ${cluster.count} items to review.`),
      questionReview: result.items.map((item) => ({
        id: item.id,
        number: item.number,
        text: item.text,
        learnerAnswer: item.userAnswer,
        correctAnswer: item.correctAnswer,
        isCorrect: item.isCorrect,
        explanation: item.explanation,
      })),
    };
  }

  const result = await fetchListeningResult(submission.contentId);
  return {
    ...baseDetail,
    strengths: [`${result.score}/${result.total} listening items captured correctly.`],
    issues: result.questions.filter((question) => !question.isCorrect && !question.isInvalid).map((question) => question.distractorExplanation ?? question.explanation),
    questionReview: result.questions.map((question) => ({
      id: question.id,
      number: question.number,
      text: question.text,
      learnerAnswer: question.userAnswer,
      correctAnswer: question.correctAnswer,
      isCorrect: question.isCorrect,
      explanation: question.explanation,
      transcriptExcerpt: question.transcriptExcerpt,
      distractorExplanation: question.distractorExplanation,
    })),
  };
}

export async function fetchSubmissionComparison(leftId?: string, rightId?: string): Promise<SubmissionComparison> {
  const params = new URLSearchParams();
  if (leftId) params.set('leftId', leftId);
  if (rightId) params.set('rightId', rightId);
  const response = await apiRequest<ApiRecord>(`/v1/submissions/compare?${params.toString()}`);
  return {
    canCompare: Boolean(response.canCompare),
    reason: response.reason ?? undefined,
    summary: response.summary ?? undefined,
    comparisonGroupId: response.comparisonGroupId ?? null,
    left: response.left
      ? {
          attemptId: response.left.attemptId,
          evaluationId: response.left.evaluationId ?? undefined,
          scoreRange: scoreRangeDisplay(response.left.scoreRange ?? ''),
          subtest: toSubTest(response.left.subtest),
        }
      : undefined,
    right: response.right
      ? {
          attemptId: response.right.attemptId,
          evaluationId: response.right.evaluationId ?? undefined,
          scoreRange: scoreRangeDisplay(response.right.scoreRange ?? ''),
          subtest: toSubTest(response.right.subtest),
        }
      : undefined,
  };
}

export interface PublicBillingPlan {
  planId: string;
  code: string;
  label: string;
  tier: string;
  description: string;
  price: { amount: number; currency: string; interval: string };
  reviewCredits: number;
  mockReportsIncluded: boolean;
  includedSubtests: string[];
  trialDays: number;
  isRenewable: boolean;
  changeDirection: string;
}

export async function fetchPublicPlans(): Promise<{ items: PublicBillingPlan[] }> {
  return apiRequest('/v1/public/plans');
}

export async function fetchBilling(): Promise<BillingData> {
  const [summary, invoices, plans, extras] = await Promise.all([
    apiRequest<ApiRecord>('/v1/billing/summary'),
    apiRequest<ApiRecord>('/v1/billing/invoices'),
    apiRequest<ApiRecord>('/v1/billing/plans'),
    apiRequest<ApiRecord>('/v1/billing/extras'),
  ]);
  const activeAddOns = (summary.activeAddOns ?? []).map((item: ApiRecord) => ({
    id: item.id,
    code: item.code,
    name: item.name,
    productType: item.productType,
    quantity: Number(item.quantity ?? 0),
    price: formatCurrency(item.price?.amount ?? item.price, item.price?.currency ?? item.currency ?? 'AUD'),
    currency: item.price?.currency ?? item.currency ?? 'AUD',
    interval: item.price?.interval ?? item.interval ?? 'one_time',
    status: item.status ?? 'active',
    description: item.description ?? '',
    grantCredits: Number(item.grantCredits ?? 0),
    durationDays: Number(item.durationDays ?? 0),
    isRecurring: Boolean(item.isRecurring),
    appliesToAllPlans: Boolean(item.appliesToAllPlans),
    quantityStep: Number(item.quantityStep ?? 1),
    maxQuantity: item.maxQuantity == null ? null : Number(item.maxQuantity),
    compatiblePlanCodes: toStringArray(item.compatiblePlanCodes),
  }));
  return {
    currentPlan: summary.planName ?? titleCase(summary.planId),
    currentPlanId: summary.planId,
    currentPlanCode: summary.planCode ?? summary.planId,
    planName: summary.planName ?? titleCase(summary.planId),
    planDescription: summary.planDescription ?? '',
    profession: summary.profession ?? 'all',
    price: formatCurrency(summary.price?.amount ?? 0, summary.price?.currency ?? 'GBP'),
    interval: summary.price.interval,
    // Keep the raw status token ("active" | "frozen" | "past_due" | ...). The UI
    // formats it for display via formatSubscriptionStatus; keeping it raw also
    // lets status comparisons (e.g. past_due) match reliably.
    status: summary.status,
    nextRenewal: summary.nextRenewalAt,
    reviewCredits: summary.wallet.creditBalance,
    activeAddOns,
    entitlements: {
      productiveSkillReviewsEnabled: Boolean(summary.entitlements?.productiveSkillReviewsEnabled),
      supportedReviewSubtests: summary.entitlements?.supportedReviewSubtests ?? [],
      invoiceDownloadsAvailable: Boolean(summary.entitlements?.invoiceDownloadsAvailable),
    },
    plans: (plans.items ?? []).map((plan: ApiRecord) => ({
      id: plan.planId ?? plan.code,
      code: plan.code ?? plan.planId,
      label: plan.label,
      tier: titleCase(plan.tier ?? plan.code ?? plan.planId),
      description: plan.description,
      price: formatCurrency(plan.price?.amount, plan.price?.currency),
      interval: plan.price?.interval ?? 'month',
      reviewCredits: Number(plan.reviewCredits ?? 0),
      canChangeTo: Boolean(plan.canChangeTo),
      changeDirection: plan.changeDirection ?? 'current',
      badge: plan.badge ?? '',
      status: plan.status ?? 'active',
      durationMonths: Number(plan.durationMonths ?? 1),
      isVisible: Boolean(plan.isVisible ?? true),
      isRenewable: Boolean(plan.isRenewable ?? true),
      trialDays: Number(plan.trialDays ?? 0),
      displayOrder: Number(plan.displayOrder ?? 0),
      includedSubtests: toStringArray(plan.includedSubtests),
      entitlements: asRecord(plan.entitlements),
    })),
    addOns: (extras.items ?? []).map((extra: ApiRecord) => ({
      id: extra.id,
      code: extra.code ?? extra.id,
      name: extra.name ?? extra.description ?? extra.id,
      productType: extra.productType ?? 'review_credits',
      quantity: Number(extra.quantity ?? 0),
      price: formatCurrency(extra.price?.amount ?? extra.price, extra.price?.currency ?? extra.currency ?? 'AUD'),
      currency: extra.price?.currency ?? extra.currency ?? 'AUD',
      interval: extra.price?.interval ?? extra.interval ?? 'one_time',
      status: extra.status ?? 'active',
      description: extra.description,
      grantCredits: Number(extra.grantCredits ?? 0),
      durationDays: Number(extra.durationDays ?? 0),
      isRecurring: Boolean(extra.isRecurring ?? false),
      appliesToAllPlans: Boolean(extra.appliesToAllPlans ?? true),
      quantityStep: Number(extra.quantityStep ?? 1),
      maxQuantity: extra.maxQuantity == null ? null : Number(extra.maxQuantity),
      compatiblePlanCodes: toStringArray(extra.compatiblePlanCodes),
    })),
    coupons: [],
    quote: null,
    invoices: (invoices.items ?? []).map((invoice: ApiRecord) => ({
      id: invoice.invoiceId ?? invoice.id,
      date: invoice.date,
      amount: formatCurrency(invoice.amount, invoice.currency),
      status: toBillingStatus(invoice.status),
      currency: invoice.currency,
      downloadUrl: invoice.downloadUrl,
      description: invoice.description,
    })),
  };
}

export async function fetchBillingQuote(input: {
  productType: BillingProductType;
  quantity: number;
  priceId?: string | null;
  couponCode?: string | null;
  addOnCodes?: string[];
  parentSubscriptionId?: string | null;
}): Promise<BillingQuote> {
  const params = new URLSearchParams();
  params.set('productType', input.productType);
  params.set('quantity', String(input.quantity));
  if (input.priceId) params.set('priceId', input.priceId);
  if (input.couponCode) params.set('couponCode', input.couponCode);
  if (input.addOnCodes && input.addOnCodes.length > 0) params.set('addOnCodes', input.addOnCodes.join(','));
  if (input.parentSubscriptionId) params.set('parentSubscriptionId', input.parentSubscriptionId);
  const quote = await apiRequest<ApiRecord>(`/v1/billing/quote?${params.toString()}`);
  // The backend reports the plan's delivery method inside the quote's `validation`
  // bag rather than as a top-level field. Prefer a top-level value if one ever
  // appears, so promoting it server-side does not need a change here.
  const validation = asRecord(quote.validation);
  return {
    quoteId: quote.quoteId,
    status: quote.status,
    currency: quote.currency,
    subtotalAmount: Number(quote.subtotalAmount ?? 0),
    discountAmount: Number(quote.discountAmount ?? 0),
    totalAmount: Number(quote.totalAmount ?? 0),
    planCode: quote.planCode ?? null,
    couponCode: quote.couponCode ?? null,
    addOnCodes: toStringArray(quote.addOnCodes),
    items: asArray(quote.items).map((item: ApiRecord) => ({
      kind: String(item.kind ?? 'item'),
      code: String(item.code ?? item.id ?? ''),
      name: String(item.name ?? item.code ?? ''),
      amount: Number(item.amount ?? 0),
      currency: String(item.currency ?? 'AUD'),
      quantity: Number(item.quantity ?? 1),
      description: toNullableString(item.description),
    })),
    expiresAt: quote.expiresAt,
    summary: String(quote.summary ?? ''),
    deliveryMethod: toNullableString(quote.deliveryMethod) ?? toNullableString(validation.deliveryMethod),
    validation,
  };
}

export async function purchaseReviewCredits(count: number): Promise<{ success: boolean; newBalance: number }> {
  const billing = await fetchBilling();
  await apiRequest('/v1/billing/checkout-sessions', {
    method: 'POST',
    body: JSON.stringify({
      productType: 'review_credits',
      quantity: count,
      priceId: null,
      couponCode: null,
      addOnCodes: null,
      quoteId: null,
      idempotencyKey: crypto.randomUUID?.() ?? String(Date.now()),
    }),
  });
  return { success: true, newBalance: billing.reviewCredits + count };
}

export async function fetchBillingChangePreview(targetPlanId: string): Promise<BillingChangePreview> {
  const preview = await apiRequest<ApiRecord>(`/v1/billing/change-preview?targetPlanId=${encodeURIComponent(targetPlanId)}`);
  return {
    currentPlanId: preview.currentPlanId,
    targetPlanId: preview.targetPlanId,
    direction: preview.direction,
    proratedAmount: formatCurrency(preview.proratedAmount),
    effectiveAt: preview.effectiveAt,
    summary: preview.summary,
    currentCreditsIncluded: Number(preview.currentCreditsIncluded ?? 0),
    targetCreditsIncluded: Number(preview.targetCreditsIncluded ?? 0),
  };
}

export async function createBillingCheckoutSession(input: {
  productType: BillingProductType;
  quantity: number;
  priceId?: string | null;
  couponCode?: string | null;
  addOnCodes?: string[];
  parentSubscriptionId?: string | null;
  quoteId?: string | null;
  gateway?: string;
  idempotencyKey?: string;
}): Promise<{ checkoutUrl: string; checkoutSessionId: string; quoteId?: string | null; totalAmount?: number; currency?: string; clientSecret?: string | null; gateway?: string }> {
  const response = await apiRequest<ApiRecord>('/v1/billing/checkout-sessions', {
    method: 'POST',
    body: JSON.stringify({
      productType: input.productType,
      quantity: input.quantity,
      priceId: input.priceId ?? null,
      couponCode: input.couponCode ?? null,
      addOnCodes: input.addOnCodes ?? null,
      parentSubscriptionId: input.parentSubscriptionId ?? null,
      quoteId: input.quoteId ?? null,
      gateway: input.gateway ?? null,
      idempotencyKey: input.idempotencyKey ?? crypto.randomUUID?.() ?? String(Date.now()),
    }),
  });
  return {
    checkoutUrl: response.checkoutUrl,
    checkoutSessionId: response.checkoutSessionId,
    quoteId: response.quoteId ?? null,
    totalAmount: response.totalAmount != null ? Number(response.totalAmount) : undefined,
    currency: response.currency ?? undefined,
    clientSecret: response.clientSecret ?? null,
    gateway: response.gateway ?? undefined,
  };
}

export async function fetchBillingPaymentStatus(input: {
  quoteId?: string | null;
  sessionId?: string | null;
}): Promise<BillingPaymentStatus> {
  const params = new URLSearchParams();
  if (input.quoteId) params.set('quoteId', input.quoteId);
  if (input.sessionId) params.set('sessionId', input.sessionId);
  const response = await apiRequest<ApiRecord>(`/v1/billing/payment-status?${params.toString()}`);
  return {
    status: String(response.status ?? 'pending'),
    quoteId: toNullableString(response.quoteId),
    checkoutSessionId: toNullableString(response.checkoutSessionId),
    productType: toNullableString(response.productType),
    targetPlanId: toNullableString(response.targetPlanId),
    addOnCodes: toStringArray(response.addOnCodes),
    items: asArray(response.items).map((item: ApiRecord) => ({
      kind: String(item.kind ?? 'item'),
      code: String(item.code ?? ''),
      name: String(item.name ?? item.code ?? ''),
      amount: Number(item.amount ?? 0),
      currency: String(item.currency ?? response.currency ?? 'GBP'),
      quantity: Number(item.quantity ?? 1),
      description: toNullableString(item.description),
    })),
    totalAmount: Number(response.totalAmount ?? 0),
    currency: String(response.currency ?? 'GBP'),
    invoiceId: toNullableString(response.invoiceId),
    subscriptionId: toNullableString(response.subscriptionId),
    failureReason: toNullableString(response.failureReason),
    fulfilledAt: toNullableString(response.fulfilledAt),
    expiresAt: toNullableString(response.expiresAt),
    manualDeliveryRequired: response.manualDeliveryRequired === true,
    whatsAppUrl: toNullableString(response.whatsAppUrl),
  };
}

export async function fetchAiPackages(): Promise<AiPackagesResponse> {
  const data = await apiRequest<ApiRecord>('/v1/billing/ai-packages');
  const mapPackage = (p: ApiRecord): AiPackage => ({
    code: String(p.code ?? ''),
    name: String(p.name ?? ''),
    description: String(p.description ?? ''),
    price: Number(p.price ?? 0),
    currency: String(p.currency ?? 'GBP'),
    credits: Number(p.credits ?? 0),
    sharedCredits: Number(p.sharedCredits ?? 0),
    writingCredits: Number(p.writingCredits ?? 0),
    speakingCredits: Number(p.speakingCredits ?? 0),
    mocks: Number(p.mocks ?? 0),
    validityDays: Number(p.validityDays ?? 0),
    priorityQueue: Boolean(p.priorityQueue ?? false),
    group: String(p.group ?? 'full') as AiPackage['group'],
    features: toStringArray(p.features),
  });
  const separate = asRecord(data.separate);
  return {
    currency: String(data.currency ?? 'GBP'),
    full: asArray(data.full).map(mapPackage),
    separate: {
      listening: asArray(separate.listening).map(mapPackage),
      reading: asArray(separate.reading).map(mapPackage),
      writing: asArray(separate.writing).map(mapPackage),
      speaking: asArray(separate.speaking).map(mapPackage),
    },
    mock: asArray(data.mock).map(mapPackage),
  };
}

export async function fetchMyAiPackageCredits(): Promise<AiPackageCreditSnapshot> {
  const data = await apiRequest<ApiRecord>('/v1/me/ai-package-credits');
  return mapAiPackageCreditSnapshot(data);
}

export interface LearnerAttemptHistoryItem {
  attemptId: string;
  subtest: string;
  title: string;
  contentRef?: string | null;
  startedAt: string;
  submittedAt?: string | null;
  status: 'in_progress' | 'completed';
  balanceSource?: string | null;
  creditsUsed: number;
  route: string;
}

/** Unified all-four-subtest activity history (Master Catalogue §2). */
export async function fetchMyAttemptHistory(limit = 100): Promise<LearnerAttemptHistoryItem[]> {
  const data = await apiRequest<ApiRecord>(`/v1/me/attempts?limit=${limit}`);
  return asArray((data as ApiRecord).items).map((item) => ({
    attemptId: String(item.attemptId ?? ''),
    subtest: String(item.subtest ?? ''),
    title: String(item.title ?? ''),
    contentRef: item.contentRef == null ? null : String(item.contentRef),
    startedAt: String(item.startedAt ?? ''),
    submittedAt: item.submittedAt == null ? null : String(item.submittedAt),
    status: item.status === 'completed' ? 'completed' : 'in_progress',
    balanceSource: item.balanceSource == null ? null : String(item.balanceSource),
    creditsUsed: Number(item.creditsUsed ?? 0),
    route: String(item.route ?? '/submissions'),
  }));
}

export async function fetchAdminUserAiCredits(userId: string): Promise<AiPackageCreditSnapshot> {
  const data = await apiRequest<ApiRecord>(
    `/v1/admin/users/${encodeURIComponent(userId)}/ai-credits?pageSize=100`,
  );
  return mapAiPackageCreditSnapshot(data);
}

export interface AiPackageCreditAdjustmentPayload {
  sharedCreditsDelta?: number;
  sharedCreditsSet?: number;
  flexibleCreditsDelta?: number;
  flexibleCreditsSet?: number;
  writingOnlyCreditsDelta?: number;
  writingOnlyCreditsSet?: number;
  speakingOnlyCreditsDelta?: number;
  speakingOnlyCreditsSet?: number;
  listeningTestsDelta?: number;
  listeningTestsSet?: number;
  readingTestsDelta?: number;
  readingTestsSet?: number;
  mockExamsDelta?: number;
  mockExamsSet?: number;
  expiresAt?: string | null;
  reason?: string | null;
}

/** Per-bucket credit adjustment — every change is written to the ledger. */
export async function adjustAdminUserAiCredits(
  userId: string,
  payload: AiPackageCreditAdjustmentPayload,
): Promise<AiPackageCreditSnapshot> {
  const data = await apiRequest<ApiRecord>(
    `/v1/admin/ai-package-credits/${encodeURIComponent(userId)}/adjust`,
    { method: 'POST', body: JSON.stringify(payload) },
  );
  return mapAiPackageCreditSnapshot(data);
}

export async function adjustAdminAiPackageCredits(
  userId: string,
  payload: {
    sharedCreditsDelta?: number;
    sharedCreditsSet?: number;
    writingOnlyCreditsDelta?: number;
    writingOnlyCreditsSet?: number;
    speakingOnlyCreditsDelta?: number;
    speakingOnlyCreditsSet?: number;
    flexibleCreditsDelta?: number;
    flexibleCreditsSet?: number;
    listeningTestsDelta?: number;
    listeningTestsSet?: number;
    readingTestsDelta?: number;
    readingTestsSet?: number;
    mockExamsDelta?: number;
    mockExamsSet?: number;
    reason?: string;
  },
): Promise<AiPackageCreditSnapshot> {
  const data = await apiRequest<ApiRecord>(
    `/v1/admin/ai-package-credits/${encodeURIComponent(userId)}/adjust`,
    {
      method: 'POST',
      body: JSON.stringify({
        flexibleCreditsDelta: payload.flexibleCreditsDelta ?? 0,
        writingOnlyCreditsDelta: payload.writingOnlyCreditsDelta ?? 0,
        speakingOnlyCreditsDelta: payload.speakingOnlyCreditsDelta ?? 0,
        listeningTestsDelta: payload.listeningTestsDelta ?? 0,
        readingTestsDelta: payload.readingTestsDelta ?? 0,
        mockExamsDelta: payload.mockExamsDelta ?? 0,
        sharedCreditsDelta: payload.sharedCreditsDelta ?? 0,
        sharedCreditsSet: payload.sharedCreditsSet ?? null,
        flexibleCreditsSet: payload.flexibleCreditsSet ?? null,
        writingOnlyCreditsSet: payload.writingOnlyCreditsSet ?? null,
        speakingOnlyCreditsSet: payload.speakingOnlyCreditsSet ?? null,
        listeningTestsSet: payload.listeningTestsSet ?? null,
        readingTestsSet: payload.readingTestsSet ?? null,
        mockExamsSet: payload.mockExamsSet ?? null,
        reason: payload.reason ?? 'admin adjustment',
      }),
    },
  );
  return mapAiPackageCreditSnapshot(data);
}

export async function downloadInvoice(invoiceId: string): Promise<string> {
  return fetchAuthorizedObjectUrl(`/v1/billing/invoices/${encodeURIComponent(invoiceId)}/download`);
}

export async function pauseSubscription(days?: number, reason?: string): Promise<object> {
  return apiRequest('/v1/billing/subscription/pause', {
    method: 'POST',
    body: JSON.stringify({ days: days ?? null, reason: reason ?? null }),
  });
}

export async function resumeSubscription(): Promise<object> {
  return apiRequest('/v1/billing/subscription/resume', { method: 'POST' });
}

export interface BankAccountConfigDto {
  id: string;
  region: string;
  currency: string;
  bankName: string;
  accountHolderName: string;
  accountNumber?: string | null;
  routingOrSortCode?: string | null;
  iban?: string | null;
  swiftBic?: string | null;
  instructionsMarkdown?: string | null;
  isActive?: boolean;
}

export async function fetchMyBankAccounts(): Promise<BankAccountConfigDto[]> {
  return apiRequest<BankAccountConfigDto[]>('/v1/billing/bank-accounts/me');
}

export async function fetchTurnaroundOptions(): Promise<TurnaroundOption[]> {
  const response = await apiRequest<ApiRecord>('/v1/billing/review-options');
  return (response.items ?? []).map((item: ApiRecord) => ({
    id: item.id,
    label: item.label,
    time: item.turnaround,
    cost: item.price,
    description: item.description,
  }));
}

export async function fetchFocusAreas(subtest?: string): Promise<FocusArea[]> {
  const query = subtest ? `?subtest=${encodeURIComponent(subtest)}` : '';
  const criteria = await apiRequest<ApiRecord[]>(`/v1/reference/criteria${query}`);
  const unique = new Map<string, FocusArea>();
  for (const criterion of criteria) {
    unique.set(criterion.code, {
      id: criterion.code,
      label: criterion.label,
      description: criterion.description,
    });
  }
  return Array.from(unique.values());
}

export async function submitReviewRequest(request: { submissionId: string; turnaroundId: string; focusAreas: string[]; notes: string; }): Promise<{ reviewId: string; estimatedDelivery: string }> {
  const target = await resolveReviewTarget(request.submissionId);
  const response = await apiRequest<ApiRecord>('/v1/reviews/requests', {
    method: 'POST',
    body: JSON.stringify({
      attemptId: target.attemptId,
      subtest: target.subtest,
      turnaroundOption: request.turnaroundId,
      focusAreas: request.focusAreas,
      learnerNotes: request.notes,
      paymentSource: 'credits',
      idempotencyKey: crypto.randomUUID?.() ?? String(Date.now()),
    }),
  });
  const options = await fetchTurnaroundOptions();
  const option = options.find((item) => item.id === request.turnaroundId);
  return {
    reviewId: response.reviewRequestId,
    estimatedDelivery: option?.time ?? '48-72 hours',
  };
}

export async function fetchDiagnosticSession(): Promise<DiagnosticSession> {
  const overview = await apiRequest<ApiRecord>('/v1/diagnostic/overview');
  return {
    id: overview.diagnosticId,
    status: overview.state,
    startedAt: overview.startedAt,
    completedAt: overview.completedAt,
    subTests: (overview.subtests ?? []).map((item: ApiRecord) => ({
      subTest: toSubTest(item.subtest),
      status: item.state,
      estimatedDuration: minutesToLabel(item.estimatedDurationMinutes),
      completedAt: item.completedAt,
      contentId: item.contentId ?? item.taskId ?? undefined,
    })),
  };
}

export async function startDiagnostic(): Promise<DiagnosticSession> {
  const session = await apiRequest<ApiRecord>('/v1/diagnostic/attempts', { method: 'POST' });
  return {
    id: session.diagnosticId,
    status: session.state,
    startedAt: session.startedAt,
    completedAt: session.completedAt,
    subTests: (session.subtests ?? []).map((item: ApiRecord) => ({
      subTest: toSubTest(item.subtest),
      status: item.state,
      estimatedDuration: minutesToLabel(item.estimatedDurationMinutes),
      completedAt: item.completedAt,
      contentId: item.contentId ?? item.taskId ?? undefined,
    })),
  };
}

/**
 * Fetches the diagnostic task ID for a given sub-test.
 * Fails closed if the backend has not published a real diagnostic task.
 */
export async function fetchDiagnosticTaskId(subTest: SubTest): Promise<string> {
  try {
    const response = await apiRequest<ApiRecord>(`/v1/diagnostic/tasks?subtest=${encodeURIComponent(subTest)}`);
    if (response.diagnosticEligible !== true) {
      throw new Error('Diagnostic task is not marked eligible.');
    }
    const taskId = response.taskId ?? response.contentId ?? null;
    if (taskId) return String(taskId);
  } catch {
    // Fail closed below; learner-facing diagnostics must not use demo IDs.
  }

  throw new Error(`Diagnostic ${subTest} task is unavailable.`);
}

export async function fetchDiagnosticResults(): Promise<DiagnosticResult[]> {
  const session = await fetchDiagnosticSession();
  const response = await apiRequest<ApiRecord>(`/v1/diagnostic/attempts/${session.id}/results`);
  return (response.results ?? []).map((item: ApiRecord) => ({
    subTest: toSubTest(item.subTest),
    scoreRange: scoreRangeDisplay(item.scoreRange),
    confidence: toConfidence(item.confidence),
    strengths: item.strengths ?? [],
    issues: item.issues ?? [],
    readiness: item.readiness ?? 0,
    criterionBreakdown: (item.criterionBreakdown ?? []).map((criterion: ApiRecord) => ({
      name: criterion.name,
      score: criterion.score,
      maxScore: criterion.maxScore,
      grade: criterion.grade,
      explanation: criterion.explanation,
      anchoredComments: criterion.anchoredComments ?? [],
      omissions: criterion.omissions ?? [],
      unnecessaryDetails: criterion.unnecessaryDetails ?? [],
      revisionSuggestions: criterion.revisionSuggestions ?? [],
      strengths: criterion.strengths ?? [],
      issues: criterion.issues ?? [],
    })),
  }));
}

// ─── Expert Console API ───

export async function fetchExpertMe(): Promise<ExpertMe> {
  return apiRequest<ExpertMe>('/v1/expert/me');
}

export async function fetchExpertDashboard(): Promise<ExpertDashboardData> {
  return apiRequest<ExpertDashboardData>('/v1/expert/dashboard');
}

// ── Expert Onboarding ─────────────────────────────────────
export type {
  CalibrationCase,
  CalibrationCaseDetail,
  CalibrationNote,
  ExpertLearnerDirectoryResponse,
  ExpertLearnerReviewContext,
  ExpertMetrics,
  ExpertOnboardingProfile,
  ExpertOnboardingQualifications,
  ExpertOnboardingRates,
  ExpertOnboardingStatus,
  ExpertQueueFilterMetadata,
  ExpertReviewHistory,
  ExpertSchedule,
  LearnerProfileExpanded,
  ReviewDraft,
  ReviewQueueResponse,
  ReviewVoiceNote,
  ScheduleException,
  SpeakingReviewDetail,
  WritingReviewDetail,
} from './types/expert';
export type {
  ExpertAvailabilityConstraints,
  ExpertCalibrationAlignment,
  ExpertCalibrationAlignmentBreakdown,
  ExpertCalibrationAlignmentTrendPoint,
  ExpertCalibrationHistory,
  ExpertCalibrationHistoryEntry,
  ReviewCriterionVoiceNoteResult,
  WritingMarkingVoiceNote,
} from './api/expert';
export {
  addWritingReviewVoiceNote,
  claimReview,
  completeExpertOnboarding,
  createScheduleException,
  deleteScheduleException,
  fetchCalibrationCaseDetail,
  fetchCalibrationCases,
  fetchCalibrationNotes,
  fetchExpertAvailabilityConstraints,
  fetchExpertCalibrationAlignment,
  fetchExpertCalibrationHistory,
  fetchExpertLearnerReviewContext,
  fetchExpertMetrics,
  fetchExpertOnboardingStatus,
  fetchExpertQueueFilterMetadata,
  fetchExpertSchedule,
  fetchLearnerProfile,
  fetchExpertLearners,
  fetchLearnerReviewResult,
  fetchLearnerReviewVoiceNotes,
  fetchReviewQueue,
  fetchScheduleExceptions,
  fetchSpeakingReviewDetail,
  fetchExpertReviewHistory,
  fetchWritingReviewDetail,
  getWritingSubmissionVoiceNote,
  releaseReview,
  requestRework,
  saveCalibrationDraft,
  saveDraftReview,
  saveExpertOnboardingProfile,
  saveExpertOnboardingQualifications,
  saveExpertOnboardingRates,
  saveExpertSchedule,
  submitCalibrationCase,
  submitExpertSpeakingReview,
  submitExpertWritingReview,
  uploadSpeakingReviewCriterionVoiceNote,
  uploadWritingMarkingVoiceNote,
  uploadWritingReviewCriterionVoiceNote,
} from './api/expert';

// ─── Admin / CMS API ───

// ── Admin Alerts ─────────────────────────────────────
// ── Admin Content ─────────────────────────────────────
export type {
  AdminSignupExamTypePayload,
  AdminSignupProfessionPayload,
} from './api/admin-platform';
export {
  archiveAdminContent,
  archiveAdminSignupExamType,
  archiveAdminSignupProfession,
  archiveAdminTaxonomy,
  activateAdminSignupExamType,
  activateAdminSignupProfession,
  createAdminAIConfig,
  createAdminContent,
  createAdminCriterion,
  createAdminFlag,
  createAdminSignupExamType,
  createAdminSignupProfession,
  createAdminTaxonomy,
  exportAdminAuditLogs,
  fetchAdminAIConfig,
  fetchAdminAlerts,
  fetchAdminAuditLogs,
  fetchAdminContent,
  fetchAdminContentDetail,
  fetchAdminContentRevisions,
  fetchAdminCriteria,
  fetchAdminDashboard,
  fetchAdminFlags,
  fetchAdminSignupCatalog,
  fetchAdminTaxonomy,
  forceDeleteAdminSignupExamType,
  forceDeleteAdminSignupProfession,
  forceDeleteAdminTaxonomy,
  publishAdminContent,
  restoreAdminContentRevision,
  updateAdminAIConfig,
  updateAdminContent,
  updateAdminCriterion,
  updateAdminFlag,
  updateAdminSignupExamType,
  updateAdminSignupProfession,
  updateAdminTaxonomy,
} from './api/admin-platform';

export type {
  AdminBillingAddOnOet2026Fields,
  AdminBillingPlanOet2026Fields,
  AdminSponsorDto,
  AdminUserProfileUpdatePayload,
  AdminWalletTierInput,
  AdminWalletTierRow,
  AdminWalletTiersResponse,
} from './api/admin-users';
export {
  adjustAdminUserCredits,
  bulkImportUsers,
  createAdminBillingAddOn,
  createAdminBillingPlan,
  deleteAdminBillingAddOn,
  deleteAdminBillingPlan,
  deleteAdminUser,
  fetchAdminBillingAddOnVersions,
  fetchAdminBillingAddOns,
  fetchAdminBillingPlanVersions,
  fetchAdminBillingPlans,
  fetchAdminSponsors,
  fetchAdminUserDetail,
  fetchAdminUsers,
  fetchAdminWalletTiers,
  hardDeleteAdminUser,
  inviteAdminUser,
  replaceAdminWalletTiers,
  resendAdminUserInvite,
  restoreAdminUser,
  revokeAdminUserSessions,
  setAdminUserPassword,
  triggerAdminUserPasswordReset,
  unlockAdminUser,
  updateAdminBillingAddOn,
  updateAdminBillingPlan,
  updateAdminUserProfile,
  updateAdminUserStatus,
  verifyAdminUserEmail,
} from './api/admin-users';

// ── Hard-delete (404 + 409 handled by caller). Server returns 409 when
// the plan/add-on still has historical references — caller should fall
// back to archive (PUT status=archived) in that case.

// ── Billing page copy (admin-editable learner-page strings) ──────────────
export type { AdminBillingContentEntry } from './api/billing-content';
export {
  deleteAdminBillingContentEntry,
  fetchAdminBillingContent,
  fetchBillingContent,
  replaceAdminBillingContent,
} from './api/billing-content';

// ── OET 2026 catalog API ─────────────────────────────────────────────────
export type {
  AdminCatalogPresentationResponse,
  MyEntitlementSnapshot,
  Oet2026ReseedResponse,
} from './api/catalog';
export {
  fetchAdminCatalogPresentation,
  fetchEligibilityMatrix,
  fetchMyEntitlementSnapshot,
  fetchPublicCatalog,
  quoteAddonEligibility,
  reseedOet2026Catalog,
  saveAdminCatalogPresentation,
} from './api/catalog';

// ── Tutor Book API ───────────────────────────────────────────────────────
export type {
  AdminTutorBookAudioScript,
  AdminTutorBookUpdate,
  TutorBookAudioScript,
  TutorBookUpdate,
  TutorBookWhatsAppResponse,
} from './api/tutor-book';
export {
  adminDeleteTutorBookAudioScript,
  adminDeleteTutorBookUpdate,
  adminListTutorBookAudioScripts,
  adminListTutorBookUpdates,
  adminUpsertTutorBookAudioScript,
  adminUpsertTutorBookUpdate,
  fetchTutorBookAudioScripts,
  fetchTutorBookUpdates,
  fetchTutorBookWhatsApp,
  tutorBookDownloadUrl,
} from './api/tutor-book';

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

// ── Subscription lifecycle (admin manual actions) ──
export {
  activateAdminAIConfig,
  activateAdminFlag,
  adminApproveSubscriptionFreeze,
  adminCancelSubscription,
  adminChangeSubscriptionPlan,
  adminCreateSubscription,
  adminExtendSubscription,
  adminFreezeSubscription,
  adminReactivateSubscription,
  adminRejectSubscriptionFreeze,
  adminResumeSubscription,
  adminSetSubscriptionStatus,
  approveAdminFreeze,
  assignAdminReview,
  bulkAdminContentAction,
  cancelAdminReview,
  cancelFreeze,
  confirmFreeze,
  createAdminManualFreeze,
  deactivateAdminFlag,
  deleteAdminAIConfig,
  endAdminFreeze,
  fetchAdminAuditLogDetail,
  fetchAdminBillingCouponRedemptions,
  fetchAdminBillingEntitlementDiagnostics,
  fetchAdminBillingInvoiceEvidence,
  fetchAdminBillingInvoices,
  fetchAdminBillingPaymentTransactions,
  fetchAdminBillingProviderLifecycleSignals,
  fetchAdminCohortAnalysis,
  fetchAdminContentEffectiveness,
  fetchAdminContentImpact,
  fetchAdminExpertEfficiency,
  fetchAdminFreezeOverview,
  fetchAdminQualityAnalytics,
  fetchAdminReviewFailures,
  fetchAdminReviewOpsQueue,
  fetchAdminReviewOpsSummary,
  fetchAdminSubscriptionHealth,
  fetchAdminTaxonomyImpact,
  fetchFreezeStatus,
  forceEndAdminFreeze,
  reopenAdminReview,
  rejectAdminFreeze,
  requestFreeze,
  updateAdminFreezePolicy,
} from './api/admin-operations';

// ── Gamification ─────────────────────────────────────────────────────────────
export type { LearnerFeatureFlag } from './api/gamification';
export {
  fetchAchievements,
  fetchLeaderboard,
  fetchLearnerFeatureFlag,
  fetchMyLeaderboardPosition,
  fetchStreak,
  fetchXP,
  recordActivity,
  setLeaderboardOptIn,
} from './api/gamification';

// ── Spaced Repetition ─────────────────────────────────────────────────────────
export {
  createReviewItem,
  deleteReviewItem,
  fetchDueReviewItems,
  fetchReviewSummary,
  submitReview,
} from './api/spaced-repetition';

// ── Recalls (unified vocabulary + spaced-repetition) ─────────────────────────
// See docs/RECALLS-MODULE-PLAN.md.
export type {
  RecallsBulkUploadResult,
  RecallsBulkUploadRow,
  RecallsLibraryItem,
  RecallsQueueItem,
  RecallsRevisionPlanResponse,
  RecallsStarReason,
  RecallsTodayResponse,
  RecallsWeeklyReport,
} from './api/recalls';
export {
  adminBulkUploadRecalls,
  fetchRecallsAudio,
  fetchRecallsLibrary,
  fetchRecallsQueue,
  fetchRecallsRevisionPlan,
  fetchRecallsToday,
  fetchRecallsWeeklyReport,
  starRecall,
} from './api/recalls';

// ── Vocabulary ────────────────────────────────────────────────────────────────
export type {
  MyVocabularyPageRequest,
  RecallSetSummary,
  RecallSetsResponse,
} from './api/vocabulary';
export {
  addToMyVocabulary,
  fetchDueFlashcards,
  fetchMyVocabulary,
  fetchVocabQuiz,
  fetchVocabularyCategories,
  fetchVocabularyDailySet,
  fetchVocabularyQuizHistory,
  fetchVocabularyRecallSets,
  fetchVocabularyStats,
  fetchVocabularyTerm,
  fetchVocabularyTerms,
  lookupVocabularyTerm,
  removeFromMyVocabulary,
  submitFlashcardReview,
  submitVocabQuiz,
} from './api/vocabulary';

// ── Content Hierarchy: Program Browser (Phase 8) ──
// ── Content Browser (access-aware) ──
export {
  fetchContentAccess,
  fetchContentBrowser,
  fetchContentPackage,
  fetchContentPackages,
  fetchContentProgram,
  fetchContentPrograms,
  fetchContentTracks,
  fetchFoundationResources,
  fetchFreePreviewAssets,
  fetchProgramsBrowser,
} from './api/content-browser';

// ── Phase 6: Readiness & Skill-based Content ──
// ── Phase 9: Search & Recommendations ──
// ── Phase 11: Media Access ──
// ── Media Management ──
export type { UploadedMediaAsset } from './api/content-discovery';
export {
  deleteMedia,
  fetchContentBySkill,
  fetchMediaItem,
  fetchMyMedia,
  fetchReadinessScore,
  fetchRecommendations,
  fetchSearchFacets,
  fetchSignedMediaUrl,
  searchContent,
  uploadMedia,
} from './api/content-discovery';
// uploadMedia is also used by voice-note submitters below in this file.

// ── Admin: Content Hierarchy Management ──
export {
  adminAssembleMockExam,
  adminBulkImportContent,
  adminDedupScan,
  adminDesignateCanonical,
  adminGenerateDiagnostic,
  adminProcessMediaAsset,
  adminUpdateContentEligibility,
  createAdminLesson,
  createAdminModule,
  createAdminPackage,
  createAdminProgram,
  createAdminTrack,
  fetchAdminContentInventory,
  fetchAdminContentPackages,
  fetchAdminContentPrograms,
  fetchAdminDedupGroups,
  fetchAdminLessons,
  fetchAdminMediaAssets,
  fetchAdminMediaAudit,
  fetchAdminModules,
  fetchAdminPackage,
  fetchAdminProgram,
  fetchAdminTracks,
  updateAdminLesson,
  updateAdminModule,
  updateAdminPackage,
  updateAdminProgram,
  updateAdminTrack,
} from './api/admin-content';

// ── Admin: Vocabulary Management ──────────────────────────────────────

// Wave 3 of docs/SPEAKING-MODULE-PLAN.md - admin CRUD for speaking
// mock sets. Permissions reuse AdminContent* on the backend.
export type { AdminSpeakingMockSetRow } from './api/speaking-mock-sets';
export {
  archiveAdminSpeakingMockSet,
  createAdminSpeakingMockSet,
  fetchAdminSpeakingContentOptions,
  fetchAdminSpeakingMockSet,
  fetchAdminSpeakingMockSets,
  forceDeleteAdminSpeakingMockSet,
  publishAdminSpeakingMockSet,
  updateAdminSpeakingMockSet,
} from './api/speaking-mock-sets';

// ── Wave 4 of docs/SPEAKING-MODULE-PLAN.md - tutor calibration drift +
// inline transcript comments. Three audiences:
//   • Admin: CRUD over calibration samples, drift report.
//   • Expert/tutor: list samples, submit rubric, post inline comments.
//   • Learner: read inline comments on their attempt.
export type {
  AdminSpeakingCalibrationDriftReport,
  AdminSpeakingCalibrationDriftRow,
  AdminSpeakingCalibrationSampleRow,
  SpeakingCriterionRubric,
  TutorCalibrationSubmissionResult,
  TutorSpeakingCalibrationSampleRow,
} from './api/speaking-calibration';
export {
  archiveAdminSpeakingCalibrationSample,
  createAdminSpeakingCalibrationSample,
  fetchAdminSpeakingCalibrationDrift,
  fetchAdminSpeakingCalibrationSamples,
  fetchTutorSpeakingCalibrationSamples,
  publishAdminSpeakingCalibrationSample,
  submitTutorSpeakingCalibrationScores,
} from './api/speaking-calibration';

// ── Inline transcript comments ──
export type {
  SpeakingDrillRow,
  SpeakingDrillsListResponse,
  SpeakingSelfPracticeStartResult,
  SpeakingTranscriptComment,
} from './api/speaking-practice';
export {
  fetchSpeakingDrills,
  fetchSpeakingTranscriptComments,
  postExpertSpeakingTranscriptComment,
  startSpeakingSelfPracticeSession,
} from './api/speaking-practice';

// ── Admin: Vocabulary Management ──
export type {
  AdminRecallSetSummary,
  AdminVocabularyAudioGenerateResponse,
  AdminVocabularyAudioProgress,
  AdminVocabularyBulkActivateResponse,
  AdminVocabularyBulkArchiveResponse,
  AdminVocabularyBulkDeleteResponse,
  AdminVocabularyBulkDraftResponse,
  AdminVocabularyBulkPreviewResponse,
} from './api/admin-vocabulary';
export {
  acceptAdminVocabularyAiDrafts,
  backfillAdminVocabularyAudio,
  bulkActivateAdminVocabularyItems,
  bulkArchiveAdminVocabularyItems,
  bulkDraftAdminVocabularyItems,
  bulkImportAdminVocabulary,
  cancelAdminVocabularyImportAudio,
  createAdminVocabularyItem,
  deleteAdminVocabularyItem,
  deleteAdminVocabularyItems,
  exportAdminVocabularyImportBatchCsv,
  fetchAdminVocabularyAudioProgress,
  fetchAdminVocabularyCategories,
  fetchAdminVocabularyImportBatch,
  fetchAdminVocabularyItem,
  fetchAdminVocabularyItems,
  fetchAdminVocabularyRecallSets,
  generateAdminVocabularyAudio,
  previewAdminVocabularyImport,
  reconcileAdminVocabularyImportBatch,
  requestAdminVocabularyAiDraft,
  resumeAdminVocabularyAudio,
  rollbackAdminVocabularyImportBatch,
  setAdminVocabularyFreePreview,
  updateAdminVocabularyItem,
} from './api/admin-vocabulary';

// ── Adaptive Difficulty ───────────────────────────────────────────────────────
// ── Predictions ────────────────────────────────────────────────────────────────
// ── Community ─────────────────────────────────────────────────────────────────
// ── Community Moderation (Admin) ──────────────────────────────────────────────
export {
  adminDeleteCommunityReply,
  adminDeleteCommunityThread,
  createForumThread,
  createReply,
  createStudyGroup,
  fetchAdminCommunityThreads,
  fetchAdaptiveContent,
  fetchForumCategories,
  fetchForumThread,
  fetchForumThreads,
  fetchPrediction,
  fetchPredictions,
  fetchSkillProfile,
  fetchStudyGroups,
  fetchThreadReplies,
  joinStudyGroup,
  lockCommunityThread,
  pinCommunityThread,
  requestPredictionComputation,
} from './api/community';

// ── Grammar ───────────────────────────────────────────────────────────────────
export type {
  AdminGrammarLessonFull,
  AdminGrammarLessonRow,
  AdminGrammarTopic,
  GrammarAttemptResult,
  GrammarContentBlockLearner,
  GrammarExerciseAuthoring,
  GrammarExerciseLearner,
  GrammarLessonDocument,
  GrammarLessonProgress,
  GrammarLessonSummary,
  GrammarLessonUpsertPayload,
  GrammarRecommendation,
  GrammarTopicLearner,
  GrammarTopicUpsertPayload,
} from './grammar/types';
export type { GrammarEntitlement } from './api/grammar';
export {
  adminArchiveGrammarLessonV2,
  adminArchiveGrammarTopic,
  adminCreateGrammarLessonV2,
  adminCreateGrammarTopic,
  adminEvaluateGrammarPublishGate,
  adminFetchGrammarPublishGate,
  adminFetchGrammarStats,
  adminForceDeleteGrammarLessonV2,
  adminGenerateGrammarAiDraft,
  adminGenerateWritingAiDraft,
  adminGetGrammarLessonV2,
  adminListGrammarLessonsV2,
  adminListGrammarTopics,
  adminPublishGrammarLesson,
  adminPublishGrammarLessonV2,
  adminUnpublishGrammarLesson,
  adminUnpublishGrammarLessonV2,
  adminUpdateGrammarLessonV2,
  adminUpdateGrammarTopic,
  completeGrammarLesson,
  dismissGrammarRecommendation,
  fetchGrammarEntitlement,
  fetchGrammarLesson,
  fetchGrammarLessons,
  fetchGrammarOverview,
  fetchGrammarTopicDetail,
  startGrammarLesson,
  submitGrammarAttempt,
} from './api/grammar';

// ── Writing Options (admin: AI kill-switch + entitlement) ──
// ── Writing Rule-Violation Analytics (admin: P22 dashboard) ──
// ── Video Lessons ─────────────────────────────────────────────────────────────
// Retired 2026-07: the legacy /v1/lessons feature was superseded by the Video
// Library (`lib/api/videos.ts`, /v1/video-library). Old endpoints return 410.

// ── Strategy Guides ───────────────────────────────────────────────────────────
export type {
  AdminWritingAttemptViolations,
  AdminWritingLetterTypeCount,
  AdminWritingOptions,
  AdminWritingProfessionCount,
  AdminWritingRuleViolationDashboard,
  AdminWritingRuleViolationGroup,
  AdminWritingRuleViolationRow,
  AdminWritingRuleViolationSummary,
} from './api/strategies';
export {
  adminArchiveStrategyGuide,
  adminCreateStrategyGuide,
  adminForceDeleteStrategyGuide,
  adminGetStrategyGuide,
  adminGetWritingAttemptViolations,
  adminGetWritingOptions,
  adminGetWritingRuleViolationDashboard,
  adminListStrategyGuides,
  adminPublishStrategyGuide,
  adminUpdateStrategyGuide,
  adminUpdateWritingOptions,
  adminValidateStrategyGuidePublish,
  fetchStrategyGuide,
  fetchStrategyGuides,
  setStrategyGuideBookmark,
  updateStrategyGuideProgress,
} from './api/strategies';

// ── Pronunciation ─────────────────────────────────────────────────────────────
// All pronunciation endpoints are protected by the backend's LearnerOnly policy
// and the `pronunciation_analysis` feature flag. The recording+scoring flow:
//   1. pronunciationInitAttempt(drillId) → { attemptId, uploadUrl, ... }
//   2. pronunciationUploadAudio(drillId, attemptId, blob, durationMs)
//   3. fetchPronunciationAssessment(assessmentId) for the result detail page.
// ── Admin: Pronunciation CMS ────────────────────────────────────────────────
export type {
  AdminPronunciationGenerateAudioResponse,
  PronunciationAssessmentDetail,
  PronunciationDrillSummary,
  PronunciationEntitlement,
  PronunciationProgressItem,
} from './api/pronunciation';
export {
  adminPronunciationAiDraft,
  archiveAdminPronunciationDrill,
  createAdminPronunciationDrill,
  fetchAdminPronunciationDrill,
  fetchAdminPronunciationDrills,
  fetchMyPronunciationProgress,
  fetchPronunciationAssessment,
  fetchPronunciationDrill,
  fetchPronunciationDrills,
  fetchPronunciationDueDrills,
  fetchPronunciationEntitlement,
  fetchPronunciationProfile,
  fetchPronunciationSpeakingLinked,
  forceDeleteAdminPronunciationDrill,
  generateAdminPronunciationModelAudio,
  pronunciationInitAttempt,
  pronunciationUploadAudio,
  submitPronunciationDiscrimination,
  updateAdminPronunciationDrill,
} from './api/pronunciation';

// ── Certificates ──────────────────────────────────────────────────────────────
// ── Referrals ─────────────────────────────────────────────────────────────────
// ── Exam Booking ──────────────────────────────────────────────────────────────
// ── Tutoring ──────────────────────────────────────────────────────────────────
export {
  applyReferralCode,
  bookTutoringSession,
  createExamBooking,
  deleteExamBooking,
  fetchExamBookings,
  fetchMyCertificates,
  fetchMyReferralCode,
  fetchMyReferrals,
  fetchTutoringSessions,
  rateTutoringSession,
  verifyCertificate,
} from './api/learner-perks';

// ── AI Conversation ─────────────────────────────────────────────────────
// ── Admin: Conversation Templates ──────────────────────────────────────
export type {
  AdminElevenLabsVoice,
  AdminLaunchReadinessSettings,
  AppReleasePolicy,
} from './api/conversation';
export {
  adminConversationTtsPreview,
  archiveAdminConversationTemplate,
  completeConversation,
  conversationTranscriptExportUrl,
  createAdminConversationTemplate,
  createConversation,
  downloadConversationTranscript,
  fetchAdminConversationSessionDetail,
  fetchAdminConversationSessions,
  fetchAdminConversationSettings,
  fetchAdminConversationTemplate,
  fetchAdminConversationTemplates,
  fetchAdminLaunchReadinessSettings,
  fetchAppReleasePolicy,
  forceDeleteAdminConversationTemplate,
  getConversation,
  getConversationEntitlement,
  getConversationEvaluation,
  getConversationHistory,
  getConversationTaskTypes,
  getElevenLabsVoices,
  publishAdminConversationTemplate,
  resumeConversation,
  updateAdminConversationSettings,
  updateAdminConversationTemplate,
  updateAdminLaunchReadinessSettings,
} from './api/conversation';

// ── Mocks Module Phase 6 — admin QC pipeline + item retire ──
export type {
  AdminAnswerKeyReport,
  AdminAnswerKeyReportAssessment,
  AdminAnswerKeyReportStatus,
  AdminMockItemAnalysisResponse,
  AdminMockItemAnalysisRow,
  AdminMockLeakReport,
  AdminMockLeakReportStatus,
  MockBundleBulkAction,
  MockBundleReviewStageEntry,
  MockBundleReviewStageSummary,
  MockItemRetireResponse,
  MockReviewStage,
} from './api/admin-mocks';
export {
  addAdminMockBundleSection,
  advanceMockBundleReviewStage,
  archiveAdminMockBundle,
  assignAdminMockBooking,
  bulkAdminMockBundles,
  createAdminMockBundle,
  fetchAdminMockAnalytics,
  fetchAdminMockBundle,
  fetchAdminMockBundleItemAnalysis,
  fetchAdminMockBundleListeningItemAnalysis,
  fetchAdminMockBundles,
  fetchAdminMockItemAnalysis,
  fetchAdminMockRiskList,
  fetchMockBundleReviewStage,
  listAdminAnswerKeyReports,
  listAdminMockLeakReports,
  publishAdminMockBundle,
  recomputeAdminMockBundleItemAnalysis,
  retireMockItem,
  reorderAdminMockBundleSections,
  updateAdminMockBundle,
  updateAdminMockLeakReport,
  updateAdminAnswerKeyReport,
  MOCK_REVIEW_STAGES,
} from './api/admin-mocks';
// Booking projections live in ./api/mock-bookings (shared mapMockBooking).
export type {
  AdminMockBookingRow,
  ExpertMockBookingDetail,
  ExpertSpeakingContent,
  ExpertSpeakingInterlocutorCard,
  MockLiveRoomTargetState,
  MockLiveRoomTransitionOptions,
} from './api/mock-bookings';
export {
  fetchAdminMockBookings,
  fetchExpertMockBookings,
  fetchExpertMockBookingDetail,
  mapMockBooking,
  normalizeMockDeliveryMode,
  transitionAdminMockBookingLiveRoom,
  transitionAdminMockBookingLiveRoomState,
  transitionExpertMockBookingLiveRoom,
  transitionMockBookingLiveRoom,
} from './api/mock-bookings';

// ── Writing Coach ───────────────────────────────────────────────────────
// ── Content Generation (Admin) ──────────────────────────────────────────
// ── Content Marketplace ─────────────────────────────────────────────────
export {
  browseMarketplace,
  coachCheckText,
  createMarketplaceSubmission,
  fetchCoachStats,
  fetchContentGenerationJob,
  fetchContentGenerationJobs,
  fetchMarketplaceProfile,
  fetchMarketplaceSubmission,
  fetchMyMarketplaceSubmissions,
  fetchPendingMarketplaceSubmissions,
  queueContentGeneration,
  resolveCoachSuggestion,
  reviewMarketplaceSubmission,
  updateMarketplaceProfile,
} from './api/content-studio';

// ── Admin Permissions (RBAC) ──────────────────────────
// ── Permission Templates ──────────────────────────────
// ── Content Publishing Workflow ────────────────────────
// ── Webhook Monitoring ────────────────────────────────
export {
  applyPermissionTemplate,
  approvePublishRequest,
  createPermissionTemplate,
  deletePermissionTemplate,
  editorApproveContent,
  editorRejectContent,
  fetchAdminPermissions,
  fetchAllPermissions,
  fetchPendingReviewContent,
  fetchPermissionTemplates,
  fetchPublishRequests,
  fetchWebhookEvents,
  fetchWebhookSummary,
  publisherApproveContent,
  publisherRejectContent,
  rejectPublishRequest,
  requestContentPublish,
  retryWebhook,
  submitContentForReview,
  updateAdminPermissions,
} from './api/admin-governance';

// ── Review Escalations ────────────────────────────────
// ── Learner Escalations (Disputes) ────────────────────
// ── Score Guarantee (Learner) ─────────────────────────
// ── Score Equivalences ────────────────────────────────
// ── Study Commitment ──────────────────────────────────
export {
  activateScoreGuarantee,
  assignEscalationReviewer,
  fetchEscalationDetails,
  fetchMyEscalations,
  fetchReviewEscalations,
  fetchScoreEquivalences,
  fetchScoreGuarantee,
  fetchStudyCommitment,
  resolveEscalation,
  setStudyCommitment,
  submitEscalation,
  submitScoreGuaranteeClaim,
} from './api/escalations';

// ── Certificates ──────────────────────────────────────
// ── Referral ──────────────────────────────────────────
export {
  fetchCertificates,
  fetchReferralInfo,
  generateReferralCode,
} from './api/learner-perks';

// ── Expert Annotation Templates ───────────────────────
// ── Expert Amend Review ──────────────────────────────
// ── Expert Rework Chain ──────────────────────────────
// ── Expert Bulk Operations ────────────────────────────
// ── Expert Messaging ──────────────────────────────────
// ── Expert Compensation ───────────────────────────────
// ── Admin: Score Guarantee Claims ─────────────────────
export {
  amendReview,
  bulkClaimReviews,
  bulkReleaseReviews,
  createAnnotationTemplate,
  createExpertMessageThread,
  deleteAnnotationTemplate,
  fetchAdminScoreGuaranteeClaims,
  fetchAmendEligibility,
  fetchAnnotationTemplates,
  fetchExpertCompensationSummary,
  fetchExpertEarningsHistory,
  fetchExpertMessageThread,
  fetchExpertMessageThreads,
  fetchExpertPayouts,
  fetchReworkChain,
  postExpertMessageReply,
  reviewScoreGuaranteeClaim,
  updateAnnotationTemplate,
} from './api/expert-ops';

// ── Private Speaking Sessions ─────────────────────────────
// ── Private Speaking: Expert ──────────────────────────────
// ── Private Speaking: Admin ───────────────────────────────
export type {
  LiveClassJoinToken,
  PrivateSpeakingBookingResult,
  PrivateSpeakingCalendarConnectResult,
  PrivateSpeakingCalendarStatus,
} from './api/private-speaking';
export {
  adminEditPrivateSpeakingBooking,
  adminManualReschedulePrivateSpeaking,
  adminMarkPrivateSpeakingNoShow,
  adminOverridePrivateSpeakingRefund,
  adminUpdatePrivateSpeakingAvailabilityRule,
  cancelAdminPrivateSpeakingBooking,
  cancelExpertPrivateSpeakingSession,
  cancelPrivateSpeakingBooking,
  completeAdminPrivateSpeakingBooking,
  connectExpertPrivateSpeakingGoogleCalendar,
  createAdminPrivateSpeakingAvailabilityRule,
  createAdminPrivateSpeakingTutor,
  createPrivateSpeakingBooking,
  deleteAdminPrivateSpeakingAvailabilityRule,
  deleteExpertPrivateSpeakingAvailability,
  disconnectExpertPrivateSpeakingCalendar,
  downloadAdminPrivateSpeakingBookingsCsv,
  downloadExpertPrivateSpeakingCalendarInvite,
  downloadPrivateSpeakingCalendarInvite,
  fetchAdminPrivateSpeakingAuditLogs,
  fetchAdminPrivateSpeakingAvailability,
  fetchAdminPrivateSpeakingBookings,
  fetchAdminPrivateSpeakingConfig,
  fetchAdminPrivateSpeakingStats,
  fetchAdminPrivateSpeakingTutor,
  fetchAdminPrivateSpeakingTutors,
  fetchAllPrivateSpeakingSlots,
  fetchExpertPrivateSpeakingAvailability,
  fetchExpertPrivateSpeakingCalendarStatus,
  fetchExpertPrivateSpeakingJoinToken,
  fetchExpertPrivateSpeakingProfile,
  fetchExpertPrivateSpeakingSessionDetail,
  fetchExpertPrivateSpeakingSessions,
  fetchLearnerPrivateSpeakingBookings,
  fetchPrivateSpeakingBookingDetail,
  fetchPrivateSpeakingConfig,
  fetchPrivateSpeakingJoinToken,
  fetchPrivateSpeakingSlots,
  fetchPrivateSpeakingTutors,
  markExpertPrivateSpeakingNoShow,
  ratePrivateSpeakingSession,
  reschedulePrivateSpeakingBooking,
  retryAdminPrivateSpeakingZoom,
  updateAdminPrivateSpeakingConfig,
  updateAdminPrivateSpeakingTutor,
  updateExpertPrivateSpeakingAvailability,
  updateExpertPrivateSpeakingAvailabilityRule,
} from './api/private-speaking';
import type { LiveClassJoinToken } from './api/private-speaking';

// ── Zoom Live Classes ───────────────────────────────────
// ── Tutor (Zoom-backed live classes — wave B1) ──────────
export type { AdminLiveClassUpsertPayload } from './api/live-classes';
export type {
  ClassFeedbackEntry,
  ClassFeedbackSubmitPayload,
  ClassWaitlistEntry,
  DayOfWeekString,
  LiveClassDetail,
  LiveClassEnrollment,
  LiveClassListItem,
  LiveClassQueryParams,
  LiveClassRecording,
  LiveClassSessionSummary,
  LiveClassTranscript,
  TutorAttendanceLine,
  TutorAvailabilitySlot,
  TutorAvailabilityUpsertPayload,
  TutorClassCreatePayload,
  TutorClassSessionCreatePayload,
  TutorClassSessionUpdatePayload,
  TutorClassUpdatePayload,
  TutorEarnings,
  TutorEarningsLine,
  TutorProfile,
  TutorUpsertPayload,
} from './api/live-classes';
export {
  addAdminLiveClassSession,
  addTutorClassSession,
  cancelAdminLiveClassSession,
  cancelLiveClassEnrollment,
  cancelTutorClassSession,
  createAdminLiveClass,
  createTutorClass,
  createTutorProfile,
  enrollLiveClassSession,
  fetchAdminLiveClassAnalytics,
  fetchAdminLiveClassDetail,
  fetchAdminLiveClasses,
  fetchClassTranscript,
  fetchExpertLiveClasses,
  fetchExpertLiveClassJoinToken,
  fetchLiveClassDetail,
  fetchLiveClassJoinToken,
  fetchLiveClassRecording,
  fetchLiveClasses,
  fetchMyPastLiveClasses,
  fetchMyUpcomingLiveClasses,
  fetchTutorAvailability,
  fetchTutorClasses,
  fetchTutorEarnings,
  fetchTutorProfile,
  fetchTutorSessionAttendance,
  joinClassWaitlist,
  leaveClassWaitlist,
  provisionTutorZoomUser,
  publishAdminLiveClass,
  replaceTutorAvailability,
  retryAdminLiveClassSessionZoom,
  submitClassFeedback,
  updateAdminLiveClassSession,
  updateTutorClass,
  updateTutorClassSession,
  updateTutorProfile,
} from './api/live-classes';

// ── Orphan Endpoint Wiring ────────────────────────────
// ── Sponsor Dashboard ──
export type {
  SponsorBillingData,
  SponsorDashboardData,
  SponsoredLearner,
  SponsorInvoice,
} from './api/misc';
export {
  applyStreakFreeze,
  fetchDiagnosticPersonalization,
  fetchFluencyTimeline,
  fetchReadinessRisk,
  fetchSponsorBilling,
  fetchSponsorDashboard,
  fetchSponsoredLearners,
  fetchStudyPlanDrift,
  inviteSponsoredLearner,
  regenerateStudyPlan,
  removeSponsoredLearner,
} from './api/misc';

// -- Admin: Rulebook Management ------------------------------------------
// === SUBAGENT_BACKEND: admin-content-management START ===
// === SUBAGENT_BACKEND: admin-content-management END ===
export type {
  AdminBulkPaperPublishResult,
  AdminBulkPaperStatusResult,
  AdminConversationAiDraftPayload,
  AdminConversationAiDraftResult,
  AdminPublishWithWarningsResponse,
  AdminRulebookDetail,
  AdminRulebookMetadata,
  AdminRulebookRule,
  AdminRulebookSection,
  AdminRulebookSummary,
} from './api/admin-rulebooks';
export {
  adminBulkPublishPapers,
  adminBulkSetPaperStatus,
  adminCloneRulebook,
  adminConversationAiDraft,
  adminCreateRulebook,
  adminCreateRulebookRule,
  adminCreateRulebookSection,
  adminDeleteRulebook,
  adminDeleteRulebookRule,
  adminDeleteRulebookSection,
  adminExportRulebook,
  adminGetRulebook,
  adminGetRulebookMetadata,
  adminImportRulebook,
  adminListRulebooks,
  adminPublishPaperWithWarnings,
  adminPublishRulebook,
  adminUnarchiveConversationTemplate,
  adminUnarchiveGrammarLesson,
  adminUnarchiveMockBundle,
  adminUnarchivePaper,
  adminUnarchivePronunciationDrill,
  adminUnpublishRulebook,
  adminUpdateRulebookMeta,
  adminUpdateRulebookRule,
  adminUpdateRulebookSection,
} from './api/admin-rulebooks';

// === SUBAGENT_B: listening-authoring START ===
// === SUBAGENT_B: listening-authoring END ===
export {
  adminListeningBackfillAll,
  adminListeningExportAttempt,
  adminListeningGetAnalytics,
  adminListeningGetExtracts,
  adminListeningGetStructure,
  adminListeningPatchExtract,
  adminListeningPatchQuestion,
  adminListeningReplaceExtracts,
  adminListeningReplaceStructure,
  adminListeningValidate,
} from './api/listening-authoring';

// === SUBAGENT_C: bulk-import-and-generation START ===
// === SUBAGENT_C: bulk-import-and-generation END ===
export {
  adminCommitZipImport,
  adminDiscardUpload,
  adminGetGenerationJob,
  adminListGenerationJobs,
  adminQueueContentGeneration,
  adminStartZipImport,
} from './api/bulk-import';

// === SUBAGENT_E: bulk-ops START ===
// === SUBAGENT_E: bulk-ops END ===
// (Wave-2 bulk pages were never built — helpers removed 2026-09-06 as dead
// code. Backend routes are untouched; re-scaffold from OpenAPI if needed.)

// === SUBAGENT_D: speaking-conv-pron START ===
// === SUBAGENT_D: speaking-conv-pron END ===
// (Wave-2 workspace/create/backfill/analytics pages were never built —
// helpers removed 2026-09-06 as dead code. Backend routes are untouched;
// re-scaffold from OpenAPI if needed.)

// === SUBAGENT_A: reading-authoring START ===
// === SUBAGENT_A: reading-authoring END ===
export {
  adminReadingApproveExtraction,
  adminReadingCreateExtraction,
  adminReadingDeleteQuestion,
  adminReadingDeleteText,
  adminReadingEnsureCanonical,
  adminReadingGetAnalytics,
  adminReadingGetExtraction,
  adminReadingGetManifest,
  adminReadingGetReviewHistory,
  adminReadingGetStructure,
  adminReadingImportManifest,
  adminReadingListExtractions,
  adminReadingRejectExtraction,
  adminReadingReorderQuestions,
  adminReadingReorderTexts,
  adminReadingSetDistractors,
  adminReadingTransitionReview,
  adminReadingUpsertPart,
  adminReadingUpsertQuestion,
  adminReadingUpsertText,
  adminReadingValidate,
} from './api/reading-authoring';

// -----------------------------------------------------------------------------
// Scoring Policy (admin singleton document + learner read)
// -----------------------------------------------------------------------------

export interface ScoringPolicyDto {
  id: string;
  bodyMarkdown: string;
  policyJson: string;
  isActive: boolean;
  updatedByUserId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ScoringPolicyLearnerDto {
  id: string;
  bodyMarkdown: string;
  policyJson: string;
  updatedAt: string;
}

export async function adminGetScoringPolicy(): Promise<ScoringPolicyDto | null> {
  return apiRequest<ScoringPolicyDto | null>('/v1/admin/scoring-policy');
}

export async function adminUpdateScoringPolicy(payload: {
  bodyMarkdown: string;
  policyJson: string;
}): Promise<ScoringPolicyDto> {
  return apiRequest<ScoringPolicyDto>('/v1/admin/scoring-policy', {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function adminListScoringPolicyHistory(): Promise<ScoringPolicyDto[]> {
  return apiRequest<ScoringPolicyDto[]>('/v1/admin/scoring-policy/history');
}

export async function adminActivateScoringPolicy(id: string): Promise<ScoringPolicyDto> {
  return apiRequest<ScoringPolicyDto>(`/v1/admin/scoring-policy/${encodeURIComponent(id)}/activate`, {
    method: 'POST',
    body: '{}',
  });
}

export async function learnerGetScoringPolicy(): Promise<ScoringPolicyLearnerDto | null> {
  return apiRequest<ScoringPolicyLearnerDto | null>('/v1/scoring-policy');
}

// -----------------------------------------------------------------------------
// Rulebook reference PDF (human-readable companion to the JSON rulebook)
// -----------------------------------------------------------------------------

export interface RulebookReferencePdfDto {
  id: string;
  referencePdfAssetId: string;
  originalFilename: string;
  sizeBytes: number;
}

export async function adminUploadRulebookReferencePdf(rulebookId: string, file: File): Promise<RulebookReferencePdfDto> {
  const form = new FormData();
  form.append('file', file);
  const token = await ensureFreshAccessToken();
  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  const response = await fetchWithTimeout(
    resolveApiUrl(`/v1/admin/rulebooks/${encodeURIComponent(rulebookId)}/reference-pdf`),
    { method: 'POST', headers, body: form },
    120_000,
  );
  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new Error(`Upload failed: ${response.status} ${text}`);
  }
  return response.json() as Promise<RulebookReferencePdfDto>;
}

export async function adminDeleteRulebookReferencePdf(rulebookId: string): Promise<void> {
  await apiRequest<void>(
    `/v1/admin/rulebooks/${encodeURIComponent(rulebookId)}/reference-pdf`,
    { method: 'DELETE' },
    { acceptedStatuses: [204] },
  );
}

export async function learnerGetRulebookReferencePdf(kind: string, profession: string): Promise<{ rulebookId: string; referencePdfAssetId: string } | null> {
  try {
    return await apiRequest<{ rulebookId: string; referencePdfAssetId: string }>(`/v1/rulebooks/${encodeURIComponent(kind)}/${encodeURIComponent(profession)}/reference-pdf`);
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null;
    throw e;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Recall set tags (practice-collection labels) — admin CRUD
// ─────────────────────────────────────────────────────────────────────────────

export interface RecallSetTagDto {
  code: string;
  displayName: string;
  shortLabel: string | null;
  description: string | null;
  sortOrder: number;
  isActive: boolean;
  examTypeCode: string | null;
  createdByUserId: string | null;
  createdAt: string;
  updatedAt: string;
  canonical: boolean;
}

export async function adminListRecallSetTags(params: { includeArchived?: boolean; examTypeCode?: string } = {}): Promise<RecallSetTagDto[]> {
  const qs = new URLSearchParams();
  if (params.includeArchived) qs.set('includeArchived', 'true');
  if (params.examTypeCode) qs.set('examTypeCode', params.examTypeCode);
  const q = qs.toString();
  return apiRequest<RecallSetTagDto[]>(`/v1/admin/recall-set-tags${q ? `?${q}` : ''}`);
}

export async function adminGetRecallSetTag(code: string): Promise<RecallSetTagDto> {
  return apiRequest<RecallSetTagDto>(`/v1/admin/recall-set-tags/${encodeURIComponent(code)}`);
}

export async function adminCreateRecallSetTag(payload: {
  code: string;
  displayName: string;
  shortLabel?: string | null;
  description?: string | null;
  sortOrder?: number;
  isActive?: boolean;
  examTypeCode?: string | null;
}): Promise<RecallSetTagDto> {
  return apiRequest<RecallSetTagDto>('/v1/admin/recall-set-tags', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function adminUpdateRecallSetTag(code: string, patch: {
  displayName?: string;
  shortLabel?: string | null;
  description?: string | null;
  sortOrder?: number;
  isActive?: boolean;
  examTypeCode?: string | null;
}): Promise<RecallSetTagDto> {
  return apiRequest<RecallSetTagDto>(`/v1/admin/recall-set-tags/${encodeURIComponent(code)}`, {
    method: 'PUT',
    body: JSON.stringify(patch),
  });
}

export async function adminArchiveRecallSetTag(code: string): Promise<RecallSetTagDto> {
  return apiRequest<RecallSetTagDto>(`/v1/admin/recall-set-tags/${encodeURIComponent(code)}/archive`, {
    method: 'POST', body: '{}',
  });
}

export async function adminUnarchiveRecallSetTag(code: string): Promise<RecallSetTagDto> {
  return apiRequest<RecallSetTagDto>(`/v1/admin/recall-set-tags/${encodeURIComponent(code)}/unarchive`, {
    method: 'POST', body: '{}',
  });
}

export async function adminDeleteRecallSetTag(code: string): Promise<{ archived: boolean; code: string; hardDelete: boolean; reason?: string }> {
  return apiRequest(`/v1/admin/recall-set-tags/${encodeURIComponent(code)}`, {
    method: 'DELETE',
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// Result-template gallery (images displayed on learner mock-result pages)
// ─────────────────────────────────────────────────────────────────────────────

export interface ResultTemplateDto {
  id: string;
  templateKey: string;
  title: string;
  description: string | null;
  professionId: string | null;
  mediaAssetId: string;
  isActive: boolean;
  sortOrder: number;
  uploadedByUserId: string | null;
  createdAt: string;
  updatedAt: string;
  media: {
    id: string;
    originalFilename: string;
    mimeType: string;
    format: string;
    sizeBytes: number;
    sha256: string | null;
  } | null;
}

export interface LearnerResultTemplateDto {
  id: string;
  templateKey: string;
  title: string;
  description: string | null;
  professionId: string | null;
  mediaAssetId: string;
  sortOrder: number;
  updatedAt: string;
  media: {
    id: string;
    originalFilename: string;
    mimeType: string;
    sizeBytes: number;
  } | null;
}

export async function adminListResultTemplates(profession?: string): Promise<ResultTemplateDto[]> {
  const qs = profession ? `?profession=${encodeURIComponent(profession)}` : '';
  return apiRequest<ResultTemplateDto[]>(`/v1/admin/result-templates${qs}`);
}

export async function adminUploadResultTemplate(payload: {
  file: File;
  templateKey: string;
  title: string;
  description?: string | null;
  professionId?: string | null;
  sortOrder?: number;
}): Promise<ResultTemplateDto> {
  const form = new FormData();
  form.append('file', payload.file);
  form.append('templateKey', payload.templateKey);
  form.append('title', payload.title);
  if (payload.description != null) form.append('description', payload.description);
  if (payload.professionId) form.append('professionId', payload.professionId);
  if (payload.sortOrder != null) form.append('sortOrder', String(payload.sortOrder));
  const token = await ensureFreshAccessToken();
  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  const response = await fetchWithTimeout(resolveApiUrl('/v1/admin/result-templates'), {
    method: 'POST', headers, body: form,
  }, 120_000);
  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new Error(`Upload failed: ${response.status} ${text}`);
  }
  return response.json() as Promise<ResultTemplateDto>;
}

export async function adminUpdateResultTemplate(id: string, patch: {
  title?: string;
  description?: string | null;
  professionId?: string | null;
  sortOrder?: number;
}): Promise<ResultTemplateDto> {
  return apiRequest<ResultTemplateDto>(`/v1/admin/result-templates/${encodeURIComponent(id)}`, {
    method: 'PUT', body: JSON.stringify(patch),
  });
}

export async function adminActivateResultTemplate(id: string): Promise<{ id: string; isActive: boolean }> {
  return apiRequest(`/v1/admin/result-templates/${encodeURIComponent(id)}/activate`, { method: 'POST', body: '{}' });
}

export async function adminDeactivateResultTemplate(id: string): Promise<{ id: string; isActive: boolean }> {
  return apiRequest(`/v1/admin/result-templates/${encodeURIComponent(id)}/deactivate`, { method: 'POST', body: '{}' });
}

export async function adminDeleteResultTemplate(id: string): Promise<void> {
  await apiRequest<void>(`/v1/admin/result-templates/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  }, { acceptedStatuses: [204] });
}

/** Permanently removes a result-template row (the MediaAsset it points at is kept). system_admin only. */
export async function adminForceDeleteResultTemplate(id: string): Promise<void> {
  await apiRequest<void>(`/v1/admin/result-templates/${encodeURIComponent(id)}/force-delete`, {
    method: 'POST',
  }, { acceptedStatuses: [204] });
}

export async function learnerGetActiveResultTemplate(): Promise<LearnerResultTemplateDto | null> {
  try {
    return await apiRequest<LearnerResultTemplateDto>('/v1/result-templates/active');
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null;
    throw e;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Speaking shared resources (Warm-up Questions + Assessment Criteria PDFs)
// ─────────────────────────────────────────────────────────────────────────────

export type SpeakingSharedResourceKind = 'WarmUpQuestions' | 'AssessmentCriteria';

export interface SpeakingSharedResourceDto {
  id: string;
  kind: SpeakingSharedResourceKind;
  title: string;
  professionId: string | null;
  mediaAssetId: string;
  status: 'Draft' | 'InReview' | 'EditorReview' | 'PublisherApproval' | 'Published' | 'Rejected' | 'Archived';
  publishedAt: string | null;
  effectiveFrom: string | null;
  uploadedByUserId: string | null;
  createdAt: string;
  updatedAt: string;
  media: {
    id: string;
    originalFilename: string;
    mimeType: string;
    sizeBytes: number;
    sha256: string | null;
  } | null;
}

export interface SpeakingSharedResourceLearnerDto {
  id: string;
  kind: SpeakingSharedResourceKind;
  title: string;
  professionId: string | null;
  publishedAt: string | null;
  media: { id: string; originalFilename: string; sizeBytes: number } | null;
}

export async function adminListSpeakingSharedResources(params: { kind?: SpeakingSharedResourceKind; profession?: string } = {}): Promise<SpeakingSharedResourceDto[]> {
  const qs = new URLSearchParams();
  if (params.kind) qs.set('kind', params.kind);
  if (params.profession) qs.set('profession', params.profession);
  const q = qs.toString();
  return apiRequest<SpeakingSharedResourceDto[]>(`/v1/admin/speaking/shared-resources${q ? `?${q}` : ''}`);
}

export async function adminUploadSpeakingSharedResource(payload: {
  file: File;
  kind: SpeakingSharedResourceKind;
  title: string;
  professionId?: string | null;
}): Promise<SpeakingSharedResourceDto> {
  const form = new FormData();
  form.append('file', payload.file);
  form.append('kind', payload.kind);
  form.append('title', payload.title);
  if (payload.professionId) form.append('professionId', payload.professionId);
  const token = await ensureFreshAccessToken();
  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  const response = await fetchWithTimeout(resolveApiUrl('/v1/admin/speaking/shared-resources'), {
    method: 'POST', headers, body: form,
  }, 120_000);
  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new Error(`Upload failed: ${response.status} ${text}`);
  }
  return response.json() as Promise<SpeakingSharedResourceDto>;
}

export async function adminPublishSpeakingSharedResource(id: string): Promise<{ id: string; status: string }> {
  return apiRequest(`/v1/admin/speaking/shared-resources/${encodeURIComponent(id)}/publish`, { method: 'POST', body: '{}' });
}

export async function adminArchiveSpeakingSharedResource(id: string): Promise<{ id: string; status: string }> {
  return apiRequest(`/v1/admin/speaking/shared-resources/${encodeURIComponent(id)}/archive`, { method: 'POST', body: '{}' });
}

export async function adminDeleteSpeakingSharedResource(id: string): Promise<void> {
  await apiRequest<void>(`/v1/admin/speaking/shared-resources/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  }, { acceptedStatuses: [204] });
}

/** Permanently removes a speaking shared resource row (MediaAsset left intact). system_admin only. */
export async function adminForceDeleteSpeakingSharedResource(id: string): Promise<void> {
  await apiRequest<void>(`/v1/admin/speaking/shared-resources/${encodeURIComponent(id)}/force-delete`, {
    method: 'POST',
  }, { acceptedStatuses: [204] });
}

export async function learnerListSpeakingSharedResources(): Promise<SpeakingSharedResourceLearnerDto[]> {
  return apiRequest<SpeakingSharedResourceLearnerDto[]>('/v1/speaking/shared-resources');
}

export async function downloadSpeakingSharedResourceMedia(assetId: string): Promise<Blob> {
  const path = `/v1/media/${encodeURIComponent(assetId)}/content`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path, undefined, { json: false }),
  }, 120_000);
  if (!response.ok) {
    throw new ApiError(response.status, 'speaking_shared_resource_download_failed', `Speaking resource download failed: ${response.status}`, isRetryable(response.status));
  }
  return response.blob();
}

// ─────────────────────────────────────────────────────────────────────────────
// Real Content folder importer
// ─────────────────────────────────────────────────────────────────────────────

export type RealContentTarget =
  | 'ListeningPaper' | 'ReadingPaper' | 'WritingPaper' | 'SpeakingPaper'
  | 'RecallDocument' | 'ResultTemplate' | 'SpeakingSharedResource'
  | 'RulebookReferencePdf' | 'ScoringPolicyBody';

export interface RealContentProposalDto {
  target: RealContentTarget;
  title: string;
  subtest: string | null;
  professionId: string | null;
  cardType: string | null;
  letterType: string | null;
  periodLabel: string | null;
  templateKey: string | null;
  sharedResourceKind: string | null;
  rulebookKind: string | null;
  rulebookProfession: string | null;
  sourcePath: string;
  assets: Array<{ role: string; part: string | null; sourcePath: string; originalFilename: string | null }>;
}

export interface RealContentStageResultDto {
  sessionId: string;
  uploadedFilename: string;
  stagedAt: string;
  proposals: RealContentProposalDto[];
  issues: string[];
}

export interface RealContentCommitResultDto {
  created: Array<{ target: RealContentTarget; id: string; title: string }>;
  errors: string[];
}

export async function adminStageRealContentFolder(file: File): Promise<RealContentStageResultDto> {
  const form = new FormData();
  form.append('file', file);
  const token = await ensureFreshAccessToken();
  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  const response = await fetchWithTimeout(
    resolveApiUrl('/v1/admin/imports/real-content-folder/stage'),
    { method: 'POST', headers, body: form },
    600_000, // 10 min for large ZIPs
  );
  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new Error(`Stage failed: ${response.status} ${text}`);
  }
  return response.json() as Promise<RealContentStageResultDto>;
}

export async function adminCommitRealContentFolder(
  sessionId: string,
  approvedSourcePaths?: string[],
): Promise<RealContentCommitResultDto> {
  return apiRequest<RealContentCommitResultDto>(
    `/v1/admin/imports/real-content-folder/${encodeURIComponent(sessionId)}/commit`,
    {
      method: 'POST',
      body: JSON.stringify({ approvedSourcePaths: approvedSourcePaths ?? null }),
    },
  );
}

export async function downloadRulebookReferencePdfMedia(assetId: string): Promise<Blob> {
  const path = `/v1/media/${encodeURIComponent(assetId)}/content`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path, undefined, { json: false }),
  }, 120_000);
  if (!response.ok) {
    throw new ApiError(response.status, 'rulebook_reference_pdf_download_failed', `Rulebook reference PDF download failed: ${response.status}`, isRetryable(response.status));
  }
  return response.blob();
}

/** Generic authenticated fetch of a MediaAsset's bytes (PDF/audio/image) for
 * in-app preview. Caller is responsible for `URL.createObjectURL` lifecycle. */
export async function downloadMediaAssetContent(assetId: string): Promise<Blob> {
  const path = `/v1/media/${encodeURIComponent(assetId)}/content`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path, undefined, { json: false }),
  }, 120_000);
  if (!response.ok) {
    throw new ApiError(response.status, 'media_asset_download_failed', `Media download failed: ${response.status}`, isRetryable(response.status));
  }
  return response.blob();
}

// ── Admin mocks analytics (Phase 3) ─────────────────────────────────────
export type {
  AdminMocksAnalyticsAttemptsCompletion,
  AdminMocksAnalyticsAverageReadiness,
  AdminMocksAnalyticsMarkingDelay,
  AdminMocksAnalyticsMarkingDelayRow,
  AdminMocksAnalyticsPassPrediction,
  AdminMocksAnalyticsPassPredictionProfessionRow,
  AdminMocksAnalyticsReadinessDistribution,
  AdminMocksAnalyticsResponse,
  AdminMocksAnalyticsRevenueRow,
  AdminMocksAnalyticsTutorWorkloadRow,
  AdminMocksAnalyticsLowQualityRow,
  AdminMocksAnalyticsReadingSection,
  AdminMocksAnalyticsWindow,
} from './api/admin-quality';
export {
  fetchAdminMocksAnalytics,
} from './api/admin-quality';

// ── Admin speaking calibration (Phase 7a) ───────────────────────────────
// ── Admin interlocutor onboarding (Phase 7a) ────────────────────────────
export type {
  InterlocutorPracticeQueueResponse,
  InterlocutorPracticeQueueRow,
  InterlocutorPracticeSessionStart,
  InterlocutorTraineeRow,
  InterlocutorTraineesResponse,
  InterlocutorTrainingStatusLabel,
  MarkInterlocutorTrainedResult,
  SpeakingCalibrationDriftSummary,
  SpeakingCalibrationDriftTutorRow,
  SpeakingCalibrationSampleSummaryRow,
  SpeakingCalibrationSamplesResponse,
} from './api/admin-quality';
export {
  fetchInterlocutorPracticeQueue,
  fetchInterlocutorTraineeList,
  fetchInterlocutorTrainees,
  fetchSpeakingCalibrationOverview,
  fetchSpeakingCalibrationSets,
  fetchSpeakingCalibrationSummary,
  markInterlocutorTrained,
  startInterlocutorPracticeSession,
} from './api/admin-quality';

// ── Voice Design Studio API ─────────────────────────────────────────
export type {
  AdminAudioBatch,
  AdminAudioRegenerateBatchResult,
  AdminAudioRegenerateRequest,
  AdminVoiceDesignConfig,
} from './api/voice-design';
export {
  cancelAudioRegenerationBatch,
  getAdminRecallsAudioBatchProgress,
  getAdminVoiceDesignConfig,
  getAudioRegenerationBatchProgress,
  getAudioRegenerationBatches,
  previewAdminVoiceDesign,
  regenerateAllAudio,
  retryAudioRegenerationBatch,
  saveAdminVoiceDesignConfig,
  startAdminRecallsAudioBackfill,
} from './api/voice-design';

export async function uploadElevenLabsPronunciationDictionary(
  file: File,
  name?: string,
): Promise<{ dictionaryId: string; versionId: string | null }> {
  const form = new FormData();
  form.append('file', file);
  if (name?.trim()) form.append('name', name.trim());

  return apiClient.postForm<{ dictionaryId: string; versionId: string | null }>(
    '/v1/admin/voice-design/elevenlabs/dictionary',
    form,
  );
}

// ─────────────────────────────────────────────────────────────────────────
// OET Speaking module re-exports
//
// Surface the typed API clients living under `lib/api/speaking-*.ts` so
// existing call sites can keep importing from `lib/api`. Each module
// owns its own request/response types — these `export *` lines are the
// single integration point.
// ─────────────────────────────────────────────────────────────────────────
export * from './api/speaking-role-play-cards';
export * from './api/speaking-sessions';
export * from './api/speaking-live-rooms';
export * from './api/speaking-assessments';
export * from './api/speaking-compliance';
export * from './api/billing-region';
export * from './api/billing-expansion';
export * from './api/ai-analytics';

// ─────────────────────────────────────────────────────────────────────────────
// Listening Policy Admin
// ─────────────────────────────────────────────────────────────────────────────
export type {
  ListeningPolicyDto,
  ListeningUserPolicyOverrideDto,
} from './api/listening-policy';
export {
  adminGetListeningPolicy,
  adminGetListeningUserPolicyOverride,
  adminUpsertListeningPolicy,
  adminUpsertListeningUserPolicyOverride,
} from './api/listening-policy';

// ── Wave B2: Cart / Checkout / Subscription self-service / Admin products & coupons ──
//
// (Server-cart wrappers removed 2026-09-06 as dead code: the live cart is
// client state (`lib/cart/cart-store`) + Stripe, and zero callers used these
// helpers. Backend routes are untouched; re-scaffold from OpenAPI if needed.)


// ── Subscription self-service ───────────────────────────────────────
export type {
  SubscriptionInvoice,
  SubscriptionMe,
  SubscriptionMeListItem,
} from './api/subscriptions';
export {
  cancelSubscription,
  changeSubscriptionPlanSelf,
  createSubscriptionPortalSession,
  fetchSubscriptionInvoices,
  fetchSubscriptionMe,
  fetchSubscriptionsMe,
  pauseSubscriptionSelf,
  requestSubscriptionFreeze,
  resumeSubscriptionById,
  resumeSubscriptionSelf,
} from './api/subscriptions';

// ── Admin: Billing products (catalog) — Wave B2 thin CRUD ───────────
export type {
  AdminBillingAnalyticsResponse,
  AdminBillingAnalyticsSeriesPoint,
  AdminBillingProduct,
  AdminBillingProductPrice,
  AdminRefundRequest,
} from './api/billing-products';
export {
  fetchAdminBillingAnalytics,
  fetchAdminBillingProduct,
  fetchAdminBillingProducts,
  fetchAdminRefunds,
  postAdminRefundAction,
  updateAdminBillingProduct,
} from './api/billing-products';

// ── Checkout session status ──────────────────────────────────────────
// Restored: dropped entirely (not just un-re-exported) somewhere in the
// lib/api.ts split — still consumed by components/checkout/CheckoutSessionSummary.tsx
// and CheckoutSuccessPoller.tsx, which failed to compile without it.
export interface CheckoutSessionStatusItem {
  productCode: string;
  productName: string;
  quantity: number;
  description?: string | null;
}

export interface CheckoutSessionStatus {
  sessionId: string;
  status: 'pending' | 'fulfilled' | 'failed' | 'expired' | string;
  totalAmount?: number;
  currency?: string;
  items?: CheckoutSessionStatusItem[];
  failureReason?: string | null;
  fulfilledAt?: string | null;
  /**
   * How the purchased package is handed over — `automatic_web` | `manual_web` |
   * `whatsapp` | `manual_material`. `status: 'fulfilled'` only means the PAYMENT
   * cleared; for anything other than `automatic_web` access is NOT live, because the
   * subscription stays Pending until an admin marks it fulfilled (spec 2026-07-15
   * §2/§6.6). The success page must branch on this before claiming access was added.
   */
  deliveryMethod?: string | null;
  /**
   * `auto` | `pending_manual` | `fulfilled` for the subscription this order opened.
   * Null when the order granted no course subscription, and also null on cart-pipeline
   * orders that never created a domain Subscription — so treat `deliveryMethod` as the
   * load-bearing signal and this as best-effort enrichment.
   */
  fulfilmentStatus?: string | null;
  /** Server-confirmed external-only delivery. Undefined/null means unknown, never false. */
  externalOnly?: boolean | null;
}

export async function fetchCheckoutSessionStatus(sessionId: string): Promise<CheckoutSessionStatus> {
  return apiRequest<CheckoutSessionStatus>(`/v1/checkout/sessions/${encodeURIComponent(sessionId)}/status`);
}
