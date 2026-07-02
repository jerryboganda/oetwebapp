import { ensureFreshAccessToken } from './auth-client';
import {
  titleCase as domainTitleCase,
  minutesToLabel as domainMinutesToLabel,
  scoreRangeDisplay as domainScoreRangeDisplay,
  formatCurrency as domainFormatCurrency,
  normalizeWaveformPeaks as domainNormalizeWaveformPeaks,
  parseCriterionScore as domainParseCriterionScore,
  scoreToGrade as domainScoreToGrade,
} from './domain/format';
import { env } from './env';
import { fetchWithTimeout } from './network/fetch-with-timeout';
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
import type { FreezePolicy } from './types/freeze';
import type { BulkActionResultDto } from './types/admin';
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
import type {
  VideoLessonDetail,
  VideoLessonListItem,
  VideoLessonProgram,
  VideoProgressUpdateResponse,
} from './types/video-lessons';
import type {
  StrategyGuideAdminItem,
  StrategyGuideBookmarkUpdateResponse,
  StrategyGuideDetail,
  StrategyGuideLibrary,
  StrategyGuidePublishResult,
  StrategyGuidePublishValidation,
  StrategyGuideProgressUpdateResponse,
  StrategyGuideUpsertPayload,
} from './types/strategies';
import type {
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

const API_BASE_URL = env.apiBaseUrl;
type ApiRecord = Record<string, any>;

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

export interface LearnerFeatureFlag {
  key: string;
  enabled: boolean;
}

function asRecord(value: unknown): ApiRecord {
  return value && typeof value === 'object' ? (value as ApiRecord) : {};
}

function asArray(value: unknown): ApiRecord[] {
  return Array.isArray(value) ? value.map(asRecord) : [];
}

function toStringArray(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  return value
    .map((item) => {
      if (typeof item === 'string') return item;
      if (typeof item === 'number' || typeof item === 'boolean') return String(item);
      const record = item && typeof item === 'object' ? (item as ApiRecord) : {};
      return record.code ?? record.value ?? record.id ?? record.name ?? null;
    })
    .filter((item): item is string => typeof item === 'string' && item.length > 0);
}

function toNullableString(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
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

function toExamFamilyCode(value: unknown): ExamFamilyCode {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : '';
  if (normalized === 'ielts' || normalized === 'pte') {
    return normalized;
  }

  if (normalized !== 'oet' && normalized !== '') {
    console.warn('[API] Unknown exam family code:', value);
  }
  return 'oet';
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

function resolveApiUrl(pathOrUrl: string): string {
  if (/^https?:\/\//i.test(pathOrUrl)) {
    return pathOrUrl;
  }

  return `${API_BASE_URL}${pathOrUrl.startsWith('/') ? pathOrUrl : `/${pathOrUrl}`}`;
}

function resolveBrowserApiResourceUrl(pathOrUrl: string): string | null {
  if (typeof window !== 'undefined' && /^https?:\/\//i.test(pathOrUrl)) {
    try {
      const url = new URL(pathOrUrl);
      if (url.pathname.startsWith('/v1/')) {
        return resolveApiUrl(`${url.pathname}${url.search}`);
      }
    } catch {
      // Fall back to the normal resolver for malformed input.
    }
  }

  return null;
}

function resolveApiUploadUrl(pathOrUrl: string): string {
  return resolveBrowserApiResourceUrl(pathOrUrl) ?? resolveApiUrl(pathOrUrl);
}

async function getHeaders(path: string, extra?: HeadersInit, options?: { json?: boolean }): Promise<HeadersInit> {
  const headers = new Headers(extra);
  if (options?.json ?? true) {
    headers.set('Content-Type', 'application/json');
  }

  // Attach CSRF token (double-submit cookie pattern) for mutation requests
  if (typeof document !== 'undefined') {
    const csrfMatch = document.cookie.match(/(?:^|;\s*)oet_csrf=([^;]+)/);
    if (csrfMatch) {
      headers.set('x-csrf-token', csrfMatch[1]);
    }
  }

  try {
    const token = await ensureFreshAccessToken();
    if (token) {
      headers.set('Authorization', `Bearer ${token}`);
    } else if (process.env.NODE_ENV === 'development') {
      console.debug('[API] No auth token available for request to', path);
    }
  } catch (err) {
    if (process.env.NODE_ENV === 'development') {
      console.debug('[API] Failed to retrieve auth token:', err);
    }
  }

  return headers;
}

/** Typed API error with status, error code, retryable flag, and user-friendly message. */
export class ApiError extends Error {
  status: number;
  code: string;
  retryable: boolean;
  userMessage: string;
  fieldErrors: Array<{ field: string; code: string; message: string }>;

  constructor(status: number, code: string, message: string, retryable: boolean, fieldErrors: Array<{ field: string; code: string; message: string }> = []) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.retryable = retryable;
    this.fieldErrors = fieldErrors;
    this.userMessage = mapErrorCodeToUserMessage(code, message);
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

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

function mapErrorCodeToUserMessage(code: string, fallback: string): string {
  switch (code) {
    case 'not_authenticated': return 'Your session expired. Please sign in again.';
    case 'unauthorized': return 'Please sign in again to continue.';
    case 'draft_version_conflict': return 'Your draft was updated in another tab. Please refresh and try again.';
    case 'calibration_already_submitted': return 'This calibration case is already finalized and is now locked.';
    case 'idempotency_duplicate': return 'This action was already completed.';
    case 'not_found': return 'The requested resource was not found.';
    case 'forbidden': return 'You do not have permission to perform this action.';
    case 'validation_error': return 'Please check your input and try again.';
    case 'rate_limited': return 'Too many requests. Please wait a moment and try again.';
    case 'internal_server_error': return 'Something went wrong. Please try again later.';
    default: return fallback;
  }
}

const MAX_RETRIES = 2;
const RETRY_DELAYS = [1000, 3000];

function isRetryable(status: number): boolean {
  return status >= 500 || status === 408 || status === 429;
}

async function apiRequest<T = any>(path: string, init?: RequestInit, options?: { json?: boolean; acceptedStatuses?: number[] }): Promise<T> {
  let lastError: Error | null = null;

  for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
    try {
      const response = await fetchWithTimeout(resolveApiUrl(path), {
        ...init,
        headers: await getHeaders(path, init?.headers, options),
      });

      const acceptedStatuses = options?.acceptedStatuses ?? [];
      if (!response.ok && acceptedStatuses.includes(response.status)) {
        if (response.status === 204) {
          return undefined as T;
        }

        return response.json() as Promise<T>;
      }

      if (!response.ok) {
        let code = 'unknown_error';
        let message = `Request failed: ${response.status}`;
        let retryable = false;
        let fieldErrors: Array<{ field: string; code: string; message: string }> = [];
        try {
          const error = await response.json();
          code = error.code ?? (response.status === 401 ? 'not_authenticated' : response.status === 403 ? 'forbidden' : code);
          message = error.message ?? error.title ?? message;
          retryable = error.retryable ?? isRetryable(response.status);
          fieldErrors = Array.isArray(error.fieldErrors) ? error.fieldErrors : [];
        } catch (err) {
          if (response.status === 401) {
            code = 'not_authenticated';
          } else if (response.status === 403) {
            code = 'forbidden';
          }
          // Body wasn't JSON (e.g. backend returned an HTML error page). This
          // isn't actionable for the user and we still surface the status code
          // via the thrown ApiError; demote to debug so it doesn't spam the
          // console in production.
          if (process.env.NODE_ENV === 'development') {
            console.debug('[API] Non-JSON error response body:', err);
          }
          retryable = isRetryable(response.status);
        }

        const apiError = new ApiError(response.status, code, message, retryable, fieldErrors);

        // Retry on 5xx/408/429, but not on 4xx client errors
        if (retryable && attempt < MAX_RETRIES) {
          lastError = apiError;
          await new Promise(resolve => setTimeout(resolve, RETRY_DELAYS[attempt]));
          continue;
        }

        throw apiError;
      }

      if (response.status === 204) {
        return undefined as T;
      }

      return response.json() as Promise<T>;
    } catch (err) {
      if (err instanceof ApiError) {
        throw err;
      }

      if (err instanceof DOMException && err.name === 'AbortError') {
        const timeoutError = new ApiError(408, 'request_timeout', 'The request timed out. Please try again.', true);
        lastError = timeoutError;
        if (attempt < MAX_RETRIES) {
          await new Promise((resolve) => setTimeout(resolve, RETRY_DELAYS[attempt]));
          continue;
        }
        throw timeoutError;
      }

      // Network errors (TypeError from fetch) are retryable
      lastError = err instanceof Error ? err : new Error(String(err));
      if (attempt < MAX_RETRIES) {
        await new Promise(resolve => setTimeout(resolve, RETRY_DELAYS[attempt]));
        continue;
      }
      throw new ApiError(0, 'network_error', 'Unable to connect to the server. Please check your internet connection.', true);
    }
  }

  throw lastError ?? new Error('Request failed');
}

async function apiBlobRequest(path: string, init?: RequestInit): Promise<Blob> {
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    ...init,
    credentials: init?.credentials ?? 'include',
    headers: await getHeaders(path, init?.headers),
  });

  if (!response.ok) {
    let code = response.status === 401 ? 'not_authenticated' : response.status === 403 ? 'forbidden' : 'unknown_error';
    let message = `Request failed: ${response.status}`;
    try {
      const error = await response.json();
      code = error.code ?? code;
      message = error.message ?? error.title ?? message;
    } catch {
      // Binary endpoints often return non-JSON error bodies; status/code still carry the failure.
    }
    throw new ApiError(response.status, code, message, isRetryable(response.status));
  }

  return response.blob();
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

export async function fetchOnboardingState(): Promise<{ completed: boolean; currentStep: number; stepCount: number; canSkip: boolean; checkpoint: string; resumeRoute: string; }> {
  const data = await apiRequest<ApiRecord>('/v1/learner/onboarding/state');
  return {
    completed: Boolean(data.completed),
    currentStep: Number(data.currentStep ?? 1),
    stepCount: Number(data.stepCount ?? 4),
    canSkip: Boolean(data.canSkip),
    checkpoint: data.checkpoint ?? 'welcome',
    resumeRoute: data.resumeRoute ?? '/onboarding',
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
export type PaymentMethodMode = 'embedded' | 'redirect';

export interface PaymentMethodOption {
  /** Gateway name passed back to checkout / top-up (e.g. "stripe", "paypal", "checkoutcom"). */
  name: string;
  /** Learner-facing label for the method. */
  label: string;
  /** Icon hint (e.g. "credit-card", "paypal", "wallet"). */
  iconName: string;
  /** "embedded" renders an in-page SDK; "redirect" opens a hosted checkout. */
  mode: PaymentMethodMode;
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

export async function updateSettingsSection(section: 'profile' | 'goals' | 'notifications' | 'privacy' | 'accessibility' | 'audio' | 'study', values: Record<string, unknown>): Promise<UpdateSettingsSectionResponse> {
  return apiRequest<UpdateSettingsSectionResponse>(`/v1/settings/${section}`, {
    method: 'PATCH',
    body: JSON.stringify({ values }),
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
}

export async function fetchActiveSessions(): Promise<ActiveSession[]> {
  const data = await apiRequest<{ sessions: ActiveSession[] }>('/v1/auth/sessions');
  return Array.isArray(data.sessions) ? data.sessions : [];
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
      explanation: itemReview.explanation ?? (isCorrect ? 'Correct.' : 'Review the transcript clue and distractor pattern.'),
      allowTranscriptReveal: Boolean(transcript?.allowed),
      transcriptExcerpt: transcript?.excerpt ?? undefined,
      distractorExplanation: transcript?.distractorExplanation ?? itemReview.distractorExplanation ?? undefined,
    };
  });

  const recommendedNextDrill = evaluation.recommendedNextDrill ?? {};
  const rawScore = Number(evaluation.rawScore ?? questions.filter((question: ListeningResult['questions'][number]) => question.isCorrect).length);
  const maxRawScore = Number(evaluation.maxRawScore ?? 42);

  return {
    id: taskId,
    title: task.title,
    score: rawScore,
    total: maxRawScore,
    questions,
    recommendedDrill: {
      id: recommendedNextDrill.drillId ?? recommendedNextDrill.id ?? 'listening-drill-detail_capture',
      title: recommendedNextDrill.title ?? 'Exact Detail Capture Drill',
      description: recommendedNextDrill.description ?? recommendedNextDrill.rationale ?? 'Practise the listening error type that appeared most often in this result.',
    },
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
    recommendedDrill: evaluation.recommendedNextDrill
      ? {
          id: evaluation.recommendedNextDrill.drillId ?? evaluation.recommendedNextDrill.id,
          title: evaluation.recommendedNextDrill.title,
          description: evaluation.recommendedNextDrill.description ?? evaluation.recommendedNextDrill.rationale ?? 'Continue with the recommended drill.',
        }
      : undefined,
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
const MOCK_DELIVERY_MODES: ReadonlySet<MockDeliveryMode> = new Set<MockDeliveryMode>([
  'computer', 'paper', 'oet_home',
]);
function normalizeMockDeliveryMode(value: unknown): MockDeliveryMode | undefined {
  const v = typeof value === 'string' ? value.toLowerCase() : '';
  return MOCK_DELIVERY_MODES.has(v as MockDeliveryMode) ? (v as MockDeliveryMode) : undefined;
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

function mapMockBooking(item: ApiRecord): MockBooking {
  return {
    id: String(item.id ?? item.bookingId ?? ''),
    bookingId: String(item.bookingId ?? item.id ?? ''),
    mockBundleId: String(item.mockBundleId ?? ''),
    mockAttemptId: item.mockAttemptId ? String(item.mockAttemptId) : null,
    title: item.title ? String(item.title) : item.mockBundleTitle ? String(item.mockBundleTitle) : undefined,
    scheduledStartAt: String(item.scheduledStartAt ?? ''),
    timezoneIana: String(item.timezoneIana ?? 'UTC'),
    status: String(item.status ?? 'scheduled'),
    deliveryMode: normalizeMockDeliveryMode(item.deliveryMode),
    liveRoomState: item.liveRoomState ? String(item.liveRoomState) : undefined,
    liveRoomTransitionVersion: typeof item.liveRoomTransitionVersion === 'number' ? item.liveRoomTransitionVersion : undefined,
    consentToRecording: Boolean(item.consentToRecording),
    rescheduleCount: Number(item.rescheduleCount ?? 0),
    joinUrl: item.joinUrl ? String(item.joinUrl) : null,
    zoomJoinUrl: item.zoomJoinUrl ? String(item.zoomJoinUrl) : null,
    learnerNotes: item.learnerNotes ? String(item.learnerNotes) : null,
    releasePolicy: item.releasePolicy ? String(item.releasePolicy) : undefined,
    candidateCardVisible: typeof item.candidateCardVisible === 'boolean' ? item.candidateCardVisible : undefined,
    interlocutorCardVisible: typeof item.interlocutorCardVisible === 'boolean' ? item.interlocutorCardVisible : undefined,
    speakingPaperId: typeof item.speakingPaperId === 'string' ? item.speakingPaperId : undefined,
    speakingContent: mapMockSpeakingContent(item.speakingContent),
  };
}

function mapMockSpeakingContent(value: unknown): MockSpeakingContent | null {
  if (!value || typeof value !== 'object') return null;
  const item = asRecord(value);
  const candidateCard = asRecord(item.candidateCard);
  const tasks = toStringArray(candidateCard.tasks).length > 0
    ? toStringArray(candidateCard.tasks)
    : toStringArray(item.tasks);
  return {
    role: typeof item.role === 'string' ? item.role : typeof candidateCard.role === 'string' ? candidateCard.role : undefined,
    setting: typeof item.setting === 'string' ? item.setting : typeof candidateCard.setting === 'string' ? candidateCard.setting : undefined,
    patient: typeof item.patient === 'string' ? item.patient : typeof candidateCard.patient === 'string' ? candidateCard.patient : undefined,
    task: typeof item.task === 'string' ? item.task : typeof candidateCard.task === 'string' ? candidateCard.task : undefined,
    brief: typeof item.brief === 'string' ? item.brief : typeof candidateCard.brief === 'string' ? candidateCard.brief : undefined,
    background: typeof item.background === 'string' ? item.background : typeof candidateCard.background === 'string' ? candidateCard.background : undefined,
    tasks,
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
    roleplayCount: typeof item.roleplayCount === 'number' ? item.roleplayCount : undefined,
    patientEmotion: typeof item.patientEmotion === 'string' ? item.patientEmotion : undefined,
    communicationGoal: typeof item.communicationGoal === 'string' ? item.communicationGoal : undefined,
    clinicalTopic: typeof item.clinicalTopic === 'string' ? item.clinicalTopic : undefined,
    criteriaFocus: toStringArray(item.criteriaFocus),
    disclaimer: typeof item.disclaimer === 'string' ? item.disclaimer : undefined,
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
  const response = await apiRequest<ApiRecord>('/v1/mock-bookings');
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

export type MockLiveRoomTargetState = 'in_progress' | 'completed' | 'tutor_no_show' | 'learner_no_show';

export interface MockLiveRoomTransitionOptions {
  reason?: string;
  clientTransitionId?: string;
}

function mockLiveRoomTransitionPayload(
  targetState: MockLiveRoomTargetState,
  options?: MockLiveRoomTransitionOptions,
) {
  return {
    targetState,
    ...(options?.reason ? { reason: options.reason } : {}),
    ...(options?.clientTransitionId ? { clientTransitionId: options.clientTransitionId } : {}),
  };
}

export async function transitionMockBookingLiveRoom(
  bookingId: string,
  targetState: MockLiveRoomTargetState,
  options?: MockLiveRoomTransitionOptions,
): Promise<MockBooking> {
  const response = await apiRequest<ApiRecord>(`/v1/mock-bookings/${bookingId}/live-room/transition`, {
    method: 'POST',
    body: JSON.stringify(mockLiveRoomTransitionPayload(targetState, options)),
  });
  return mapMockBooking(response);
}

export async function transitionExpertMockBookingLiveRoom(
  bookingId: string,
  targetState: MockLiveRoomTargetState,
  options?: MockLiveRoomTransitionOptions,
): Promise<MockBooking> {
  const response = await apiRequest<ApiRecord>(`/v1/expert/mocks/bookings/${encodeURIComponent(bookingId)}/live-room/transition`, {
    method: 'POST',
    body: JSON.stringify(mockLiveRoomTransitionPayload(targetState, options)),
  });
  return mapMockBooking(response);
}

export async function transitionAdminMockBookingLiveRoom(
  bookingId: string,
  targetState: MockLiveRoomTargetState,
  options?: MockLiveRoomTransitionOptions,
): Promise<MockBooking> {
  const response = await apiRequest<ApiRecord>(`/v1/admin/mock-bookings/${encodeURIComponent(bookingId)}/live-room/transition`, {
    method: 'POST',
    body: JSON.stringify(mockLiveRoomTransitionPayload(targetState, options)),
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
    issues: result.questions.filter((question) => !question.isCorrect).map((question) => question.distractorExplanation ?? question.explanation),
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
    price: `$${Number(summary.price.amount).toFixed(2)}`,
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
    validation: asRecord(quote.validation),
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
}): Promise<{ checkoutUrl: string; checkoutSessionId: string; quoteId?: string | null; totalAmount?: number; currency?: string }> {
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
  return {
    userId: String(data.userId ?? ''),
    flexibleCredits: Number(data.flexibleCredits ?? 0),
    writingOnlyCredits: Number(data.writingOnlyCredits ?? 0),
    speakingOnlyCredits: Number(data.speakingOnlyCredits ?? 0),
    listeningTestsRemaining: data.listeningTestsRemaining == null ? null : Number(data.listeningTestsRemaining),
    readingTestsRemaining: data.readingTestsRemaining == null ? null : Number(data.readingTestsRemaining),
    mockExamsRemaining: Number(data.mockExamsRemaining ?? 0),
    expiresAt: toNullableString(data.expiresAt),
    expiredBecausePassed: Boolean(data.expiredBecausePassed),
    passedAt: toNullableString(data.passedAt),
    transactions: asArray(data.transactions).map((item) => ({
      id: String(item.id ?? ''),
      packageId: toNullableString(item.packageId),
      packageType: toNullableString(item.packageType),
      reason: String(item.reason ?? ''),
      flexibleCreditsDelta: Number(item.flexibleCreditsDelta ?? 0),
      writingOnlyCreditsDelta: Number(item.writingOnlyCreditsDelta ?? 0),
      speakingOnlyCreditsDelta: Number(item.speakingOnlyCreditsDelta ?? 0),
      listeningTestsDelta: Number(item.listeningTestsDelta ?? 0),
      readingTestsDelta: Number(item.readingTestsDelta ?? 0),
      mockExamsDelta: Number(item.mockExamsDelta ?? 0),
      referenceId: toNullableString(item.referenceId),
      description: String(item.description ?? ''),
      expiresAt: toNullableString(item.expiresAt),
      createdAt: String(item.createdAt ?? ''),
    })),
  };
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

export async function fetchExpertOnboardingStatus(): Promise<ExpertOnboardingStatus> {
  return apiRequest<ExpertOnboardingStatus>('/v1/expert/onboarding/status');
}

export async function saveExpertOnboardingProfile(data: ExpertOnboardingProfile): Promise<ExpertOnboardingProfile> {
  return apiRequest<ExpertOnboardingProfile>('/v1/expert/onboarding/profile', {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export async function saveExpertOnboardingQualifications(data: ExpertOnboardingQualifications): Promise<ExpertOnboardingQualifications> {
  return apiRequest<ExpertOnboardingQualifications>('/v1/expert/onboarding/qualifications', {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export async function saveExpertOnboardingRates(data: ExpertOnboardingRates): Promise<ExpertOnboardingRates> {
  return apiRequest<ExpertOnboardingRates>('/v1/expert/onboarding/rates', {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export async function completeExpertOnboarding(): Promise<{ completed: boolean }> {
  return apiRequest<{ completed: boolean }>('/v1/expert/onboarding/complete', {
    method: 'PATCH',
  });
}

export async function fetchReviewQueue(params?: {
  search?: string;
  type?: string[];
  profession?: string[];
  priority?: string[];
  status?: string[];
  confidence?: string[];
  assignment?: string[];
  overdue?: boolean;
  page?: number;
  pageSize?: number;
}): Promise<ReviewQueueResponse> {
  const queryParams = new URLSearchParams();
  if (params?.search?.trim()) queryParams.set('search', params.search.trim());
  if (params?.type?.length) queryParams.set('type', params.type.join(','));
  if (params?.profession?.length) queryParams.set('profession', params.profession.join(','));
  if (params?.priority?.length) queryParams.set('priority', params.priority.join(','));
  if (params?.status?.length) queryParams.set('status', params.status.join(','));
  if (params?.confidence?.length) queryParams.set('confidence', params.confidence.join(','));
  if (params?.assignment?.length) queryParams.set('assignment', params.assignment.join(','));
  if (params?.overdue) queryParams.set('overdue', 'true');
  if (params?.page) queryParams.set('page', String(params.page));
  if (params?.pageSize) queryParams.set('pageSize', String(params.pageSize));
  const query = queryParams.toString();
  return apiRequest<ReviewQueueResponse>(`/v1/expert/queue${query ? `?${query}` : ''}`);
}

export async function fetchExpertQueueFilterMetadata(): Promise<ExpertQueueFilterMetadata> {
  return apiRequest<ExpertQueueFilterMetadata>('/v1/expert/queue/filters/metadata');
}

export async function fetchExpertMetrics(days?: number): Promise<{ metrics: ExpertMetrics; completionData: { day: string; count: number }[]; days: number; generatedAt: string }> {
  const query = days ? `?days=${days}` : '';
  return apiRequest(`/v1/expert/metrics${query}`);
}

export async function fetchExpertSchedule(): Promise<ExpertSchedule> {
  return apiRequest<ExpertSchedule>('/v1/expert/schedule');
}

export async function saveExpertSchedule(schedule: ExpertSchedule): Promise<ExpertSchedule> {
  return apiRequest<ExpertSchedule>('/v1/expert/schedule', {
    method: 'PUT',
    body: JSON.stringify({ timezone: schedule.timezone, days: schedule.days }),
  });
}

export async function fetchScheduleExceptions(from?: string, to?: string): Promise<{ exceptions: ScheduleException[] }> {
  const params = new URLSearchParams();
  if (from) params.set('from', from);
  if (to) params.set('to', to);
  const query = params.toString();
  return apiRequest<{ exceptions: ScheduleException[] }>(`/v1/expert/schedule/exceptions${query ? `?${query}` : ''}`);
}

export async function createScheduleException(data: {
  date: string;
  isBlocked: boolean;
  startTime?: string;
  endTime?: string;
  reason?: string;
}): Promise<ScheduleException> {
  return apiRequest<ScheduleException>('/v1/expert/schedule/exceptions', {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export async function deleteScheduleException(exceptionId: string): Promise<{ deleted: boolean }> {
  return apiRequest<{ deleted: boolean }>(`/v1/expert/schedule/exceptions/${encodeURIComponent(exceptionId)}`, {
    method: 'DELETE',
  });
}

export async function fetchCalibrationCases(): Promise<CalibrationCase[]> {
  return apiRequest<CalibrationCase[]>('/v1/expert/calibration/cases');
}

export async function fetchCalibrationCaseDetail(caseId: string): Promise<CalibrationCaseDetail> {
  return apiRequest<CalibrationCaseDetail>(`/v1/expert/calibration/cases/${encodeURIComponent(caseId)}`);
}

export async function fetchCalibrationNotes(): Promise<CalibrationNote[]> {
  return apiRequest<CalibrationNote[]>('/v1/expert/calibration/notes');
}

export async function fetchExpertLearners(params?: {
  search?: string;
  profession?: string;
  subTest?: string;
  relevance?: string;
  page?: number;
  pageSize?: number;
}): Promise<ExpertLearnerDirectoryResponse> {
  const queryParams = new URLSearchParams();
  if (params?.search?.trim()) queryParams.set('search', params.search.trim());
  if (params?.profession) queryParams.set('profession', params.profession);
  if (params?.subTest) queryParams.set('subTest', params.subTest);
  if (params?.relevance) queryParams.set('relevance', params.relevance);
  if (params?.page) queryParams.set('page', String(params.page));
  if (params?.pageSize) queryParams.set('pageSize', String(params.pageSize));
  const query = queryParams.toString();
  return apiRequest<ExpertLearnerDirectoryResponse>(`/v1/expert/learners${query ? `?${query}` : ''}`);
}

export async function fetchLearnerProfile(learnerId: string): Promise<LearnerProfileExpanded> {
  return apiRequest<LearnerProfileExpanded>(`/v1/expert/learners/${encodeURIComponent(learnerId)}`);
}

export async function fetchExpertLearnerReviewContext(learnerId: string): Promise<ExpertLearnerReviewContext> {
  return apiRequest<ExpertLearnerReviewContext>(`/v1/expert/learners/${encodeURIComponent(learnerId)}/review-context`);
}

export async function fetchWritingReviewDetail(reviewRequestId: string): Promise<WritingReviewDetail> {
  return apiRequest<WritingReviewDetail>(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/writing`);
}

export async function addWritingReviewVoiceNote(reviewRequestId: string, payload: { mediaAssetId: string; durationSeconds?: number | null; transcriptText?: string; writtenNotes?: string; rubricScores?: Record<string, number>; }): Promise<{ reviewRequestId: string; item: ReviewVoiceNote }> {
  return apiRequest(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/writing/voice-notes`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function fetchLearnerReviewVoiceNotes(reviewRequestId: string): Promise<{ reviewRequestId: string; items: ReviewVoiceNote[] }> {
  return apiRequest(`/v1/reviews/requests/${encodeURIComponent(reviewRequestId)}/voice-notes`);
}

// ── Per-criterion voice notes (Phase 7b — VoiceNoteRecorder) ──
//
// The backend voice-note endpoints (writing on ExpertEndpoints, speaking on
// SpeakingReviewVoiceNoteEndpoints) accept JSON referencing a MediaAsset. To
// keep the existing schema intact while still recording the *which-criterion*
// signal, the recorder uploads the audio to /v1/media/upload, then attaches
// the resulting MediaAsset with the criterion code carried through
// `writtenNotes` (human-readable tag) and `rubricJson` (machine-readable, for
// speaking). The shape returned is normalised to a small
// `{ voiceNoteId, url }` envelope the recorder component can consume.
export interface ReviewCriterionVoiceNoteResult {
  voiceNoteId: string;
  url: string;
  mediaAssetId: string;
}

async function uploadReviewCriterionVoiceNote(
  subtest: 'speaking' | 'writing',
  reviewRequestId: string,
  body: { audio: Blob; criterionCode: string; durationMs: number },
): Promise<ReviewCriterionVoiceNoteResult> {
  if (!reviewRequestId) {
    throw new ApiError(400, 'review_request_required', 'Review request id is required.', false);
  }
  if (!body.criterionCode) {
    throw new ApiError(400, 'criterion_required', 'Criterion code is required.', false);
  }
  if (!body.audio || body.audio.size === 0) {
    throw new ApiError(400, 'audio_required', 'A non-empty audio blob is required.', false);
  }

  const inferredType = body.audio.type || 'audio/webm';
  const fileExt = inferredType.includes('webm')
    ? 'webm'
    : inferredType.includes('mp4') || inferredType.includes('m4a')
      ? 'm4a'
      : inferredType.includes('wav')
        ? 'wav'
        : inferredType.includes('ogg')
          ? 'ogg'
          : 'webm';
  const fileName = `voice-note-${subtest}-${body.criterionCode}-${Date.now()}.${fileExt}`;
  const file = body.audio instanceof File ? body.audio : new File([body.audio], fileName, { type: inferredType });

  const uploaded = await uploadMedia(file);
  const durationSeconds = Math.max(0, Math.round(body.durationMs / 1000));
  const writtenNotes = `[criterion:${body.criterionCode}] Voice note (${durationSeconds}s)`;

  if (subtest === 'writing') {
    const response = await addWritingReviewVoiceNote(reviewRequestId, {
      mediaAssetId: uploaded.id,
      durationSeconds,
      writtenNotes,
      // The criterion scores cannot be intuited here; carry only the criterion
      // code via writtenNotes. rubricScores is left undefined so we don't
      // accidentally overwrite the saved draft scores.
    });
    return {
      voiceNoteId: response.item?.id ?? uploaded.id,
      url: response.item?.url ?? uploaded.url,
      mediaAssetId: uploaded.id,
    };
  }

  // Speaking — POST to SpeakingReviewVoiceNoteEndpoints (JSON body).
  const speakingResponse = await apiRequest<{ id: string; mediaAssetId?: string; url?: string }>(
    `/v1/expert/speaking/reviews/${encodeURIComponent(reviewRequestId)}/voice-notes`,
    {
      method: 'POST',
      body: JSON.stringify({
        mediaAssetId: uploaded.id,
        durationSeconds,
        writtenNotes,
        rubricJson: JSON.stringify({ criterionCode: body.criterionCode }),
      }),
    },
  );

  return {
    voiceNoteId: speakingResponse.id ?? uploaded.id,
    url: speakingResponse.url ?? uploaded.url,
    mediaAssetId: uploaded.id,
  };
}

export function uploadSpeakingReviewCriterionVoiceNote(
  reviewRequestId: string,
  body: { audio: Blob; criterionCode: string; durationMs: number },
): Promise<ReviewCriterionVoiceNoteResult> {
  return uploadReviewCriterionVoiceNote('speaking', reviewRequestId, body);
}

export function uploadWritingReviewCriterionVoiceNote(
  reviewRequestId: string,
  body: { audio: Blob; criterionCode: string; durationMs: number },
): Promise<ReviewCriterionVoiceNoteResult> {
  return uploadReviewCriterionVoiceNote('writing', reviewRequestId, body);
}

// ── Writing V2 marking voice note (System A, submission-keyed) ─────────────────
// One overall tutor voice note per writing submission (mock + normal). Distinct
// from uploadWritingReviewCriterionVoiceNote, which posts per-criterion notes to
// the older ReviewRequest-keyed expert flow. The signature matches the
// VoiceNoteRecorder `uploader` prop (criterionCode is accepted but ignored — the
// note is always the overall one).
export interface WritingMarkingVoiceNote {
  id: string;
  submissionId: string;
  mediaAssetId: string;
  url: string;
  durationSeconds: number | null;
  status: string;
  createdAt: string;
}

export async function uploadWritingMarkingVoiceNote(
  submissionId: string,
  body: { audio: Blob; criterionCode?: string; durationMs: number },
): Promise<ReviewCriterionVoiceNoteResult> {
  if (!submissionId) {
    throw new ApiError(400, 'submission_required', 'Submission id is required.', false);
  }
  if (!body.audio || body.audio.size === 0) {
    throw new ApiError(400, 'audio_required', 'A non-empty audio blob is required.', false);
  }

  const inferredType = body.audio.type || 'audio/webm';
  const fileExt = inferredType.includes('webm')
    ? 'webm'
    : inferredType.includes('mp4') || inferredType.includes('m4a')
      ? 'm4a'
      : inferredType.includes('wav')
        ? 'wav'
        : inferredType.includes('ogg')
          ? 'ogg'
          : 'webm';
  const fileName = `voice-note-writing-overall-${Date.now()}.${fileExt}`;
  const file = body.audio instanceof File ? body.audio : new File([body.audio], fileName, { type: inferredType });

  const uploaded = await uploadMedia(file);
  const durationSeconds = Math.max(0, Math.round(body.durationMs / 1000));
  const response = await apiRequest<WritingMarkingVoiceNote>(
    `/v1/writing/tutor/reviews/${encodeURIComponent(submissionId)}/voice-note`,
    {
      method: 'POST',
      body: JSON.stringify({ mediaAssetId: uploaded.id, durationSeconds }),
    },
  );
  return {
    voiceNoteId: response?.id ?? uploaded.id,
    url: response?.url ?? uploaded.url,
    mediaAssetId: uploaded.id,
  };
}

export async function getWritingSubmissionVoiceNote(
  submissionId: string,
): Promise<WritingMarkingVoiceNote | null> {
  return apiRequest<WritingMarkingVoiceNote | null>(
    `/v1/writing/submissions/${encodeURIComponent(submissionId)}/voice-note`,
    { method: 'GET' },
  );
}

interface LearnerReviewResultCriterion {
  code: string;
  name: string;
  score: number;
  maxScore: number;
  explanation: string;
}

interface LearnerReviewResultResponse {
  reviewRequestId: string;
  attemptId: string;
  subtest: string;
  state: string;
  completedAt?: string | null;
  submittedAt?: string | null;
  finalComment: string;
  scoreLabel?: string;
  scores: Record<string, number>;
  criterionComments: Record<string, string>;
  criteria: LearnerReviewResultCriterion[];
}

export async function fetchLearnerReviewResult(reviewRequestId: string): Promise<LearnerReviewResultResponse> {
  return apiRequest(`/v1/reviews/requests/${encodeURIComponent(reviewRequestId)}/result`);
}

export async function fetchSpeakingReviewDetail(reviewRequestId: string): Promise<SpeakingReviewDetail> {
  return apiRequest<SpeakingReviewDetail>(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/speaking`);
}

export async function fetchExpertReviewHistory(reviewRequestId: string): Promise<ExpertReviewHistory> {
  return apiRequest<ExpertReviewHistory>(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/history`);
}

export async function saveDraftReview(draft: ReviewDraft): Promise<ReviewDraft> {
  const comments = Array.isArray(draft.comments) ? draft.comments : [];
  const hasTimestampComments = comments.some((comment) => 'timestampStart' in comment);
  const response = await apiRequest<{
    version: number;
    state: string;
    scores: Record<string, number>;
    criterionComments: Record<string, string>;
    finalComment: string;
    anchoredComments: ReviewDraft['comments'];
    timestampComments: ReviewDraft['comments'];
    scratchpad: string;
    checklistItems: { id: string; label: string; checked: boolean }[];
    savedAt: string;
  }>(`/v1/expert/reviews/${encodeURIComponent(draft.reviewRequestId)}/draft`, {
    method: 'PUT',
    body: JSON.stringify({
      scores: draft.scores,
      criterionComments: draft.criterionComments,
      finalComment: draft.finalComment,
      anchoredComments: hasTimestampComments ? undefined : comments,
      timestampComments: hasTimestampComments ? comments : undefined,
      scratchpad: draft.scratchpad,
      checklistItems: draft.checklistItems,
      version: draft.version,
    }),
  });

  return {
    reviewRequestId: draft.reviewRequestId,
    scores: response.scores,
    criterionComments: response.criterionComments,
    finalComment: response.finalComment,
    comments: hasTimestampComments ? response.timestampComments : response.anchoredComments,
    scratchpad: response.scratchpad,
    checklistItems: response.checklistItems,
    savedAt: response.savedAt,
    version: response.version,
  };
}

export async function submitExpertWritingReview(reviewRequestId: string, payload: { scores: Record<string, number>; criterionComments: Record<string, string>; finalComment: string; version?: number; }): Promise<{ success: boolean; reviewRequestId: string }> {
  return apiRequest(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/writing/submit`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function submitExpertSpeakingReview(reviewRequestId: string, payload: { scores: Record<string, number>; criterionComments: Record<string, string>; finalComment: string; version?: number; }): Promise<{ success: boolean; reviewRequestId: string }> {
  return apiRequest(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/speaking/submit`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function claimReview(reviewRequestId: string): Promise<{ claimed: boolean; reviewRequestId: string }> {
  return apiRequest(`/v1/expert/queue/${encodeURIComponent(reviewRequestId)}/claim`, { method: 'POST' });
}

export async function releaseReview(reviewRequestId: string): Promise<{ released: boolean; reviewRequestId: string }> {
  return apiRequest(`/v1/expert/queue/${encodeURIComponent(reviewRequestId)}/release`, { method: 'POST' });
}

export async function requestRework(reviewRequestId: string, reason: string): Promise<{ success: boolean }> {
  return apiRequest(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/rework`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

export async function submitCalibrationCase(
  caseId: string,
  payload: { scores: Record<string, number>; notes?: string },
): Promise<{ success: boolean; caseId: string; alignment: number }> {
  return apiRequest(`/v1/expert/calibration/cases/${encodeURIComponent(caseId)}/submit`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function saveCalibrationDraft(
  caseId: string,
  payload: { scores: Record<string, number>; notes?: string },
): Promise<{ success: boolean; caseId: string; isDraft: boolean; scores: Record<string, number>; notes: string; updatedAt: string | null }> {
  return apiRequest(`/v1/expert/calibration/cases/${encodeURIComponent(caseId)}/draft`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

// ─── Expert calibration history + alignment (supplement §4.8) ───

export interface ExpertCalibrationHistoryEntry {
  id: string;
  caseId: string;
  caseTitle: string;
  profession: string;
  subTest: string;
  benchmarkScore: number;
  reviewerScore: number;
  alignmentScore: number;
  disagreementSummary: string;
  submittedAt: string;
}

export interface ExpertCalibrationHistory {
  entries: ExpertCalibrationHistoryEntry[];
  totalCount: number;
  generatedAt: string;
}

export interface ExpertCalibrationAlignmentBreakdown {
  subTest: string;
  submissionCount: number;
  averageAlignment: number;
  latestAlignment: number | null;
}

export interface ExpertCalibrationAlignmentTrendPoint {
  submittedAt: string;
  alignmentScore: number;
}

export interface ExpertCalibrationAlignment {
  totalSubmissions: number;
  overallAverageAlignment: number;
  latestAlignment: number | null;
  previousAlignment: number | null;
  deltaFromPrevious: number | null;
  perSubTest: ExpertCalibrationAlignmentBreakdown[];
  trend: ExpertCalibrationAlignmentTrendPoint[];
  generatedAt: string;
}

export async function fetchExpertCalibrationHistory(limit?: number): Promise<ExpertCalibrationHistory> {
  const query = typeof limit === 'number' ? `?limit=${limit}` : '';
  return apiRequest<ExpertCalibrationHistory>(`/v1/expert/calibration/history${query}`);
}

export async function fetchExpertCalibrationAlignment(): Promise<ExpertCalibrationAlignment> {
  return apiRequest<ExpertCalibrationAlignment>('/v1/expert/calibration/alignment');
}

// ─── Expert availability constraints (supplement: GET /v1/expert/availability/constraints) ───

export interface ExpertAvailabilityConstraints {
  minNoticeHours: number;
  maxHoursPerWeek: number;
  maxExceptionsPerMonth: number;
  minSlotDuration: string;
  maxSlotDuration: string;
  supportedTimezones: string[];
  dayKeys: string[];
}

export async function fetchExpertAvailabilityConstraints(): Promise<ExpertAvailabilityConstraints> {
  return apiRequest<ExpertAvailabilityConstraints>('/v1/expert/availability/constraints');
}

// ─── Admin / CMS API ───

// ── Admin Alerts ─────────────────────────────────────

export async function fetchAdminAlerts() {
  return apiRequest('/v1/admin/alerts');
}

// ── Admin Content ─────────────────────────────────────

export async function fetchAdminContent(params?: { type?: string; subtest?: string; profession?: string; status?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.type) qs.set('type', params.type);
  if (params?.subtest) qs.set('subtest', params.subtest);
  if (params?.profession) qs.set('profession', params.profession);
  if (params?.status) qs.set('status', params.status);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/content${q ? `?${q}` : ''}`);
}

export async function fetchAdminDashboard() {
  return apiRequest('/v1/admin/dashboard');
}

export async function fetchAdminContentDetail(contentId: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}`);
}

export async function createAdminContent(payload: { contentType: string; subtestCode: string; professionId: string; title: string; difficulty: string; estimatedDurationMinutes?: number; description?: string; caseNotes?: string; modelAnswer?: string; criteriaFocus?: string; sourceType?: string; qaStatus?: string }) {
  return apiRequest('/v1/admin/content', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminContent(contentId: string, payload: Record<string, any>) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function publishAdminContent(contentId: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/publish`, { method: 'POST' });
}

export async function archiveAdminContent(contentId: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/archive`, { method: 'POST' });
}

export async function fetchAdminContentRevisions(contentId: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/revisions`);
}

export async function restoreAdminContentRevision(contentId: string, revisionId: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/revisions/${encodeURIComponent(revisionId)}/restore`, { method: 'POST' });
}

export async function fetchAdminTaxonomy(params?: { type?: string; status?: string }) {
  const qs = new URLSearchParams();
  if (params?.type) qs.set('type', params.type);
  if (params?.status) qs.set('status', params.status);
  const q = qs.toString();
  return apiRequest(`/v1/admin/taxonomy${q ? `?${q}` : ''}`);
}

export async function createAdminTaxonomy(payload: { code: string; label: string }) {
  return apiRequest('/v1/admin/taxonomy', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminTaxonomy(professionId: string, payload: { label?: string; code?: string; status?: string }) {
  return apiRequest(`/v1/admin/taxonomy/${encodeURIComponent(professionId)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function archiveAdminTaxonomy(professionId: string) {
  return apiRequest(`/v1/admin/taxonomy/${encodeURIComponent(professionId)}/archive`, { method: 'POST' });
}

/** Permanently deletes an archived profession/taxonomy node. system_admin only. */
export async function forceDeleteAdminTaxonomy(professionId: string) {
  return apiRequest(`/v1/admin/taxonomy/${encodeURIComponent(professionId)}/force-delete`, { method: 'POST' });
}

export interface AdminSignupExamTypePayload {
  id: string;
  code: string;
  label: string;
  description?: string;
  sortOrder?: number;
  isActive?: boolean;
}

export interface AdminSignupProfessionPayload {
  id: string;
  label: string;
  description?: string;
  examTypeIds?: string[];
  countryTargets?: string[];
  sortOrder?: number;
  isActive?: boolean;
}

export async function fetchAdminSignupCatalog() {
  return apiRequest('/v1/admin/signup-catalog');
}

export async function createAdminSignupExamType(payload: AdminSignupExamTypePayload) {
  return apiRequest('/v1/admin/signup-catalog/exam-types', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminSignupExamType(id: string, payload: AdminSignupExamTypePayload) {
  return apiRequest(`/v1/admin/signup-catalog/exam-types/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function archiveAdminSignupExamType(id: string) {
  return apiRequest(`/v1/admin/signup-catalog/exam-types/${encodeURIComponent(id)}/archive`, { method: 'POST' });
}

/** Permanently deletes a signup exam-type catalog row. system_admin only. */
export async function forceDeleteAdminSignupExamType(id: string) {
  return apiRequest(`/v1/admin/signup-catalog/exam-types/${encodeURIComponent(id)}/force-delete`, { method: 'POST' });
}

export async function activateAdminSignupExamType(id: string) {
  return apiRequest(`/v1/admin/signup-catalog/exam-types/${encodeURIComponent(id)}/activate`, { method: 'POST' });
}

export async function createAdminSignupProfession(payload: AdminSignupProfessionPayload) {
  return apiRequest('/v1/admin/signup-catalog/professions', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminSignupProfession(id: string, payload: AdminSignupProfessionPayload) {
  return apiRequest(`/v1/admin/signup-catalog/professions/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function archiveAdminSignupProfession(id: string) {
  return apiRequest(`/v1/admin/signup-catalog/professions/${encodeURIComponent(id)}/archive`, { method: 'POST' });
}

/** Permanently deletes a signup profession catalog row. system_admin only. */
export async function forceDeleteAdminSignupProfession(id: string) {
  return apiRequest(`/v1/admin/signup-catalog/professions/${encodeURIComponent(id)}/force-delete`, { method: 'POST' });
}

export async function activateAdminSignupProfession(id: string) {
  return apiRequest(`/v1/admin/signup-catalog/professions/${encodeURIComponent(id)}/activate`, { method: 'POST' });
}

export async function fetchAdminCriteria(params?: { subtest?: string; status?: string }) {
  const qs = new URLSearchParams();
  if (params?.subtest) qs.set('subtest', params.subtest);
  if (params?.status) qs.set('status', params.status);
  const q = qs.toString();
  return apiRequest(`/v1/admin/criteria${q ? `?${q}` : ''}`);
}

export async function createAdminCriterion(payload: { name: string; subtestCode: string; description?: string; weight?: number }) {
  return apiRequest('/v1/admin/criteria', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminCriterion(criterionId: string, payload: { name?: string; description?: string; weight?: number; status?: string }) {
  return apiRequest(`/v1/admin/criteria/${encodeURIComponent(criterionId)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function fetchAdminAIConfig(params?: { status?: string }) {
  const qs = params?.status ? `?status=${encodeURIComponent(params.status)}` : '';
  return apiRequest(`/v1/admin/ai-config${qs}`);
}

export async function createAdminAIConfig(payload: { model: string; provider: string; taskType: string; status?: string; accuracy: number; confidenceThreshold: number; routingRule?: string; experimentFlag?: string; promptLabel?: string }) {
  return apiRequest('/v1/admin/ai-config', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminAIConfig(configId: string, payload: Record<string, any>) {
  return apiRequest(`/v1/admin/ai-config/${encodeURIComponent(configId)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function fetchAdminFlags(params?: { type?: string }) {
  const qs = params?.type ? `?type=${encodeURIComponent(params.type)}` : '';
  return apiRequest(`/v1/admin/flags${qs}`);
}

export async function createAdminFlag(payload: { name: string; key: string; enabled: boolean; flagType?: string; rolloutPercentage?: number; description?: string; owner?: string }) {
  return apiRequest('/v1/admin/flags', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminFlag(flagId: string, payload: Record<string, any>) {
  return apiRequest(`/v1/admin/flags/${encodeURIComponent(flagId)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function fetchAdminAuditLogs(params?: { action?: string; actor?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.action) qs.set('action', params.action);
  if (params?.actor) qs.set('actor', params.actor);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/audit-logs${q ? `?${q}` : ''}`);
}

export async function exportAdminAuditLogs(params?: { action?: string; actor?: string; search?: string }) {
  const qs = new URLSearchParams();
  if (params?.action) qs.set('action', params.action);
  if (params?.actor) qs.set('actor', params.actor);
  if (params?.search) qs.set('search', params.search);

  const path = `/v1/admin/audit-logs/export${qs.toString() ? `?${qs.toString()}` : ''}`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path, undefined, { json: false }),
  });

  if (!response.ok) {
    throw new ApiError(response.status, 'audit_export_failed', 'Failed to export audit logs.', response.status >= 500);
  }

  return {
    blob: await response.blob(),
    fileName: response.headers.get('content-disposition')?.match(/filename="?([^"]+)"?/)?.[1] ?? 'audit-logs.csv',
  };
}

export interface AdminSponsorDto {
  id: string;
  name: string;
  type: string;
  contactEmail: string;
  organizationName?: string;
  status: string;
  createdAt: string;
  learnerCount?: number;
}

export async function fetchAdminSponsors(params?: { status?: string; search?: string; page?: number; pageSize?: number }): Promise<{ items: AdminSponsorDto[]; total?: number; page?: number; pageSize?: number }> {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/sponsors${q ? `?${q}` : ''}`);
}

export async function fetchAdminUsers(params?: { role?: string; status?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.role) qs.set('role', params.role);
  if (params?.status) qs.set('status', params.status);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/users${q ? `?${q}` : ''}`);
}

export async function fetchAdminUserDetail(userId: string) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}`);
}

export async function inviteAdminUser(payload: { name: string; email: string; role: 'learner' | 'expert' | 'admin'; professionId?: string }): Promise<{
  id: string; email: string; role: string;
  temporaryPassword?: string | null;
  invitation?: { purpose: string; deliveryChannel: string; destinationHint: string; expiresAt: string; retryAfterSeconds: number } | null;
}> {
  return apiRequest('/v1/admin/users/invite', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminUserStatus(userId: string, payload: { status: string; reason?: string }) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/status`, { method: 'PUT', body: JSON.stringify(payload) });
}

export interface AdminUserProfileUpdatePayload {
  displayName?: string;
  firstName?: string;
  lastName?: string;
  mobileNumber?: string;
  professionId?: string;
  examTypeId?: string;
  countryTarget?: string;
  timezone?: string;
  locale?: string;
  marketingOptIn?: boolean;
  agreeToTerms?: boolean;
  agreeToPrivacy?: boolean;
  specialties?: string[];
  reason?: string;
}

export async function updateAdminUserProfile(userId: string, payload: AdminUserProfileUpdatePayload) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/profile`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function deleteAdminUser(userId: string, payload?: { reason?: string }) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/delete`, { method: 'POST', body: JSON.stringify(payload ?? {}) });
}

export async function restoreAdminUser(userId: string, payload?: { reason?: string }) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/restore`, { method: 'POST', body: JSON.stringify(payload ?? {}) });
}

/**
 * IRREVERSIBLE: permanently purges the user and every row referencing them across
 * the whole schema — including invoices, payments and audit records. system_admin only.
 */
export async function hardDeleteAdminUser(userId: string): Promise<{ userId: string; purgedRows: number; tables: number }> {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/hard-delete`, { method: 'POST' });
}

export async function adjustAdminUserCredits(userId: string, payload: { amount: number; reason?: string }) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/credits`, { method: 'POST', body: JSON.stringify(payload) });
}

export async function setAdminUserPassword(
  userId: string,
  payload: { password: string },
): Promise<{ userId: string; email: string; revoked: number }> {
  return apiRequest<{ userId: string; email: string; revoked: number }>(`/v1/admin/users/${encodeURIComponent(userId)}/password`, { method: 'POST', body: JSON.stringify(payload) });
}

export async function triggerAdminUserPasswordReset(userId: string) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/password-reset`, { method: 'POST' });
}

export async function revokeAdminUserSessions(userId: string): Promise<{ id: string; revoked: number }> {
  return apiRequest<{ id: string; revoked: number }>(`/v1/admin/users/${encodeURIComponent(userId)}/sessions/revoke`, { method: 'POST' });
}

export async function unlockAdminUser(userId: string) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/unlock`, { method: 'POST' });
}

export async function resendAdminUserInvite(userId: string) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/resend-invite`, { method: 'POST' });
}

export async function bulkImportUsers(file: File) {
  const form = new FormData();
  form.append('file', file);
  const token = await ensureFreshAccessToken();
  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  const response = await fetchWithTimeout(resolveApiUrl('/v1/admin/users/import'), {
    method: 'POST',
    headers,
    body: form,
  }, 120_000);
  if (!response.ok) {
    let code = 'unknown_error';
    let message = `Import failed: ${response.status}`;
    try {
      const error = await response.json();
      code = error.code ?? code;
      message = error.message ?? error.title ?? message;
    } catch { /* ignore parse error */ }
    throw new ApiError(response.status, code, message, false);
  }
  return response.json();
}

export async function fetchAdminBillingPlans(params?: { status?: string }) {
  const qs = params?.status ? `?status=${encodeURIComponent(params.status)}` : '';
  return apiRequest(`/v1/admin/billing/plans${qs}`);
}

export async function fetchAdminBillingPlanVersions(planId: string) {
  return apiRequest(`/v1/admin/billing/plans/${encodeURIComponent(planId)}/versions`);
}

// ── Admin: Wallet Top-Up Tiers (DB-backed CMS) ──
// Mirrors backend AdminWalletTierService. The GET response includes a
// `source` discriminator: "database" when at least one row exists, or
// "appsettings" when the appsettings fallback is being projected.

export interface AdminWalletTierRow {
  id: string | null;
  amount: number;
  credits: number;
  bonus: number;
  totalCredits: number;
  label: string | null;
  isPopular: boolean;
  displayOrder: number;
  isActive: boolean;
  currency: string;
}

export interface AdminWalletTiersResponse {
  source: 'database' | 'appsettings';
  currency: string;
  tiers: AdminWalletTierRow[];
}

export interface AdminWalletTierInput {
  id?: string | null;
  amount: number;
  credits: number;
  bonus: number;
  label?: string | null;
  isPopular: boolean;
  displayOrder: number;
  isActive: boolean;
  currency?: string | null;
}

export async function fetchAdminWalletTiers(): Promise<AdminWalletTiersResponse> {
  return apiRequest<AdminWalletTiersResponse>('/v1/admin/billing/wallet-tiers');
}

export async function replaceAdminWalletTiers(tiers: AdminWalletTierInput[]): Promise<AdminWalletTiersResponse> {
  return apiRequest<AdminWalletTiersResponse>('/v1/admin/billing/wallet-tiers', {
    method: 'PUT',
    body: JSON.stringify({ tiers }),
  });
}


function normalizeBillingCode(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 64) || 'custom-plan';
}

export interface AdminBillingPlanOet2026Fields {
  originalPriceGbp?: number | null;
  accessDurationDays?: number;
  writingAddonsEnabled?: boolean;
  speakingAddonsEnabled?: boolean;
  speakingPracticeAccessEnabled?: boolean;
  tutorBookDiscountEnabled?: boolean;
  profession?: string;
  productCategory?: string;
  dashboardModulesJson?: string;
  bundledWritingAssessments?: number;
  bundledSpeakingSessions?: number;
  bundledAiCredits?: number;
  bundledTutorBook?: boolean;
  bundledBasicEnglish?: boolean;
  isDraft?: boolean;
  extensionAllowed?: boolean;
  recallUpdatesEnabled?: boolean;
  // "What's included" bullet list — persisted on the linked ContentPackage.
  comparisonFeaturesJson?: string;
}

export async function createAdminBillingPlan(payload: {
  code?: string;
  name: string;
  description?: string;
  price: number;
  currency?: string;
  interval: string;
  durationMonths?: number;
  includedCredits?: number;
  displayOrder?: number;
  isVisible?: boolean;
  isRenewable?: boolean;
  trialDays?: number;
  status?: string;
  includedSubtestsJson?: string;
  entitlementsJson?: string;
} & AdminBillingPlanOet2026Fields) {
  return apiRequest('/v1/admin/billing/plans', {
    method: 'POST',
    body: JSON.stringify({
      code: payload.code ?? normalizeBillingCode(payload.name),
      name: payload.name,
      description: payload.description ?? '',
      price: payload.price,
      currency: payload.currency ?? 'AUD',
      interval: payload.interval,
      durationMonths: payload.durationMonths ?? 1,
      includedCredits: payload.includedCredits ?? 0,
      displayOrder: payload.displayOrder ?? 0,
      isVisible: payload.isVisible ?? true,
      isRenewable: payload.isRenewable ?? true,
      trialDays: payload.trialDays ?? 0,
      status: payload.status ?? 'active',
      includedSubtestsJson: payload.includedSubtestsJson ?? '[]',
      entitlementsJson: payload.entitlementsJson ?? '{}',
      originalPriceGbp: payload.originalPriceGbp ?? null,
      accessDurationDays: payload.accessDurationDays ?? null,
      writingAddonsEnabled: payload.writingAddonsEnabled ?? null,
      speakingAddonsEnabled: payload.speakingAddonsEnabled ?? null,
      speakingPracticeAccessEnabled: payload.speakingPracticeAccessEnabled ?? null,
      tutorBookDiscountEnabled: payload.tutorBookDiscountEnabled ?? null,
      profession: payload.profession ?? null,
      productCategory: payload.productCategory ?? null,
      dashboardModulesJson: payload.dashboardModulesJson ?? null,
      bundledWritingAssessments: payload.bundledWritingAssessments ?? null,
      bundledSpeakingSessions: payload.bundledSpeakingSessions ?? null,
      bundledAiCredits: payload.bundledAiCredits ?? null,
      bundledTutorBook: payload.bundledTutorBook ?? null,
      bundledBasicEnglish: payload.bundledBasicEnglish ?? null,
      isDraft: payload.isDraft ?? null,
      extensionAllowed: payload.extensionAllowed ?? null,
      recallUpdatesEnabled: payload.recallUpdatesEnabled ?? null,
      comparisonFeaturesJson: payload.comparisonFeaturesJson ?? null,
    }),
  });
}

export async function updateAdminBillingPlan(planId: string, payload: {
  code: string;
  name: string;
  description?: string;
  price: number;
  currency?: string;
  interval: string;
  durationMonths?: number;
  includedCredits?: number;
  displayOrder?: number;
  isVisible?: boolean;
  isRenewable?: boolean;
  trialDays?: number;
  status?: string;
  includedSubtestsJson?: string;
  entitlementsJson?: string;
} & AdminBillingPlanOet2026Fields) {
  return apiRequest(`/v1/admin/billing/plans/${encodeURIComponent(planId)}`, {
    method: 'PUT',
    body: JSON.stringify({
      code: payload.code,
      name: payload.name,
      description: payload.description ?? '',
      price: payload.price,
      currency: payload.currency ?? 'AUD',
      interval: payload.interval,
      durationMonths: payload.durationMonths ?? 1,
      includedCredits: payload.includedCredits ?? 0,
      displayOrder: payload.displayOrder ?? 0,
      isVisible: payload.isVisible ?? true,
      isRenewable: payload.isRenewable ?? true,
      trialDays: payload.trialDays ?? 0,
      status: payload.status ?? 'active',
      includedSubtestsJson: payload.includedSubtestsJson ?? '[]',
      entitlementsJson: payload.entitlementsJson ?? '{}',
      originalPriceGbp: payload.originalPriceGbp ?? null,
      accessDurationDays: payload.accessDurationDays ?? null,
      writingAddonsEnabled: payload.writingAddonsEnabled ?? null,
      speakingAddonsEnabled: payload.speakingAddonsEnabled ?? null,
      speakingPracticeAccessEnabled: payload.speakingPracticeAccessEnabled ?? null,
      tutorBookDiscountEnabled: payload.tutorBookDiscountEnabled ?? null,
      profession: payload.profession ?? null,
      productCategory: payload.productCategory ?? null,
      dashboardModulesJson: payload.dashboardModulesJson ?? null,
      bundledWritingAssessments: payload.bundledWritingAssessments ?? null,
      bundledSpeakingSessions: payload.bundledSpeakingSessions ?? null,
      bundledAiCredits: payload.bundledAiCredits ?? null,
      bundledTutorBook: payload.bundledTutorBook ?? null,
      bundledBasicEnglish: payload.bundledBasicEnglish ?? null,
      isDraft: payload.isDraft ?? null,
      extensionAllowed: payload.extensionAllowed ?? null,
      recallUpdatesEnabled: payload.recallUpdatesEnabled ?? null,
      comparisonFeaturesJson: payload.comparisonFeaturesJson ?? null,
    }),
  });
}

export async function fetchAdminBillingAddOns(params?: { status?: string }) {
  const qs = params?.status ? `?status=${encodeURIComponent(params.status)}` : '';
  return apiRequest(`/v1/admin/billing/add-ons${qs}`);
}

export async function fetchAdminBillingAddOnVersions(addOnId: string) {
  return apiRequest(`/v1/admin/billing/add-ons/${encodeURIComponent(addOnId)}/versions`);
}

export interface AdminBillingAddOnOet2026Fields {
  originalPriceGbp?: number | null;
  addonKind?: string;
  requiresEligibleParent?: boolean;
  eligibilityFlag?: string;
  lettersGranted?: number;
  sessionsGranted?: number;
  /** AI grading package storefront group: full|listening|reading|writing|speaking|mock. */
  aiPackageGroup?: string;
  /** JSON array of admin-authored AI feature bullet strings. */
  aiFeaturesJson?: string;
}

export async function createAdminBillingAddOn(payload: {
  code?: string;
  name: string;
  description?: string;
  price: number;
  currency?: string;
  interval: string;
  durationDays?: number;
  grantCredits?: number;
  displayOrder?: number;
  isRecurring?: boolean;
  appliesToAllPlans?: boolean;
  isStackable?: boolean;
  quantityStep?: number;
  maxQuantity?: number | null;
  status?: string;
  compatiblePlanCodesJson?: string;
  grantEntitlementsJson?: string;
} & AdminBillingAddOnOet2026Fields) {
  return apiRequest('/v1/admin/billing/add-ons', {
    method: 'POST',
    body: JSON.stringify({
      code: payload.code ?? normalizeBillingCode(payload.name),
      name: payload.name,
      description: payload.description ?? '',
      price: payload.price,
      currency: payload.currency ?? 'AUD',
      interval: payload.interval,
      durationDays: payload.durationDays ?? 0,
      grantCredits: payload.grantCredits ?? 0,
      displayOrder: payload.displayOrder ?? 0,
      isRecurring: payload.isRecurring ?? false,
      appliesToAllPlans: payload.appliesToAllPlans ?? true,
      isStackable: payload.isStackable ?? true,
      quantityStep: payload.quantityStep ?? 1,
      maxQuantity: payload.maxQuantity ?? null,
      status: payload.status ?? 'active',
      compatiblePlanCodesJson: payload.compatiblePlanCodesJson ?? '[]',
      grantEntitlementsJson: payload.grantEntitlementsJson ?? '{}',
      originalPriceGbp: payload.originalPriceGbp ?? null,
      addonKind: payload.addonKind ?? null,
      requiresEligibleParent: payload.requiresEligibleParent ?? null,
      eligibilityFlag: payload.eligibilityFlag ?? null,
      lettersGranted: payload.lettersGranted ?? null,
      sessionsGranted: payload.sessionsGranted ?? null,
      aiPackageGroup: payload.aiPackageGroup ?? null,
      aiFeaturesJson: payload.aiFeaturesJson ?? null,
    }),
  });
}

export async function updateAdminBillingAddOn(addOnId: string, payload: {
  code: string;
  name: string;
  description?: string;
  price: number;
  currency?: string;
  interval: string;
  durationDays?: number;
  grantCredits?: number;
  displayOrder?: number;
  isRecurring?: boolean;
  appliesToAllPlans?: boolean;
  isStackable?: boolean;
  quantityStep?: number;
  maxQuantity?: number | null;
  status?: string;
  compatiblePlanCodesJson?: string;
  grantEntitlementsJson?: string;
} & AdminBillingAddOnOet2026Fields) {
  return apiRequest(`/v1/admin/billing/add-ons/${encodeURIComponent(addOnId)}`, {
    method: 'PUT',
    body: JSON.stringify({
      code: payload.code,
      name: payload.name,
      description: payload.description ?? '',
      price: payload.price,
      currency: payload.currency ?? 'AUD',
      interval: payload.interval,
      durationDays: payload.durationDays ?? 0,
      grantCredits: payload.grantCredits ?? 0,
      displayOrder: payload.displayOrder ?? 0,
      isRecurring: payload.isRecurring ?? false,
      appliesToAllPlans: payload.appliesToAllPlans ?? true,
      isStackable: payload.isStackable ?? true,
      quantityStep: payload.quantityStep ?? 1,
      maxQuantity: payload.maxQuantity ?? null,
      status: payload.status ?? 'active',
      compatiblePlanCodesJson: payload.compatiblePlanCodesJson ?? '[]',
      grantEntitlementsJson: payload.grantEntitlementsJson ?? '{}',
      originalPriceGbp: payload.originalPriceGbp ?? null,
      addonKind: payload.addonKind ?? null,
      requiresEligibleParent: payload.requiresEligibleParent ?? null,
      eligibilityFlag: payload.eligibilityFlag ?? null,
      lettersGranted: payload.lettersGranted ?? null,
      sessionsGranted: payload.sessionsGranted ?? null,
      aiPackageGroup: payload.aiPackageGroup ?? null,
      aiFeaturesJson: payload.aiFeaturesJson ?? null,
    }),
  });
}

// ── Hard-delete (404 + 409 handled by caller). Server returns 409 when
// the plan/add-on still has historical references — caller should fall
// back to archive (PUT status=archived) in that case.

export async function deleteAdminBillingPlan(planId: string): Promise<{ id: string; code: string; deleted: boolean }> {
  return apiRequest(`/v1/admin/billing/plans/${encodeURIComponent(planId)}`, { method: 'DELETE' });
}

export async function deleteAdminBillingAddOn(addOnId: string): Promise<{ id: string; code: string; deleted: boolean }> {
  return apiRequest(`/v1/admin/billing/add-ons/${encodeURIComponent(addOnId)}`, { method: 'DELETE' });
}

// ── Billing page copy (admin-editable learner-page strings) ──────────────

export interface AdminBillingContentEntry {
  key: string;
  value: string;
  section?: string | null;
  description?: string | null;
  updatedAt?: string;
  updatedByAdminName?: string | null;
}

/** Stored copy overrides only — defaults live in lib/billing-copy-defaults.ts. */
export async function fetchAdminBillingContent(): Promise<{ entries: AdminBillingContentEntry[] }> {
  return apiRequest('/v1/admin/billing/content');
}

export async function replaceAdminBillingContent(
  entries: Array<{ key: string; value: string; section?: string; description?: string }>,
): Promise<{ entries: AdminBillingContentEntry[] }> {
  return apiRequest('/v1/admin/billing/content', {
    method: 'PUT',
    body: JSON.stringify({ entries }),
  });
}

export async function deleteAdminBillingContentEntry(key: string): Promise<{ key: string; deleted: boolean }> {
  return apiRequest(`/v1/admin/billing/content/${encodeURIComponent(key)}`, { method: 'DELETE' });
}

/** Public learner-page copy overrides as a flat { key: value } map. */
export async function fetchBillingContent(): Promise<Record<string, string>> {
  return apiRequest('/v1/billing/content') as Promise<Record<string, string>>;
}

// ── OET 2026 catalog API ─────────────────────────────────────────────────

export async function fetchPublicCatalog(): Promise<import('./types/admin').PublicCatalogResponse> {
  return apiRequest('/v1/catalog/pricing');
}

export interface AdminCatalogPresentationResponse {
  planCodes: string[];
  addOnCodes: string[];
  presentation: import('./catalog-presentation').CatalogPresentation | null;
}

export async function fetchAdminCatalogPresentation(): Promise<AdminCatalogPresentationResponse> {
  return apiRequest('/v1/admin/billing/catalog/presentation');
}

export async function saveAdminCatalogPresentation(
  presentation: import('./catalog-presentation').CatalogPresentation | null,
): Promise<void> {
  await apiRequest('/v1/admin/billing/catalog/presentation', {
    method: 'PUT',
    body: JSON.stringify({ presentation }),
  });
}

export async function quoteAddonEligibility(addOnCode: string): Promise<import('./types/admin').AddonQuoteResponse> {
  return apiRequest('/v1/billing/quote/addon', {
    method: 'POST',
    body: JSON.stringify({ addOnCode }),
  });
}

export async function fetchEligibilityMatrix(): Promise<import('./types/admin').EligibilityMatrixResponse> {
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

// ── Tutor Book API ───────────────────────────────────────────────────────

export interface TutorBookAudioScript {
  chapter: string;
  title: string;
  audioUrl: string;
  transcriptUrl?: string | null;
}

export interface TutorBookUpdate {
  id: string;
  title: string;
  bodyMarkdown: string;
  publishedAt: string;
  audience: string;
}

export interface TutorBookTelegramResponse {
  inviteUrl: string | null;
}

export async function fetchTutorBookAudioScripts(): Promise<TutorBookAudioScript[]> {
  return apiRequest('/v1/tutor-book/audio-scripts');
}

export async function fetchTutorBookUpdates(): Promise<TutorBookUpdate[]> {
  return apiRequest('/v1/tutor-book/updates');
}

export async function fetchTutorBookTelegram(): Promise<TutorBookTelegramResponse> {
  return apiRequest('/v1/tutor-book/telegram');
}

/** Returns the URL for the watermarked PDF download (same-origin /api/backend proxy). */
export function tutorBookDownloadUrl(): string {
  // FE-003: reuse the module's resolved API base instead of re-reading env with a
  // wrong-port (5199) localhost fallback. In the browser this is the same-origin
  // `/api/backend` proxy path, so the download is cookie-authenticated and works
  // without depending on NEXT_PUBLIC_API_BASE_URL being set.
  return `${API_BASE_URL.replace(/\/$/, '')}/v1/tutor-book/download`;
}

// ── Admin Tutor Book management ──────────────────────────────────────────

export interface AdminTutorBookUpdate {
  id: string;
  title: string;
  bodyMarkdown: string;
  publishedAt: string;
  audience: string;
  isPublished: boolean;
}

export interface AdminTutorBookAudioScript {
  id: string;
  chapter: string;
  title: string;
  audioUrl: string;
  transcriptUrl?: string | null;
  displayOrder: number;
  isPublished: boolean;
}

export async function adminListTutorBookUpdates(): Promise<AdminTutorBookUpdate[]> {
  return apiRequest('/v1/admin/tutor-book/updates');
}

export async function adminUpsertTutorBookUpdate(payload: {
  id?: string;
  title: string;
  bodyMarkdown: string;
  audience?: string;
  isPublished?: boolean;
  publishedAt?: string | null;
}): Promise<AdminTutorBookUpdate> {
  return apiRequest('/v1/admin/tutor-book/updates', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function adminDeleteTutorBookUpdate(id: string): Promise<void> {
  await apiRequest(`/v1/admin/tutor-book/updates/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

export async function adminListTutorBookAudioScripts(): Promise<AdminTutorBookAudioScript[]> {
  return apiRequest('/v1/admin/tutor-book/audio-scripts');
}

export async function adminUpsertTutorBookAudioScript(payload: {
  id?: string;
  chapter: string;
  title: string;
  audioUrl: string;
  transcriptUrl?: string | null;
  displayOrder: number;
  isPublished?: boolean;
}): Promise<AdminTutorBookAudioScript> {
  return apiRequest('/v1/admin/tutor-book/audio-scripts', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function adminDeleteTutorBookAudioScript(id: string): Promise<void> {
  await apiRequest(`/v1/admin/tutor-book/audio-scripts/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

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

export async function adminCreateSubscription(payload: { userId: string; planCode: string; grantIncludedCredits?: boolean; reason?: string }) {
  return apiRequest('/v1/admin/billing/subscriptions', {
    method: 'POST',
    body: JSON.stringify({
      userId: payload.userId,
      planCode: payload.planCode,
      grantIncludedCredits: payload.grantIncludedCredits ?? false,
      reason: payload.reason ?? null,
    }),
  });
}

export async function adminChangeSubscriptionPlan(subscriptionId: string, payload: { planCode: string; resetRenewalDate?: boolean; grantIncludedCredits?: boolean; reason?: string }) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/change-plan`, {
    method: 'POST',
    body: JSON.stringify({
      planCode: payload.planCode,
      resetRenewalDate: payload.resetRenewalDate ?? true,
      grantIncludedCredits: payload.grantIncludedCredits ?? false,
      reason: payload.reason ?? null,
    }),
  });
}

export async function adminExtendSubscription(subscriptionId: string, payload: { addDays?: number; addMonths?: number; newRenewalAt?: string; reason?: string }) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/extend`, {
    method: 'POST',
    body: JSON.stringify({
      addDays: payload.addDays ?? null,
      addMonths: payload.addMonths ?? null,
      newRenewalAt: payload.newRenewalAt ?? null,
      reason: payload.reason ?? null,
    }),
  });
}

export async function adminCancelSubscription(subscriptionId: string, payload: { immediate?: boolean; reason?: string }) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/cancel`, {
    method: 'POST',
    body: JSON.stringify({
      immediate: payload.immediate ?? false,
      reason: payload.reason ?? null,
    }),
  });
}

export async function adminReactivateSubscription(subscriptionId: string, payload: { resetRenewalDate?: boolean; reason?: string } = {}) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/reactivate`, {
    method: 'POST',
    body: JSON.stringify({
      resetRenewalDate: payload.resetRenewalDate ?? true,
      reason: payload.reason ?? null,
    }),
  });
}

export async function adminSetSubscriptionStatus(subscriptionId: string, payload: { status: string; reason?: string }) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/status`, {
    method: 'POST',
    body: JSON.stringify({
      status: payload.status,
      reason: payload.reason ?? null,
    }),
  });
}

export async function adminApproveSubscriptionFreeze(subscriptionId: string, payload: { reason?: string; internalNotes?: string } = {}) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/approve-freeze`, {
    method: 'POST',
    body: JSON.stringify({
      reason: payload.reason ?? null,
      internalNotes: payload.internalNotes ?? null,
    }),
  });
}

export async function adminRejectSubscriptionFreeze(subscriptionId: string, payload: { reason?: string; internalNotes?: string } = {}) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/reject-freeze`, {
    method: 'POST',
    body: JSON.stringify({
      reason: payload.reason ?? null,
      internalNotes: payload.internalNotes ?? null,
    }),
  });
}

export async function adminFreezeSubscription(subscriptionId: string, payload: { reason?: string; internalNotes?: string } = {}) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/freeze`, {
    method: 'POST',
    body: JSON.stringify({
      reason: payload.reason ?? null,
      internalNotes: payload.internalNotes ?? null,
    }),
  });
}

export async function adminResumeSubscription(subscriptionId: string, payload: { reason?: string; internalNotes?: string } = {}) {
  return apiRequest(`/v1/admin/billing/subscriptions/${encodeURIComponent(subscriptionId)}/resume`, {
    method: 'POST',
    body: JSON.stringify({
      reason: payload.reason ?? null,
      internalNotes: payload.internalNotes ?? null,
    }),
  });
}

export async function fetchAdminBillingEntitlementDiagnostics() {
  return apiRequest('/v1/admin/billing/entitlement-diagnostics');
}

export async function fetchAdminBillingCouponRedemptions(params?: { couponCode?: string; userId?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.couponCode) qs.set('couponCode', params.couponCode);
  if (params?.userId) qs.set('userId', params.userId);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/billing/redemptions${q ? `?${q}` : ''}`);
}

export async function fetchAdminBillingInvoices(params?: { status?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/billing/invoices${q ? `?${q}` : ''}`);
}

export async function fetchAdminBillingInvoiceEvidence(invoiceId: string) {
  return apiRequest(`/v1/admin/billing/invoices/${encodeURIComponent(invoiceId)}/evidence`);
}

export async function fetchAdminBillingPaymentTransactions(params?: { status?: string; gateway?: string; transactionType?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.gateway) qs.set('gateway', params.gateway);
  if (params?.transactionType) qs.set('transactionType', params.transactionType);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/billing/payment-transactions${q ? `?${q}` : ''}`);
}

export async function fetchAdminBillingProviderLifecycleSignals(params?: { gateway?: string; category?: string; processingStatus?: string; verificationStatus?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.gateway) qs.set('gateway', params.gateway);
  if (params?.category) qs.set('category', params.category);
  if (params?.processingStatus) qs.set('processingStatus', params.processingStatus);
  if (params?.verificationStatus) qs.set('verificationStatus', params.verificationStatus);
  if (params?.search) qs.set('search', params.search);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/admin/billing/provider-lifecycle-signals${q ? `?${q}` : ''}`);
}

export async function fetchAdminReviewOpsSummary() {
  return apiRequest('/v1/admin/review-ops/summary');
}

export async function fetchAdminReviewOpsQueue(params?: { status?: string; priority?: string }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.priority) qs.set('priority', params.priority);
  const q = qs.toString();
  return apiRequest(`/v1/admin/review-ops/queue${q ? `?${q}` : ''}`);
}

export async function assignAdminReview(reviewRequestId: string, payload: { expertId: string; reason?: string }) {
  return apiRequest(`/v1/admin/review-ops/${encodeURIComponent(reviewRequestId)}/assign`, { method: 'POST', body: JSON.stringify(payload) });
}

export async function fetchAdminQualityAnalytics(params?: { timeRange?: string; subtest?: string; profession?: string }) {
  const qs = new URLSearchParams();
  if (params?.timeRange) qs.set('timeRange', params.timeRange);
  if (params?.subtest) qs.set('subtest', params.subtest);
  if (params?.profession) qs.set('profession', params.profession);
  const q = qs.toString();
  return apiRequest(`/v1/admin/quality-analytics${q ? `?${q}` : ''}`);
}

export async function fetchAdminCohortAnalysis(params?: { groupBy?: string }) {
  const qs = params?.groupBy ? `?groupBy=${encodeURIComponent(params.groupBy)}` : '';
  return apiRequest(`/v1/admin/analytics/cohort${qs}`);
}

export async function fetchAdminContentEffectiveness(params?: { subtestCode?: string; top?: number }) {
  const qs = new URLSearchParams();
  if (params?.subtestCode) qs.set('subtestCode', params.subtestCode);
  if (params?.top) qs.set('top', String(params.top));
  const q = qs.toString();
  return apiRequest(`/v1/admin/analytics/content-effectiveness${q ? `?${q}` : ''}`);
}

export async function fetchAdminExpertEfficiency(params?: { days?: number }) {
  const qs = params?.days ? `?days=${encodeURIComponent(String(params.days))}` : '';
  return apiRequest(`/v1/admin/analytics/expert-efficiency${qs}`);
}

export async function fetchAdminSubscriptionHealth() {
  return apiRequest('/v1/admin/analytics/subscription-health');
}

export async function bulkAdminContentAction(payload: { action: string; contentIds: string[]; dryRun?: boolean }) {
  return apiRequest('/v1/admin/content/bulk-action', { method: 'POST', body: JSON.stringify(payload) });
}

export async function fetchAdminContentImpact(contentId: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/impact`);
}

export async function fetchAdminTaxonomyImpact(professionId: string) {
  return apiRequest(`/v1/admin/taxonomy/${encodeURIComponent(professionId)}/impact`);
}

export async function activateAdminAIConfig(configId: string) {
  return apiRequest(`/v1/admin/ai-config/${encodeURIComponent(configId)}/activate`, { method: 'POST' });
}

export async function deleteAdminAIConfig(configId: string) {
  return apiRequest(`/v1/admin/ai-config/${encodeURIComponent(configId)}`, { method: 'DELETE' });
}

export async function activateAdminFlag(flagId: string) {
  return apiRequest(`/v1/admin/flags/${encodeURIComponent(flagId)}/activate`, { method: 'POST' });
}

export async function deactivateAdminFlag(flagId: string) {
  return apiRequest(`/v1/admin/flags/${encodeURIComponent(flagId)}/deactivate`, { method: 'POST' });
}

export async function cancelAdminReview(reviewRequestId: string, payload: { reason: string }) {
  return apiRequest(`/v1/admin/review-ops/${encodeURIComponent(reviewRequestId)}/cancel`, { method: 'POST', body: JSON.stringify(payload) });
}

export async function reopenAdminReview(reviewRequestId: string, payload?: { reason?: string }) {
  return apiRequest(`/v1/admin/review-ops/${encodeURIComponent(reviewRequestId)}/reopen`, { method: 'POST', body: JSON.stringify(payload ?? {}) });
}

export async function fetchAdminReviewFailures() {
  return apiRequest('/v1/admin/review-ops/failures');
}

export async function fetchAdminAuditLogDetail(eventId: string) {
  return apiRequest(`/v1/admin/audit-logs/${encodeURIComponent(eventId)}`);
}

export async function fetchFreezeStatus() {
  return apiRequest('/v1/freeze');
}

export async function requestFreeze(payload: {
  startAt?: string | null;
  endAt?: string | null;
  reason?: string | null;
  pauseEntitlementClock?: boolean | null;
}) {
  return apiRequest('/v1/freeze/request', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function confirmFreeze(freezeId: string) {
  return apiRequest(`/v1/freeze/${encodeURIComponent(freezeId)}/confirm`, { method: 'POST' });
}

export async function cancelFreeze(freezeId: string) {
  return apiRequest(`/v1/freeze/${encodeURIComponent(freezeId)}/cancel`, { method: 'POST' });
}

export async function fetchAdminFreezeOverview() {
  return apiRequest('/v1/admin/freeze/overview');
}

export async function updateAdminFreezePolicy(payload: FreezePolicy) {
  return apiRequest('/v1/admin/freeze/policy', {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function createAdminManualFreeze(payload: {
  userId: string;
  startAt?: string | null;
  endAt?: string | null;
  reason?: string | null;
  internalNotes?: string | null;
  pauseEntitlementClock?: boolean | null;
  overrideEligibility?: boolean | null;
}) {
  return apiRequest('/v1/admin/freeze/manual', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function approveAdminFreeze(freezeId: string, payload: { reason?: string | null; internalNotes?: string | null }) {
  return apiRequest(`/v1/admin/freeze/${encodeURIComponent(freezeId)}/approve`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function rejectAdminFreeze(freezeId: string, payload: { reason?: string | null; internalNotes?: string | null }) {
  return apiRequest(`/v1/admin/freeze/${encodeURIComponent(freezeId)}/reject`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function endAdminFreeze(freezeId: string, payload: { reason?: string | null; internalNotes?: string | null }) {
  return apiRequest(`/v1/admin/freeze/${encodeURIComponent(freezeId)}/end`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function forceEndAdminFreeze(freezeId: string, payload: { reason?: string | null; internalNotes?: string | null }) {
  return apiRequest(`/v1/admin/freeze/${encodeURIComponent(freezeId)}/force-end`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

// ── Gamification ─────────────────────────────────────────────────────────────

export async function fetchXP() {
  return apiRequest('/v1/gamification/xp');
}

export async function fetchStreak() {
  return apiRequest('/v1/gamification/streak');
}

export async function fetchLearnerFeatureFlag(featureKey: string) {
  return apiRequest<LearnerFeatureFlag>(`/v1/features/${encodeURIComponent(featureKey)}`);
}

export async function recordActivity() {
  return apiRequest('/v1/gamification/streak/activity', { method: 'POST' });
}

export async function fetchAchievements() {
  return apiRequest('/v1/gamification/achievements');
}

export async function fetchLeaderboard(examTypeCode?: string, period = 'weekly') {
  const params = new URLSearchParams({ period });
  if (examTypeCode) params.set('examTypeCode', examTypeCode);
  return apiRequest(`/v1/gamification/leaderboard?${params}`);
}

export async function fetchMyLeaderboardPosition(examTypeCode?: string, period = 'weekly') {
  const params = new URLSearchParams({ period });
  if (examTypeCode) params.set('examTypeCode', examTypeCode);
  return apiRequest(`/v1/gamification/leaderboard/my-position?${params}`);
}

export async function setLeaderboardOptIn(optedIn: boolean) {
  return apiRequest('/v1/gamification/leaderboard/opt-in', {
    method: 'POST',
    body: JSON.stringify({ optedIn }),
  });
}

// ── Spaced Repetition ─────────────────────────────────────────────────────────

export async function fetchReviewSummary() {
  return apiRequest('/v1/review/summary');
}

export async function fetchDueReviewItems(limit = 20) {
  return apiRequest(`/v1/review/due?limit=${limit}`);
}

export async function createReviewItem(payload: {
  examTypeCode: string;
  sourceType: string;
  sourceId: string;
  subtestCode?: string;
  criterionCode?: string;
  questionJson: string;
  answerJson: string;
}) {
  return apiRequest('/v1/review/items', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function submitReview(itemId: string, quality: number) {
  return apiRequest(`/v1/review/items/${encodeURIComponent(itemId)}/submit`, {
    method: 'POST',
    body: JSON.stringify({ quality }),
  });
}

export async function deleteReviewItem(itemId: string) {
  return apiRequest(`/v1/review/items/${encodeURIComponent(itemId)}`, { method: 'DELETE' });
}

// ── Recalls (unified vocabulary + spaced-repetition) ─────────────────────────
// See docs/RECALLS-MODULE-PLAN.md.

export interface RecallsTodayResponse {
  dueToday: number;
  mastered: number;
  total: number;
  starred: number;
  vocabDueToday: number;
  reviewDueToday: number;
  readinessScore: number;
  weakTopics: { topic: string; total: number; weakCount: number }[];
}

export interface RecallsQueueItem {
  kind: 'vocab' | 'review';
  id: string;
  termId: string | null;
  title: string;
  subtitle: string | null;
  dueDate: string | null;
  starred: boolean;
  starReason: string | null;
  mastery: string;
  ipa: string | null;
  extraJson: string | null;
}

export type RecallsStarReason = 'spelling' | 'pronunciation' | 'meaning' | 'hearing' | 'confused';

export interface RecallsLibraryItem {
  cardId: string;
  termId: string;
  term: string;
  definition: string;
  category: string;
  mastery: string;
  starred: boolean;
  starReason: string | null;
  lastErrorTypeCode: string | null;
  intervalDays: number;
  reviewCount: number;
  correctCount: number;
}

export async function fetchRecallsToday() {
  return apiRequest<RecallsTodayResponse>('/v1/recalls/today');
}

export async function fetchRecallsQueue(limit = 20) {
  return apiRequest<RecallsQueueItem[]>(`/v1/recalls/queue?limit=${limit}`);
}

export async function starRecall(kind: 'vocab' | 'term' | 'review', id: string, starred: boolean, reason?: RecallsStarReason) {
  return apiRequest('/v1/recalls/star', {
    method: 'POST',
    body: JSON.stringify({ kind, id, starred, reason }),
  });
}

export async function fetchRecallsAudio(termId: string, speed: 'normal' | 'slow' | 'sentence' = 'normal') {
  const path = `/v1/recalls/audio/${encodeURIComponent(termId)}?speed=${speed}`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    headers: await getHeaders(path, undefined, { json: false }),
  });

  if (!response.ok) {
    let code = response.status === 401 ? 'not_authenticated' : response.status === 403 ? 'forbidden' : 'unknown_error';
    let message = `Request failed: ${response.status}`;
    try {
      const error = await response.json();
      code = error.code ?? code;
      message = error.message ?? error.title ?? message;
    } catch {
      // Non-JSON error bodies are mapped through the status code above.
    }
    throw new ApiError(response.status, code, message, isRetryable(response.status));
  }

  const blob = await response.blob();
  return {
    url: URL.createObjectURL(blob),
    provider: response.headers.get('x-recalls-tts-provider') ?? 'stream',
  };
}

export async function fetchRecallsLibrary(opts?: { bucket?: 'starred' | 'weak' | 'mastered' | 'new'; topic?: string }) {
  const p = new URLSearchParams();
  if (opts?.bucket) p.set('bucket', opts.bucket);
  if (opts?.topic) p.set('topic', opts.topic);
  const qs = p.toString();
  return apiRequest<{ items: RecallsLibraryItem[] }>(`/v1/recalls/library${qs ? `?${qs}` : ''}`);
}

export interface RecallsBulkUploadRow {
  term: string;
  definition: string;
  exampleSentence?: string;
  category?: string;
  difficulty?: string;
  ipa?: string;
  americanSpelling?: string;
  synonymsCsv?: string;
  examTypeCode?: string;
  professionId?: string;
}

export interface RecallsBulkUploadResult {
  inserted: number;
  updated: number;
  skipped: number;
  errors: string[];
}

export async function adminBulkUploadRecalls(rows: RecallsBulkUploadRow[]) {
  return apiRequest<RecallsBulkUploadResult>('/v1/admin/recalls/bulk-upload', {
    method: 'POST',
    body: JSON.stringify(rows),
  });
}

export interface RecallsWeeklyReport {
  practisedCount: number;
  masteredCount: number;
  spellingAccuracyPct: number;
  weakestTopic: string | null;
  mostCommonErrorCode: string | null;
  mostCommonErrorLabel: string | null;
  averageReviewsPerCard: number;
}

export async function fetchRecallsWeeklyReport() {
  return apiRequest<RecallsWeeklyReport>('/v1/recalls/report/week');
}

export interface RecallsRevisionPlanResponse {
  dueToday: number;
  mastered: number;
  readinessScore: number;
  headline: string;
  steps: string[];
  aiNarrative: string | null;
}

export async function fetchRecallsRevisionPlan() {
  return apiRequest<RecallsRevisionPlanResponse>('/v1/recalls/revision-plan');
}

// ── Vocabulary ────────────────────────────────────────────────────────────────

export async function fetchVocabularyTerms(params?: { examTypeCode?: string; category?: string; profession?: string; search?: string; recallSet?: string; page?: number; pageSize?: number }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.category) p.set('category', params.category);
  if (params?.profession) p.set('profession', params.profession);
  if (params?.search) p.set('search', params.search);
  if (params?.recallSet) p.set('recallSet', params.recallSet);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  return apiRequest(`/v1/vocabulary/terms?${p}`);
}

export async function fetchVocabularyTerm(termId: string) {
  return apiRequest(`/v1/vocabulary/terms/${encodeURIComponent(termId)}`);
}

export async function lookupVocabularyTerm(query: string, examTypeCode = 'oet') {
  const p = new URLSearchParams({ q: query, examTypeCode });
  return apiRequest(`/v1/vocabulary/terms/lookup?${p}`);
}

export async function fetchVocabularyCategories(params?: { examTypeCode?: string; profession?: string }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.profession) p.set('profession', params.profession);
  const qs = p.toString();
  return apiRequest(`/v1/vocabulary/categories${qs ? `?${qs}` : ''}`);
}

/**
 * Recall-set registry (year/source dimension). Returns the canonical 3-set
 * list (`old`, `2023-2025`, `2026`) with live term counts. Stable even when
 * no terms are tagged yet — admin/learner UIs can render the chips eagerly.
 * See `backend/src/OetLearner.Api/Domain/RecallSetCodes.cs`.
 */
export interface RecallSetSummary {
  code: string;
  displayName: string;
  shortLabel: string;
  description: string;
  sortOrder: number;
  termCount: number;
}
export interface RecallSetsResponse {
  examTypeCode: string;
  professionId: string | null;
  sets: RecallSetSummary[];
}
export async function fetchVocabularyRecallSets(params?: { examTypeCode?: string; profession?: string }): Promise<RecallSetsResponse> {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.profession) p.set('profession', params.profession);
  const qs = p.toString();
  return apiRequest(`/v1/vocabulary/recall-sets${qs ? `?${qs}` : ''}`) as Promise<RecallSetsResponse>;
}

export async function fetchVocabularyStats() {
  return apiRequest('/v1/vocabulary/stats');
}

export async function fetchVocabularyDailySet(count = 10) {
  return apiRequest(`/v1/vocabulary/daily-set?count=${count}`);
}

export async function fetchVocabularyQuizHistory(params?: { page?: number; pageSize?: number }) {
  const p = new URLSearchParams();
  p.set('page', String(params?.page ?? 1));
  p.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/vocabulary/quiz/history?${p}`);
}

export async function requestVocabularyGloss(payload: { word: string; context?: string; letterType?: string; profession?: string }) {
  return apiRequest('/v1/vocabulary/gloss', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function fetchMyVocabulary(mastery?: string) {
  const p = mastery ? `?mastery=${mastery}` : '';
  return apiRequest(`/v1/vocabulary/my-list${p}`);
}

export async function addToMyVocabulary(termId: string, opts?: { sourceRef?: string; context?: string }) {
  return apiRequest(`/v1/vocabulary/my-list/${encodeURIComponent(termId)}`, {
    method: 'POST',
    body: JSON.stringify({ sourceRef: opts?.sourceRef, context: opts?.context }),
  });
}

export async function removeFromMyVocabulary(termId: string) {
  return apiRequest(`/v1/vocabulary/my-list/${encodeURIComponent(termId)}`, { method: 'DELETE' });
}

export async function fetchDueFlashcards(limit = 20) {
  return apiRequest(`/v1/vocabulary/flashcards/due?limit=${limit}`);
}

// ── Content Hierarchy: Program Browser (Phase 8) ──

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

// ── Content Browser (access-aware) ──

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

// ── Phase 6: Readiness & Skill-based Content ──

export async function fetchReadinessScore() {
  return apiRequest('/v1/readiness');
}

export async function fetchContentBySkill(subtest: string, skillTag?: string, page?: number, pageSize?: number) {
  const p = new URLSearchParams({ subtest });
  if (skillTag) p.set('skillTag', skillTag);
  if (page) p.set('page', String(page));
  if (pageSize) p.set('pageSize', String(pageSize));
  return apiRequest(`/v1/content/by-skill?${p}`);
}

// ── Phase 9: Search & Recommendations ──

export async function searchContent(params?: {
  q?: string; subtest?: string; profession?: string; difficulty?: string;
  language?: string; provenance?: string; contentType?: string;
  minQuality?: number; mockEligible?: boolean; previewEligible?: boolean;
  page?: number; pageSize?: number;
}) {
  const p = new URLSearchParams();
  if (params?.q) p.set('q', params.q);
  if (params?.subtest) p.set('subtest', params.subtest);
  if (params?.profession) p.set('profession', params.profession);
  if (params?.difficulty) p.set('difficulty', params.difficulty);
  if (params?.language) p.set('language', params.language);
  if (params?.provenance) p.set('provenance', params.provenance);
  if (params?.contentType) p.set('contentType', params.contentType);
  if (params?.minQuality) p.set('minQuality', String(params.minQuality));
  if (params?.mockEligible) p.set('mockEligible', 'true');
  if (params?.previewEligible) p.set('previewEligible', 'true');
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/search${qs ? `?${qs}` : ''}`);
}

export async function fetchSearchFacets() {
  return apiRequest('/v1/search/facets');
}

export async function fetchRecommendations(count?: number) {
  return apiRequest(`/v1/recommendations${count ? `?count=${count}` : ''}`);
}

// ── Phase 11: Media Access ──

export async function fetchSignedMediaUrl(assetId: string) {
  return apiRequest(`/v1/media/${encodeURIComponent(assetId)}/url`);
}

// ── Media Management ──

export interface UploadedMediaAsset {
  id: string;
  originalFilename: string;
  mimeType: string;
  format: string;
  sizeBytes: number;
  status: string;
  uploadedBy: string;
  uploadedAt: string;
  url: string;
}

export async function uploadMedia(file: File): Promise<UploadedMediaAsset> {
  const formData = new FormData();
  formData.append('file', file);
  const response = await fetchWithTimeout(resolveApiUrl('/v1/media/upload'), {
    method: 'POST',
    headers: await getHeaders('/v1/media/upload', undefined, { json: false }),
    body: formData,
  }, 90_000);
  if (!response.ok) {
    let code = 'upload_failed';
    let message = `Upload failed: ${response.status}`;
    try {
      const error = await response.json();
      code = error.code ?? code;
      message = error.message ?? message;
    } catch { /* ignore */ }
    throw new ApiError(response.status, code, message, false);
  }
  return response.json();
}

export async function fetchMediaItem(id: string) {
  return apiRequest(`/v1/media/${encodeURIComponent(id)}`);
}

export async function deleteMedia(id: string) {
  return apiRequest(`/v1/media/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

export async function fetchMyMedia(params?: { page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/media?${qs}`);
}

export async function submitFlashcardReview(lvId: string, quality: number) {
  return apiRequest(`/v1/vocabulary/flashcards/${encodeURIComponent(lvId)}/review`, {
    method: 'POST',
    body: JSON.stringify({ quality }),
  });
}

export async function fetchVocabQuiz(count = 10, format: string = 'definition_match') {
  return apiRequest(`/v1/vocabulary/quiz?count=${count}&format=${encodeURIComponent(format)}`);
}

// ── Admin: Content Hierarchy Management ──

export async function fetchAdminContentPrograms(params?: { type?: string; language?: string; status?: string; page?: number; pageSize?: number }) {
  const p = new URLSearchParams();
  if (params?.type) p.set('type', params.type);
  if (params?.language) p.set('language', params.language);
  if (params?.status) p.set('status', params.status);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/admin/programs${qs ? `?${qs}` : ''}`);
}

export async function fetchAdminContentPackages(params?: { type?: string; status?: string; page?: number; pageSize?: number }) {
  const p = new URLSearchParams();
  if (params?.type) p.set('type', params.type);
  if (params?.status) p.set('status', params.status);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/admin/packages${qs ? `?${qs}` : ''}`);
}

export async function fetchAdminProgram(programId: string) {
  return apiRequest(`/v1/admin/programs/${encodeURIComponent(programId)}`);
}

export async function createAdminProgram(payload: Record<string, unknown>) {
  return apiRequest('/v1/admin/programs', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateAdminProgram(programId: string, payload: Record<string, unknown>) {
  return apiRequest(`/v1/admin/programs/${encodeURIComponent(programId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function fetchAdminTracks(programId: string) {
  return apiRequest(`/v1/admin/programs/${encodeURIComponent(programId)}/tracks`);
}

export async function createAdminTrack(payload: Record<string, unknown>) {
  return apiRequest('/v1/admin/tracks', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateAdminTrack(trackId: string, payload: Record<string, unknown>) {
  return apiRequest(`/v1/admin/tracks/${encodeURIComponent(trackId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function fetchAdminModules(trackId: string) {
  return apiRequest(`/v1/admin/tracks/${encodeURIComponent(trackId)}/modules`);
}

export async function createAdminModule(payload: Record<string, unknown>) {
  return apiRequest('/v1/admin/modules', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateAdminModule(moduleId: string, payload: Record<string, unknown>) {
  return apiRequest(`/v1/admin/modules/${encodeURIComponent(moduleId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function fetchAdminLessons(moduleId: string) {
  return apiRequest(`/v1/admin/modules/${encodeURIComponent(moduleId)}/lessons`);
}

export async function createAdminLesson(payload: Record<string, unknown>) {
  return apiRequest('/v1/admin/lessons', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateAdminLesson(lessonId: string, payload: Record<string, unknown>) {
  return apiRequest(`/v1/admin/lessons/${encodeURIComponent(lessonId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function fetchAdminPackage(packageId: string) {
  return apiRequest(`/v1/admin/packages/${encodeURIComponent(packageId)}`);
}

export async function createAdminPackage(payload: Record<string, unknown>) {
  return apiRequest('/v1/admin/packages', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateAdminPackage(packageId: string, payload: Record<string, unknown>) {
  return apiRequest(`/v1/admin/packages/${encodeURIComponent(packageId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function fetchAdminContentInventory(params?: {
  subtest?: string; profession?: string; language?: string; provenance?: string;
  freshness?: string; qaStatus?: string; status?: string; packageId?: string;
  importBatchId?: string; search?: string; page?: number; pageSize?: number;
}) {
  const p = new URLSearchParams();
  if (params?.subtest) p.set('subtest', params.subtest);
  if (params?.profession) p.set('profession', params.profession);
  if (params?.language) p.set('language', params.language);
  if (params?.provenance) p.set('provenance', params.provenance);
  if (params?.freshness) p.set('freshness', params.freshness);
  if (params?.qaStatus) p.set('qaStatus', params.qaStatus);
  if (params?.status) p.set('status', params.status);
  if (params?.packageId) p.set('packageId', params.packageId);
  if (params?.importBatchId) p.set('importBatchId', params.importBatchId);
  if (params?.search) p.set('search', params.search);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/admin/content/inventory${qs ? `?${qs}` : ''}`);
}

export async function adminBulkImportContent(batchTitle: string, rows: unknown[]) {
  return apiRequest('/v1/admin/content/bulk-import', {
    method: 'POST',
    body: JSON.stringify({ batchTitle, rows }),
  });
}

export async function fetchAdminDedupGroups(page?: number, pageSize?: number) {
  const p = new URLSearchParams();
  if (page) p.set('page', String(page));
  if (pageSize) p.set('pageSize', String(pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/admin/dedup/groups${qs ? `?${qs}` : ''}`);
}

export async function adminDedupScan() {
  return apiRequest('/v1/admin/dedup/scan', { method: 'POST' });
}

export async function adminDesignateCanonical(groupId: string, canonicalItemId: string) {
  return apiRequest(`/v1/admin/dedup/groups/${encodeURIComponent(groupId)}/designate-canonical`, {
    method: 'POST',
    body: JSON.stringify({ canonicalItemId }),
  });
}

export async function fetchAdminMediaAssets(params?: { mimeType?: string; status?: string; page?: number; pageSize?: number }) {
  const p = new URLSearchParams();
  if (params?.mimeType) p.set('mimeType', params.mimeType);
  if (params?.status) p.set('status', params.status);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/admin/media-assets${qs ? `?${qs}` : ''}`);
}

export async function adminProcessMediaAsset(assetId: string) {
  return apiRequest(`/v1/admin/media/${encodeURIComponent(assetId)}/process`, { method: 'POST' });
}

export async function fetchAdminMediaAudit() {
  return apiRequest('/v1/admin/media/audit');
}

export async function adminAssembleMockExam(professionId?: string, language?: string) {
  return apiRequest('/v1/admin/mock/assemble', {
    method: 'POST',
    body: JSON.stringify({ professionId, language }),
  });
}

export async function adminGenerateDiagnostic(professionId?: string) {
  return apiRequest('/v1/admin/diagnostic/generate', {
    method: 'POST',
    body: JSON.stringify({ professionId }),
  });
}

export async function adminUpdateContentEligibility(contentId: string, eligibility: { isMockEligible?: boolean; isDiagnosticEligible?: boolean }) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/eligibility`, {
    method: 'PATCH',
    body: JSON.stringify(eligibility),
  });
}

// ── Admin: Vocabulary Management ──────────────────────────────────────

// Wave 3 of docs/SPEAKING-MODULE-PLAN.md - admin CRUD for speaking
// mock sets. Permissions reuse AdminContent* on the backend.
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

// ── Wave 4 of docs/SPEAKING-MODULE-PLAN.md - tutor calibration drift +
// inline transcript comments. Three audiences:
//   • Admin: CRUD over calibration samples, drift report.
//   • Expert/tutor: list samples, submit rubric, post inline comments.
//   • Learner: read inline comments on their attempt.

export interface SpeakingCriterionRubric {
  intelligibility: number;
  fluency: number;
  appropriateness: number;
  grammarExpression: number;
  relationshipBuilding: number;
  patientPerspective: number;
  structure: number;
  informationGathering: number;
  informationGiving: number;
}

export interface AdminSpeakingCalibrationSampleRow {
  sampleId: string;
  title: string;
  description: string;
  sourceAttemptId: string;
  professionId: string;
  difficulty: string;
  status: 'draft' | 'published' | 'archived';
  goldScores: Partial<SpeakingCriterionRubric>;
  tutorSubmissionCount: number;
  createdAt: string;
  publishedAt: string | null;
}

function mapAdminCalibrationSampleRow(rec: ApiRecord): AdminSpeakingCalibrationSampleRow {
  const status = (typeof rec.status === 'string' ? rec.status : 'draft') as 'draft' | 'published' | 'archived';
  const gold = asRecord(rec.goldScores);
  return {
    sampleId: typeof rec.sampleId === 'string' ? rec.sampleId : '',
    title: typeof rec.title === 'string' ? rec.title : '',
    description: typeof rec.description === 'string' ? rec.description : '',
    sourceAttemptId: typeof rec.sourceAttemptId === 'string' ? rec.sourceAttemptId : '',
    professionId: typeof rec.professionId === 'string' ? rec.professionId : 'nursing',
    difficulty: typeof rec.difficulty === 'string' ? rec.difficulty : 'core',
    status,
    goldScores: Object.fromEntries(
      Object.entries(gold).filter(([, v]) => typeof v === 'number'),
    ) as Partial<SpeakingCriterionRubric>,
    tutorSubmissionCount: typeof rec.tutorSubmissionCount === 'number' ? rec.tutorSubmissionCount : 0,
    createdAt: typeof rec.createdAt === 'string' ? rec.createdAt : '',
    publishedAt: typeof rec.publishedAt === 'string' ? rec.publishedAt : null,
  };
}

export async function fetchAdminSpeakingCalibrationSamples(status?: string): Promise<AdminSpeakingCalibrationSampleRow[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : '';
  const json = await apiRequest<ApiRecord>(`/v1/admin/speaking/calibration/samples${qs}`);
  const items = Array.isArray(json.samples) ? json.samples.map(asRecord) : [];
  return items.map(mapAdminCalibrationSampleRow);
}

export async function createAdminSpeakingCalibrationSample(payload: {
  title: string;
  sourceAttemptId: string;
  goldScores: SpeakingCriterionRubric;
  description?: string;
  professionId?: string;
  difficulty?: string;
  calibrationNotes?: string;
}): Promise<AdminSpeakingCalibrationSampleRow> {
  const json = await apiRequest<ApiRecord>('/v1/admin/speaking/calibration/samples', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
  return mapAdminCalibrationSampleRow(json);
}

export async function publishAdminSpeakingCalibrationSample(sampleId: string): Promise<AdminSpeakingCalibrationSampleRow> {
  const json = await apiRequest<ApiRecord>(
    `/v1/admin/speaking/calibration/samples/${encodeURIComponent(sampleId)}/publish`,
    { method: 'POST' },
  );
  return mapAdminCalibrationSampleRow(json);
}

export async function archiveAdminSpeakingCalibrationSample(sampleId: string): Promise<AdminSpeakingCalibrationSampleRow> {
  const json = await apiRequest<ApiRecord>(
    `/v1/admin/speaking/calibration/samples/${encodeURIComponent(sampleId)}/archive`,
    { method: 'POST' },
  );
  return mapAdminCalibrationSampleRow(json);
}

export interface AdminSpeakingCalibrationDriftRow {
  tutorId: string;
  tutorName: string;
  submissionCount: number;
  meanAbsoluteError: number;
  totalAbsoluteError: number;
  lastSubmittedAt: string;
}

export interface AdminSpeakingCalibrationDriftReport {
  tutors: AdminSpeakingCalibrationDriftRow[];
  sampleSize: number;
  samplesPublished: number;
}

export async function fetchAdminSpeakingCalibrationDrift(minSubmissions = 1): Promise<AdminSpeakingCalibrationDriftReport> {
  const json = await apiRequest<ApiRecord>(`/v1/admin/speaking/calibration/drift?minSubmissions=${minSubmissions}`);
  const tutors = Array.isArray(json.tutors) ? json.tutors.map(asRecord) : [];
  return {
    tutors: tutors.map((t) => ({
      tutorId: typeof t.tutorId === 'string' ? t.tutorId : '',
      tutorName: typeof t.tutorName === 'string' ? t.tutorName : '',
      submissionCount: typeof t.submissionCount === 'number' ? t.submissionCount : 0,
      meanAbsoluteError: typeof t.meanAbsoluteError === 'number' ? t.meanAbsoluteError : 0,
      totalAbsoluteError: typeof t.totalAbsoluteError === 'number' ? t.totalAbsoluteError : 0,
      lastSubmittedAt: typeof t.lastSubmittedAt === 'string' ? t.lastSubmittedAt : '',
    })),
    sampleSize: typeof json.sampleSize === 'number' ? json.sampleSize : 0,
    samplesPublished: typeof json.samplesPublished === 'number' ? json.samplesPublished : 0,
  };
}

// ── Tutor surface ──

export interface TutorSpeakingCalibrationSampleRow {
  sampleId: string;
  title: string;
  description: string;
  sourceAttemptId: string;
  professionId: string;
  difficulty: string;
  publishedAt: string | null;
  submitted: boolean;
  mySubmission: { submittedAt: string; totalAbsoluteError: number } | null;
}

export async function fetchTutorSpeakingCalibrationSamples(): Promise<TutorSpeakingCalibrationSampleRow[]> {
  const json = await apiRequest<ApiRecord>('/v1/expert/calibration/speaking/samples');
  const items = Array.isArray(json.samples) ? json.samples.map(asRecord) : [];
  return items.map((rec) => {
    const mine = rec.mySubmission ? asRecord(rec.mySubmission) : null;
    return {
      sampleId: typeof rec.sampleId === 'string' ? rec.sampleId : '',
      title: typeof rec.title === 'string' ? rec.title : '',
      description: typeof rec.description === 'string' ? rec.description : '',
      sourceAttemptId: typeof rec.sourceAttemptId === 'string' ? rec.sourceAttemptId : '',
      professionId: typeof rec.professionId === 'string' ? rec.professionId : 'nursing',
      difficulty: typeof rec.difficulty === 'string' ? rec.difficulty : 'core',
      publishedAt: typeof rec.publishedAt === 'string' ? rec.publishedAt : null,
      submitted: rec.submitted === true,
      mySubmission: mine
        ? {
            submittedAt: typeof mine.submittedAt === 'string' ? mine.submittedAt : '',
            totalAbsoluteError: typeof mine.totalAbsoluteError === 'number' ? mine.totalAbsoluteError : 0,
          }
        : null,
    };
  });
}

export interface TutorCalibrationSubmissionResult {
  sampleId: string;
  tutorId: string;
  submittedAt: string;
  totalAbsoluteError: number;
  perCriterionDelta: Partial<SpeakingCriterionRubric>;
}

export async function submitTutorSpeakingCalibrationScores(
  sampleId: string,
  scores: SpeakingCriterionRubric,
  notes?: string,
): Promise<TutorCalibrationSubmissionResult> {
  const json = await apiRequest<ApiRecord>(
    `/v1/expert/calibration/speaking/samples/${encodeURIComponent(sampleId)}/scores`,
    { method: 'POST', body: JSON.stringify({ scores, notes }) },
  );
  const delta = asRecord(json.perCriterionDelta);
  return {
    sampleId: typeof json.sampleId === 'string' ? json.sampleId : sampleId,
    tutorId: typeof json.tutorId === 'string' ? json.tutorId : '',
    submittedAt: typeof json.submittedAt === 'string' ? json.submittedAt : '',
    totalAbsoluteError: typeof json.totalAbsoluteError === 'number' ? json.totalAbsoluteError : 0,
    perCriterionDelta: Object.fromEntries(
      Object.entries(delta).filter(([, v]) => typeof v === 'number'),
    ) as Partial<SpeakingCriterionRubric>,
  };
}

// ── Inline transcript comments ──

export interface SpeakingTranscriptComment {
  commentId: string;
  attemptId: string;
  expertId: string;
  transcriptLineIndex: number;
  criterionCode: string;
  body: string;
  createdAt: string;
}

function mapTranscriptComment(rec: ApiRecord): SpeakingTranscriptComment {
  return {
    commentId: typeof rec.commentId === 'string' ? rec.commentId : '',
    attemptId: typeof rec.attemptId === 'string' ? rec.attemptId : '',
    expertId: typeof rec.expertId === 'string' ? rec.expertId : '',
    transcriptLineIndex: typeof rec.transcriptLineIndex === 'number' ? rec.transcriptLineIndex : 0,
    criterionCode: typeof rec.criterionCode === 'string' ? rec.criterionCode : 'general',
    body: typeof rec.body === 'string' ? rec.body : '',
    createdAt: typeof rec.createdAt === 'string' ? rec.createdAt : '',
  };
}

export async function fetchSpeakingTranscriptComments(attemptId: string): Promise<SpeakingTranscriptComment[]> {
  const json = await apiRequest<ApiRecord>(`/v1/speaking/attempts/${encodeURIComponent(attemptId)}/comments`);
  const items = Array.isArray(json.comments) ? json.comments.map(asRecord) : [];
  return items.map(mapTranscriptComment);
}

export async function postExpertSpeakingTranscriptComment(
  attemptId: string,
  payload: { transcriptLineIndex: number; body: string; criterionCode?: string },
): Promise<SpeakingTranscriptComment> {
  const json = await apiRequest<ApiRecord>(
    `/v1/expert/speaking/attempts/${encodeURIComponent(attemptId)}/comments`,
    { method: 'POST', body: JSON.stringify(payload) },
  );
  return mapTranscriptComment(json);
}

// Wave 5 of docs/SPEAKING-MODULE-PLAN.md - deep-link from a speaking
// task into the AI-patient Conversation module. Returns the redirect
// path the caller should navigate the learner to.
export interface SpeakingSelfPracticeStartResult {
  sessionId: string;
  redirectPath: string;
}

export async function startSpeakingSelfPracticeSession(
  taskId: string,
): Promise<SpeakingSelfPracticeStartResult> {
  const json = await apiRequest<ApiRecord>(
    `/v1/speaking/tasks/${encodeURIComponent(taskId)}/self-practice`,
    { method: 'POST', body: JSON.stringify({}) },
  );
  const session = asRecord(json.session);
  const sessionId = typeof session.id === 'string' ? session.id : '';
  const redirectPath = typeof json.redirectPath === 'string' && json.redirectPath
    ? json.redirectPath
    : `/conversation/${sessionId}`;
  return { sessionId, redirectPath };
}

// Wave 6 of docs/SPEAKING-MODULE-PLAN.md - speaking drills bank.
export interface SpeakingDrillRow {
  id: string;
  drillId: string;
  title: string;
  kind: string;
  difficulty: string;
  estimatedDurationMinutes: number;
  professionCode: string | null;
  criteriaFocus: string[];
  caseNotes: string | null;
  completed: boolean;
}

export interface SpeakingDrillsListResponse {
  kinds: string[];
  totalCount: number;
  completedCount: number;
  items: SpeakingDrillRow[];
}

export async function fetchSpeakingDrills(filters?: {
  kind?: string;
  profession?: string;
  criterion?: string;
}): Promise<SpeakingDrillsListResponse> {
  const params = new URLSearchParams();
  if (filters?.kind) params.set('kind', filters.kind);
  if (filters?.profession) params.set('profession', filters.profession);
  if (filters?.criterion) params.set('criterion', filters.criterion);
  const qs = params.toString();
  const json = await apiRequest<ApiRecord>(`/v1/speaking/drills${qs ? `?${qs}` : ''}`);
  const items = Array.isArray(json.items) ? json.items.map(asRecord) : [];
  const kinds = Array.isArray(json.kinds)
    ? json.kinds.filter((k): k is string => typeof k === 'string')
    : [];
  return {
    kinds,
    totalCount: typeof json.totalCount === 'number' ? json.totalCount : items.length,
    completedCount: typeof json.completedCount === 'number' ? json.completedCount : 0,
    items: items.map((row) => ({
      id: typeof row.id === 'string' ? row.id : '',
      drillId: typeof row.drillId === 'string'
        ? row.drillId
        : typeof row.id === 'string'
          ? row.id
          : '',
      title: typeof row.title === 'string' ? row.title : '',
      kind: typeof row.kind === 'string' ? row.kind : 'drill',
      difficulty: typeof row.difficulty === 'string' ? row.difficulty : 'easy',
      estimatedDurationMinutes:
        typeof row.estimatedDurationMinutes === 'number' ? row.estimatedDurationMinutes : 5,
      professionCode: typeof row.professionCode === 'string' ? row.professionCode : null,
      criteriaFocus: Array.isArray(row.criteriaFocus)
        ? row.criteriaFocus.filter((c): c is string => typeof c === 'string')
        : [],
      caseNotes: typeof row.caseNotes === 'string' ? row.caseNotes : null,
      completed: row.completed === true,
    })),
  };
}

export async function fetchAdminVocabularyItems(params?: {
  profession?: string; category?: string; status?: string; search?: string;
  recallSet?: string;
  page?: number; pageSize?: number;
}) {
  const p = new URLSearchParams();
  if (params?.profession) p.set('profession', params.profession);
  if (params?.category) p.set('category', params.category);
  if (params?.status) p.set('status', params.status);
  if (params?.search) p.set('search', params.search);
  if (params?.recallSet) p.set('recallSet', params.recallSet);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/admin/vocabulary/items${qs ? `?${qs}` : ''}`);
}

/**
 * Admin recall-set registry: canonical 3-set list with per-status counts
 * (active / draft / archived / total). See `RecallSetCodes` on the backend.
 */
export interface AdminRecallSetSummary {
  code: string;
  displayName: string;
  shortLabel: string;
  description: string;
  sortOrder: number;
  active: number;
  draft: number;
  archived: number;
  total: number;
}
export async function fetchAdminVocabularyRecallSets(params?: { examTypeCode?: string; professionId?: string }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.professionId) p.set('professionId', params.professionId);
  const qs = p.toString();
  return apiRequest(`/v1/admin/vocabulary/recall-sets${qs ? `?${qs}` : ''}`) as Promise<{
    examTypeCode: string | null;
    professionId: string | null;
    sets: AdminRecallSetSummary[];
  }>;
}

export async function fetchAdminVocabularyItem(itemId: string) {
  return apiRequest(`/v1/admin/vocabulary/items/${encodeURIComponent(itemId)}`);
}

export async function createAdminVocabularyItem(payload: Record<string, unknown>) {
  return apiRequest('/v1/admin/vocabulary/items', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateAdminVocabularyItem(itemId: string, payload: Record<string, unknown>) {
  return apiRequest(`/v1/admin/vocabulary/items/${encodeURIComponent(itemId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function deleteAdminVocabularyItem(itemId: string) {
  return apiRequest(`/v1/admin/vocabulary/items/${encodeURIComponent(itemId)}`, {
    method: 'DELETE',
  });
}

export type AdminVocabularyBulkDeleteResponse = {
  totalRequested: number;
  deleted: number;
  archived: number;
  failed: number;
  errors: string[];
};

export async function deleteAdminVocabularyItems(itemIds: string[]): Promise<AdminVocabularyBulkDeleteResponse> {
  return apiRequest('/v1/admin/vocabulary/items/bulk-delete', {
    method: 'POST',
    body: JSON.stringify({ itemIds }),
  }) as Promise<AdminVocabularyBulkDeleteResponse>;
}

export type AdminVocabularyBulkActivateResponse = {
  totalRequested: number;
  activated: number;
  skipped: number;
  failed: number;
  errors: string[];
};

export async function bulkActivateAdminVocabularyItems(itemIds: string[]): Promise<AdminVocabularyBulkActivateResponse> {
  return apiRequest('/v1/admin/vocabulary/items/bulk-activate', {
    method: 'POST',
    body: JSON.stringify({ itemIds }),
  }) as Promise<AdminVocabularyBulkActivateResponse>;
}

export type AdminVocabularyBulkArchiveResponse = {
  totalRequested: number;
  archived: number;
  skipped: number;
};

export async function bulkArchiveAdminVocabularyItems(itemIds: string[]): Promise<AdminVocabularyBulkArchiveResponse> {
  return apiRequest('/v1/admin/vocabulary/items/bulk-archive', {
    method: 'POST',
    body: JSON.stringify({ itemIds }),
  }) as Promise<AdminVocabularyBulkArchiveResponse>;
}

export type AdminVocabularyBulkDraftResponse = {
  totalRequested: number;
  drafted: number;
  skipped: number;
};

export async function bulkDraftAdminVocabularyItems(itemIds: string[]): Promise<AdminVocabularyBulkDraftResponse> {
  return apiRequest('/v1/admin/vocabulary/items/bulk-draft', {
    method: 'POST',
    body: JSON.stringify({ itemIds }),
  }) as Promise<AdminVocabularyBulkDraftResponse>;
}

export type AdminVocabularyBulkPreviewResponse = {
  totalRequested: number;
  updated: number;
  failed: number;
  freePreviewTotal: number;
  errors: string[];
};

/**
 * Set or clear the free-preview flag on the given vocabulary terms. Free-preview
 * terms are the only Recall Vocabulary Bank terms a non-subscribed learner can
 * access. Admin-curated — no automatic cap.
 */
export async function setAdminVocabularyFreePreview(
  itemIds: string[],
  isFreePreview: boolean,
): Promise<AdminVocabularyBulkPreviewResponse> {
  return apiRequest('/v1/admin/vocabulary/items/bulk-free-preview', {
    method: 'POST',
    body: JSON.stringify({ itemIds, isFreePreview }),
  }) as Promise<AdminVocabularyBulkPreviewResponse>;
}

export type AdminVocabularyAudioProgress = {
  total: number;
  withAudio: number;
  pending: number;
  percentComplete: number;
};

export async function fetchAdminVocabularyAudioProgress(): Promise<AdminVocabularyAudioProgress> {
  // no-store: the progress endpoint must never be served from cache, otherwise
  // the admin panel can pin to a stale "N pending" snapshot forever.
  return apiRequest('/v1/admin/vocabulary/audio/progress', { cache: 'no-store' }) as Promise<AdminVocabularyAudioProgress>;
}

export type AdminVocabularyAudioGenerateResponse = {
  totalRequested: number;
  enqueued: number;
  skipped: number;
  notFound: number;
  dryRun: boolean;
  batchId: string;
};

/**
 * Enqueue ElevenLabs audio synthesis for a set of vocabulary terms.
 * - `forceRegenerate=false` (default): only terms missing/broken audio are queued;
 *   terms that already have working audio are skipped. Used by the bulk
 *   "Auto-generate missing audios" action.
 * - `forceRegenerate=true`: re-synthesise every term, overwriting existing audio.
 *   Used by the per-row "Regenerate audio" button.
 * - `dryRun=true`: return the counts without enqueuing anything (cost preview).
 *
 * The id list is chunked (≤1000) to stay under the server's per-call cap; the
 * per-chunk counts are summed. `batchId` from the first chunk is returned.
 */
export async function generateAdminVocabularyAudio(
  itemIds: string[],
  opts?: { forceRegenerate?: boolean; dryRun?: boolean },
): Promise<AdminVocabularyAudioGenerateResponse> {
  const forceRegenerate = opts?.forceRegenerate ?? false;
  const dryRun = opts?.dryRun ?? false;
  const CHUNK = 1000;
  const agg: AdminVocabularyAudioGenerateResponse = {
    totalRequested: 0, enqueued: 0, skipped: 0, notFound: 0, dryRun, batchId: '',
  };
  for (let i = 0; i < itemIds.length; i += CHUNK) {
    const chunk = itemIds.slice(i, i + CHUNK);
    const res = (await apiRequest('/v1/admin/vocabulary/items/audio/generate', {
      method: 'POST',
      body: JSON.stringify({ itemIds: chunk, forceRegenerate, dryRun }),
    })) as AdminVocabularyAudioGenerateResponse;
    agg.totalRequested += res.totalRequested;
    agg.enqueued += res.enqueued;
    agg.skipped += res.skipped;
    agg.notFound += res.notFound;
    if (!agg.batchId) agg.batchId = res.batchId;
  }
  return agg;
}

export async function fetchAdminVocabularyCategories(params?: { examTypeCode?: string; professionId?: string }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.professionId) p.set('professionId', params.professionId);
  const qs = p.toString();
  return apiRequest(`/v1/admin/vocabulary/categories${qs ? `?${qs}` : ''}`);
}

export async function previewAdminVocabularyImport(file: File, importBatchId?: string, recallSetCode?: string) {
  const formData = new FormData();
  formData.append('file', file);
  const params = new URLSearchParams();
  if (importBatchId) params.set('importBatchId', importBatchId);
  if (recallSetCode) params.set('recallSetCode', recallSetCode);
  const qs = params.toString();
  const response = await fetchWithTimeout(resolveApiUrl(`/v1/admin/vocabulary/import/preview${qs ? `?${qs}` : ''}`), {
    method: 'POST',
    headers: await getHeaders('/v1/admin/vocabulary/import/preview', undefined, { json: false }),
    body: formData,
  }, 60_000);
  if (!response.ok) {
    let code = 'preview_failed';
    let message = `Preview failed: ${response.status}`;
    try {
      const err = await response.json();
      code = err.code ?? err.errorCode ?? code;
      message = err.message ?? err.error ?? message;
    } catch { /* ignore */ }
    throw new ApiError(response.status, code, message, false);
  }
  return response.json();
}

export async function bulkImportAdminVocabulary(file: File, dryRun = true, importBatchId?: string, recallSetCode?: string) {
  const formData = new FormData();
  formData.append('file', file);
  const params = new URLSearchParams({ dryRun: String(dryRun) });
  if (importBatchId) params.set('importBatchId', importBatchId);
  if (recallSetCode) params.set('recallSetCode', recallSetCode);
  const response = await fetchWithTimeout(resolveApiUrl(`/v1/admin/vocabulary/import?${params.toString()}`), {
    method: 'POST',
    headers: await getHeaders('/v1/admin/vocabulary/import', undefined, { json: false }),
    body: formData,
  }, 120_000);
  if (!response.ok) {
    let code = 'import_failed';
    let message = `Import failed: ${response.status}`;
    try {
      const err = await response.json();
      code = err.code ?? err.errorCode ?? code;
      message = err.message ?? err.error ?? message;
    } catch { /* ignore */ }
    throw new ApiError(response.status, code, message, false);
  }
  return response.json();
}

export async function fetchAdminVocabularyImportBatch(importBatchId: string) {
  return apiRequest(`/v1/admin/vocabulary/import/batches/${encodeURIComponent(importBatchId)}`);
}

export async function backfillAdminVocabularyAudio(batchId?: string) {
  const qs = batchId ? `?batchId=${encodeURIComponent(batchId)}` : '';
  return apiRequest(`/v1/admin/vocabulary/audio/backfill${qs}`, { method: 'POST' });
}

export async function cancelAdminVocabularyImportAudio(importBatchId: string) {
  return apiRequest(`/v1/admin/vocabulary/import/batches/${encodeURIComponent(importBatchId)}/audio/cancel`, {
    method: 'POST',
  });
}

export async function resumeAdminVocabularyAudio() {
  return apiRequest(`/v1/admin/vocabulary/audio/resume`, { method: 'POST' });
}

export async function exportAdminVocabularyImportBatchCsv(importBatchId: string) {
  const path = `/v1/admin/vocabulary/import/batches/${encodeURIComponent(importBatchId)}/export`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path),
  }, 60_000);
  if (!response.ok) {
    throw new ApiError(response.status, 'export_failed', `Export failed: ${response.status}`, false);
  }
  return response.blob();
}

export async function reconcileAdminVocabularyImportBatch(importBatchId: string, file: File) {
  const formData = new FormData();
  formData.append('file', file);
  const path = `/v1/admin/vocabulary/import/batches/${encodeURIComponent(importBatchId)}/reconcile`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'POST',
    headers: await getHeaders(path, undefined, { json: false }),
    body: formData,
  }, 120_000);
  if (!response.ok) {
    let code = 'reconcile_failed';
    let message = `Reconciliation failed: ${response.status}`;
    try {
      const err = await response.json();
      code = err.code ?? err.errorCode ?? code;
      message = err.message ?? err.error ?? message;
    } catch { /* ignore */ }
    throw new ApiError(response.status, code, message, false);
  }
  return response.json();
}

export async function rollbackAdminVocabularyImportBatch(importBatchId: string, deleteDraftRows = false) {
  return apiRequest(`/v1/admin/vocabulary/import/batches/${encodeURIComponent(importBatchId)}/rollback`, {
    method: 'POST',
    body: JSON.stringify({ deleteDraftRows }),
  });
}

export async function requestAdminVocabularyAiDraft(payload: {
  count: number;
  examTypeCode: string;
  professionId?: string | null;
  category: string;
  difficulty?: string;
  seedPrompt?: string;
}) {
  return apiRequest('/v1/admin/vocabulary/ai/draft', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function acceptAdminVocabularyAiDrafts(payload: {
  examTypeCode: string;
  professionId?: string | null;
  sourceProvenance: string;
  drafts: Array<Record<string, unknown>>;
}) {
  return apiRequest('/v1/admin/vocabulary/ai/draft/accept', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function submitVocabQuiz(payload: { answers: Array<{ termId: string; correct: boolean; userAnswer?: string }>; durationSeconds: number; format?: string }) {
  return apiRequest('/v1/vocabulary/quiz/submit', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

// ── Adaptive Difficulty ───────────────────────────────────────────────────────

export async function fetchSkillProfile(examTypeCode?: string) {
  const p = examTypeCode ? `?examTypeCode=${examTypeCode}` : '';
  return apiRequest(`/v1/adaptive/skill-profile${p}`);
}

export async function fetchAdaptiveContent(examTypeCode: string, subtestCode: string, count = 5) {
  return apiRequest(`/v1/adaptive/content?examTypeCode=${examTypeCode}&subtestCode=${subtestCode}&count=${count}`);
}

// ── Predictions ───────────────────────────────────────────────────────────────

export async function fetchPredictions(examTypeCode?: string) {
  const p = examTypeCode ? `?examTypeCode=${examTypeCode}` : '';
  return apiRequest(`/v1/predictions${p}`);
}

export async function fetchPrediction(examTypeCode: string, subtestCode: string) {
  return apiRequest(`/v1/predictions/${encodeURIComponent(examTypeCode)}/${encodeURIComponent(subtestCode)}`);
}

export async function requestPredictionComputation(examTypeCode: string, subtestCode: string) {
  return apiRequest('/v1/predictions/compute', {
    method: 'POST',
    body: JSON.stringify({ examTypeCode, subtestCode }),
  });
}

// ── Community ─────────────────────────────────────────────────────────────────

export async function fetchForumCategories(examTypeCode?: string) {
  const p = examTypeCode ? `?examTypeCode=${examTypeCode}` : '';
  return apiRequest(`/v1/community/categories${p}`);
}

export async function fetchForumThreads(categoryId?: string, page = 1, pageSize = 20) {
  const p = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (categoryId) p.set('categoryId', categoryId);
  return apiRequest(`/v1/community/threads?${p}`);
}

export async function fetchAdminCommunityThreads(categoryId?: string, page = 1, pageSize = 20) {
  const p = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (categoryId) p.set('categoryId', categoryId);
  return apiRequest(`/v1/admin/community/threads?${p}`);
}

export async function fetchForumThread(threadId: string) {
  return apiRequest(`/v1/community/threads/${encodeURIComponent(threadId)}`);
}

export async function createForumThread(payload: { categoryId: string; title: string; body: string }) {
  return apiRequest('/v1/community/threads', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function fetchThreadReplies(threadId: string, page = 1, pageSize = 20) {
  return apiRequest(`/v1/community/threads/${encodeURIComponent(threadId)}/replies?page=${page}&pageSize=${pageSize}`);
}

export async function createReply(threadId: string, body: string) {
  return apiRequest(`/v1/community/threads/${encodeURIComponent(threadId)}/replies`, {
    method: 'POST',
    body: JSON.stringify({ body }),
  });
}

export async function fetchStudyGroups(examTypeCode?: string) {
  const p = examTypeCode ? `?examTypeCode=${examTypeCode}` : '';
  return apiRequest(`/v1/community/study-groups${p}`);
}

export async function createStudyGroup(payload: { name: string; description: string; examTypeCode: string; isPublic: boolean }) {
  return apiRequest('/v1/community/study-groups', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function joinStudyGroup(groupId: string) {
  return apiRequest(`/v1/community/study-groups/${encodeURIComponent(groupId)}/join`, { method: 'POST' });
}

// ── Community Moderation (Admin) ──────────────────────────────────────────────

export async function pinCommunityThread(threadId: string, isPinned: boolean) {
  return apiRequest(`/v1/admin/community/threads/${encodeURIComponent(threadId)}/pin`, {
    method: 'PATCH',
    body: JSON.stringify({ isPinned }),
  });
}

export async function lockCommunityThread(threadId: string, isLocked: boolean) {
  return apiRequest(`/v1/admin/community/threads/${encodeURIComponent(threadId)}/lock`, {
    method: 'PATCH',
    body: JSON.stringify({ isLocked }),
  });
}

export async function adminDeleteCommunityThread(threadId: string) {
  return apiRequest(`/v1/admin/community/threads/${encodeURIComponent(threadId)}`, {
    method: 'DELETE',
  });
}

export async function adminDeleteCommunityReply(threadId: string, replyId: string) {
  return apiRequest(`/v1/admin/community/threads/${encodeURIComponent(threadId)}/replies/${encodeURIComponent(replyId)}`, {
    method: 'DELETE',
  });
}

// ── Grammar ───────────────────────────────────────────────────────────────────

const GRAMMAR_PROGRESS_STORAGE_KEY = 'oet-grammar-progress-v1';
const GRAMMAR_DISMISSED_RECOMMENDATIONS_KEY = 'oet-grammar-dismissed-recommendations-v1';
const GRAMMAR_ADMIN_TOPIC_STORAGE_KEY = 'oet-grammar-admin-topics-v1';
const GRAMMAR_ADMIN_LESSON_STORAGE_KEY = 'oet-grammar-admin-lessons-v1';

function readGrammarJsonState<T>(key: string, fallback: T): T {
  if (typeof window === 'undefined') {
    return fallback;
  }

  try {
    const raw = window.localStorage.getItem(key);
    return raw ? JSON.parse(raw) as T : fallback;
  } catch {
    return fallback;
  }
}

function writeGrammarJsonState(key: string, value: unknown) {
  if (typeof window === 'undefined') {
    return;
  }

  try {
    window.localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // best-effort cache only
  }
}

function getGrammarProgressCache(): Record<string, GrammarLessonProgress> {
  return readGrammarJsonState<Record<string, GrammarLessonProgress>>(GRAMMAR_PROGRESS_STORAGE_KEY, {});
}

function saveGrammarProgressCache(cache: Record<string, GrammarLessonProgress>) {
  writeGrammarJsonState(GRAMMAR_PROGRESS_STORAGE_KEY, cache);
}

function getDismissedGrammarRecommendations(): Set<string> {
  return new Set(readGrammarJsonState<string[]>(GRAMMAR_DISMISSED_RECOMMENDATIONS_KEY, []));
}

function markGrammarRecommendationDismissed(id: string) {
  const cache = getDismissedGrammarRecommendations();
  cache.add(id);
  writeGrammarJsonState(GRAMMAR_DISMISSED_RECOMMENDATIONS_KEY, Array.from(cache));
}

function getGrammarTopicCache(): Record<string, ApiRecord> {
  return readGrammarJsonState<Record<string, ApiRecord>>(GRAMMAR_ADMIN_TOPIC_STORAGE_KEY, {});
}

function saveGrammarTopicCache(cache: Record<string, ApiRecord>) {
  writeGrammarJsonState(GRAMMAR_ADMIN_TOPIC_STORAGE_KEY, cache);
}

function getGrammarLessonCache(): Record<string, ApiRecord> {
  return readGrammarJsonState<Record<string, ApiRecord>>(GRAMMAR_ADMIN_LESSON_STORAGE_KEY, {});
}

function saveGrammarLessonCache(cache: Record<string, ApiRecord>) {
  writeGrammarJsonState(GRAMMAR_ADMIN_LESSON_STORAGE_KEY, cache);
}

function normalizeGrammarLevel(value: unknown): GrammarLessonSummary['level'] {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : '';
  if (normalized === 'beginner' || normalized === 'intermediate' || normalized === 'advanced') {
    return normalized;
  }

  return 'intermediate';
}

function normalizeGrammarBackendStatus(value: unknown): string {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : '';
  if (normalized === 'active' || normalized === 'published') return 'published';
  if (normalized === 'draft' || normalized === 'review' || normalized === 'archived') return normalized;
  return normalized || 'draft';
}

function normalizeGrammarRequestStatus(value: unknown): string {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : '';
  if (normalized === 'published' || normalized === 'active') return 'active';
  if (normalized === 'draft' || normalized === 'review' || normalized === 'archived') return normalized;
  return 'draft';
}

function normalizeGrammarTopicSlug(value: unknown): string {
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : 'general';
}

function normalizeGrammarProgressStatus(value: unknown): GrammarLessonProgress['status'] {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : '';
  if (normalized === 'not_started' || normalized === 'in_progress' || normalized === 'completed') {
    return normalized;
  }

  return 'not_started';
}

function extractGrammarLessonProgress(value: unknown): GrammarLessonProgress | null {
  const record = asRecord(value);
  if (Object.keys(record).length === 0) {
    return null;
  }

  const score = typeof record.score === 'number'
    ? record.score
    : typeof record.exerciseScore === 'number'
      ? record.exerciseScore
      : null;

  const masteryScore = typeof record.masteryScore === 'number' ? record.masteryScore : score;

  return {
    status: normalizeGrammarProgressStatus(record.status ?? record.state),
    score,
    masteryScore,
    startedAt: toNullableString(record.startedAt),
    completedAt: toNullableString(record.completedAt),
  };
}

function parseGrammarJson(value: unknown): ApiRecord | ApiRecord[] | null {
  if (typeof value !== 'string') return null;

  const trimmed = value.trim();
  if (!trimmed.startsWith('{') && !trimmed.startsWith('[')) return null;

  try {
    return JSON.parse(trimmed) as ApiRecord | ApiRecord[];
  } catch {
    return null;
  }
}

function toGrammarContentBlocks(value: unknown): GrammarContentBlockLearner[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.map((item, index) => {
    const record = asRecord(item);
    return {
      id: typeof record.id === 'string' && record.id.length > 0 ? record.id : `block-${index + 1}`,
      sortOrder: Number(record.sortOrder ?? index + 1) || index + 1,
      type: typeof record.type === 'string' && record.type.length > 0 ? record.type : 'prose',
      contentMarkdown: typeof record.contentMarkdown === 'string'
        ? record.contentMarkdown
        : typeof record.content === 'string'
          ? record.content
          : '',
    };
  });
}

function toGrammarExerciseOptions(value: unknown, type: string): Array<{ id: string; label: string } | { left: string; right: string }> {
  if (!Array.isArray(value)) {
    return [];
  }

  if (type === 'matching') {
    return value.map((item) => {
      const record = asRecord(item);
      return {
        left: typeof record.left === 'string' ? record.left : '',
        right: typeof record.right === 'string' ? record.right : '',
      };
    });
  }

  return value.map((item, index) => {
    const record = asRecord(item);
    return {
      id: typeof record.id === 'string' && record.id.length > 0 ? record.id : String.fromCharCode('a'.charCodeAt(0) + index),
      label: typeof record.label === 'string' ? record.label : typeof record.text === 'string' ? record.text : '',
    };
  });
}

function toGrammarExercises(value: unknown): GrammarExerciseAuthoring[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.map((item, index) => {
    const record = asRecord(item);
    const type = typeof record.type === 'string' && record.type.length > 0 ? record.type : 'fill_blank';
    const options = toGrammarExerciseOptions(record.options, type);
    const correctAnswer = record.correctAnswer ?? (type === 'matching' ? options : '');
    const acceptedAnswers = toStringArray(record.acceptedAnswers ?? record.acceptedAnswersJson ?? []);

    return {
      id: typeof record.id === 'string' && record.id.length > 0 ? record.id : `exercise-${index + 1}`,
      sortOrder: Number(record.sortOrder ?? index + 1) || index + 1,
      type: type as GrammarExerciseAuthoring['type'],
      promptMarkdown: typeof record.promptMarkdown === 'string'
        ? record.promptMarkdown
        : typeof record.prompt === 'string'
          ? record.prompt
          : '',
      options,
      correctAnswer,
      acceptedAnswers,
      explanationMarkdown: typeof record.explanationMarkdown === 'string'
        ? record.explanationMarkdown
        : typeof record.explanation === 'string'
          ? record.explanation
          : '',
      difficulty: normalizeGrammarLevel(record.difficulty),
      points: Number(record.points ?? 1) || 1,
    };
  });
}

function parseGrammarLessonDocument(content: string | null | undefined, exercisesJson: string | null | undefined): GrammarLessonDocument {
  const parsedContent = parseGrammarJson(content);
  const contentRecord = parsedContent && !Array.isArray(parsedContent) ? parsedContent : null;

  const topicId = typeof contentRecord?.topicId === 'string' && contentRecord.topicId.trim().length > 0
    ? contentRecord.topicId.trim()
    : typeof contentRecord?.topicSlug === 'string' && contentRecord.topicSlug.trim().length > 0
      ? contentRecord.topicSlug.trim()
      : null;

  const category = normalizeGrammarTopicSlug(
    typeof contentRecord?.category === 'string' && contentRecord.category.trim().length > 0
      ? contentRecord.category
      : topicId ?? contentRecord?.slug,
  );

  const rawExercises = contentRecord?.exercises ?? parseGrammarJson(exercisesJson) ?? [];
  const contentBlocks = toGrammarContentBlocks(contentRecord?.contentBlocks ?? contentRecord?.blocks ?? []);

  const fallbackText = typeof content === 'string' && content.trim().length > 0 ? content.trim() : '';
  const blocks = contentBlocks.length > 0
    ? contentBlocks
    : fallbackText
      ? [{ id: 'content', sortOrder: 1, type: 'prose', contentMarkdown: fallbackText }]
      : [];

  return {
    topicId,
    category,
    sourceProvenance: typeof contentRecord?.sourceProvenance === 'string' ? contentRecord.sourceProvenance : '',
    prerequisiteLessonIds: toStringArray(contentRecord?.prerequisiteLessonIds ?? contentRecord?.prerequisiteLessonId ?? []),
    contentBlocks: blocks,
    exercises: toGrammarExercises(rawExercises),
    version: Number(contentRecord?.version ?? 1) || 1,
    updatedAt: typeof contentRecord?.updatedAt === 'string' ? contentRecord.updatedAt : new Date().toISOString(),
  };
}

function stripGrammarExerciseAnswers(exercise: GrammarExerciseAuthoring): GrammarExerciseLearner {
  const { correctAnswer, acceptedAnswers, explanationMarkdown, id, options, type, difficulty, ...rest } = exercise;
  void correctAnswer;
  void acceptedAnswers;
  void explanationMarkdown;
  return {
    ...rest,
    id: id ?? `exercise-${exercise.sortOrder}`,
    type: type as GrammarExerciseLearner['type'],
    options: Array.isArray(options)
      ? (options as GrammarExerciseLearner['options'])
      : [],
    difficulty: difficulty as GrammarExerciseLearner['difficulty'],
  };
}

function toGrammarLessonProgressFromAttempt(attempt: GrammarAttemptResult): GrammarLessonProgress {
  if (attempt.progress) {
    return attempt.progress;
  }

  return {
    status: 'completed',
    score: attempt.score,
    masteryScore: attempt.masteryScore,
    startedAt: null,
    completedAt: new Date().toISOString(),
  };
}

function buildGrammarLessonSummary(raw: ApiRecord, progress?: GrammarLessonProgress | null): GrammarLessonSummary {
  const category = normalizeGrammarTopicSlug(raw.category ?? raw.topicId ?? raw.topicSlug ?? 'general');
  const currentProgress = progress ?? null;
  const masteryScore = currentProgress?.masteryScore ?? currentProgress?.score ?? 0;

  return {
    id: String(raw.id ?? raw.lessonId ?? ''),
    examTypeCode: toExamFamilyCode(raw.examTypeCode ?? raw.profession ?? 'oet'),
    topicId: typeof raw.topicId === 'string' && raw.topicId.length > 0 ? raw.topicId : category,
    topicSlug: category,
    topicName: titleCase(category),
    category,
    title: typeof raw.title === 'string' ? raw.title : '',
    description: toNullableString(raw.description),
    level: normalizeGrammarLevel(raw.level),
    estimatedMinutes: Number(raw.estimatedMinutes ?? raw.estimatedDurationMinutes ?? 0) || 0,
    sortOrder: Number(raw.sortOrder ?? 0) || 0,
    exerciseCount: Number(raw.exerciseCount ?? 0) || 0,
    progress: currentProgress,
    mastered: currentProgress?.status === 'completed' && masteryScore >= 80,
    statusLabel: currentProgress?.status === 'completed'
      ? 'Completed'
      : currentProgress?.status === 'in_progress'
        ? 'In progress'
        : 'New',
  };
}

function deriveGrammarOverviewLessons(rawLessons: ApiRecord[], progressCache: Record<string, GrammarLessonProgress>) {
  return rawLessons.map((lesson) => {
    const lessonId = String(lesson.id ?? lesson.lessonId ?? '');
    const serverProgress = extractGrammarLessonProgress(lesson.progress);
    return buildGrammarLessonSummary(lesson, serverProgress ?? progressCache[lessonId] ?? null);
  });
}

function buildGrammarTopicsFromLessons(lessons: GrammarLessonSummary[], fallbackExamTypeCode: string, topicCache: Record<string, ApiRecord>): GrammarTopicLearner[] {
  const grouped = new Map<string, GrammarLessonSummary[]>();
  lessons.forEach((lesson) => {
    const slug = normalizeGrammarTopicSlug(lesson.topicSlug ?? lesson.category ?? lesson.topicId ?? 'general');
    const list = grouped.get(slug) ?? [];
    list.push(lesson);
    grouped.set(slug, list);
  });

  return Array.from(grouped.entries())
    .map(([slug, list]) => {
      const cached = topicCache[slug] ?? {};
      const topicLessons = list.sort((a, b) => a.sortOrder - b.sortOrder);
      const lessonCount = topicLessons.length;
      const masteredLessonCount = topicLessons.filter((lesson) => lesson.mastered).length;
      const completedLessonCount = topicLessons.filter((lesson) => lesson.progress?.status === 'completed').length;

      return {
        id: typeof cached.id === 'string' && cached.id.length > 0 ? cached.id : slug,
        slug,
        name: typeof cached.name === 'string' && cached.name.length > 0 ? cached.name : titleCase(slug),
        description: toNullableString(cached.description) ?? `Strengthen ${titleCase(slug).toLowerCase()} patterns.`,
        iconEmoji: toNullableString(cached.iconEmoji) ?? '📘',
        levelHint: typeof cached.levelHint === 'string' && cached.levelHint.length > 0 ? cached.levelHint : fallbackExamTypeCode.toUpperCase(),
        lessonCount,
        masteredLessonCount,
        completedLessonCount,
      };
    })
    .sort((a, b) => a.lessonCount - b.lessonCount ? b.lessonCount - a.lessonCount : a.name.localeCompare(b.name));
}

function buildGrammarRecommendations(lessons: GrammarLessonSummary[], dismissed: Set<string>): GrammarRecommendation[] {
  return lessons
    .filter((lesson) => !dismissed.has(lesson.id))
    .filter((lesson) => lesson.progress?.status !== 'completed')
    .slice()
    .sort((a, b) => a.sortOrder - b.sortOrder)
    .slice(0, 3)
    .map((lesson) => ({
      id: `rec-${lesson.id}`,
      lessonId: lesson.id,
      title: lesson.title,
      reason: lesson.progress?.status === 'in_progress'
        ? `Finish your current ${titleCase(lesson.category).toLowerCase()} lesson.`
        : `Build confidence with ${titleCase(lesson.category).toLowerCase()} patterns.`,
      topicSlug: lesson.topicSlug,
      topicName: lesson.topicName,
      level: lesson.level,
      estimatedMinutes: lesson.estimatedMinutes,
      actionLabel: 'Open lesson',
    }));
}

async function fetchGrammarLessonsRaw(params?: { examTypeCode?: string; category?: string; level?: string }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.category) p.set('category', params.category);
  if (params?.level) p.set('level', params.level);
  return apiRequest<ApiRecord[]>(`/v1/grammar/lessons?${p}`);
}

export async function fetchGrammarLessons(params?: { examTypeCode?: string; category?: string; level?: string }) {
  const rawLessons = await fetchGrammarLessonsRaw(params);
  const progressCache = getGrammarProgressCache();

  const lessons = deriveGrammarOverviewLessons(rawLessons, progressCache);

  return Promise.all(
    lessons.map(async (lesson) => {
      try {
        const detail = await fetchGrammarLesson(lesson.id);
        return {
          ...lesson,
          exerciseCount: detail.exercises.length,
          progress: detail.progress ?? lesson.progress ?? null,
          mastered: detail.mastered ?? lesson.mastered,
        };
      } catch {
        return lesson;
      }
    }),
  );
}

export async function fetchGrammarLesson(lessonId: string) {
  const raw = await apiRequest<ApiRecord>(`/v1/grammar/lessons/${encodeURIComponent(lessonId)}`);
  const document = parseGrammarLessonDocument(raw.contentHtml ?? raw.content ?? '', raw.exercisesJson ?? '[]');
  const progress = extractGrammarLessonProgress(raw.progress) ?? getGrammarProgressCache()[lessonId] ?? null;

  const summary = buildGrammarLessonSummary(raw, progress);
  const result = {
    ...summary,
    contentBlocks: document.contentBlocks,
    exercises: document.exercises.map(stripGrammarExerciseAnswers),
    sourceProvenance: document.sourceProvenance || null,
  };

  return result;
}

export async function startGrammarLesson(lessonId: string) {
  const result = await apiRequest(`/v1/grammar/lessons/${encodeURIComponent(lessonId)}/start`, { method: 'POST' });
  const progressCache = getGrammarProgressCache();
  progressCache[lessonId] = {
    status: 'in_progress',
    score: null,
    masteryScore: null,
    startedAt: new Date().toISOString(),
    completedAt: null,
  };
  saveGrammarProgressCache(progressCache);
  return result;
}

export async function submitGrammarAttempt(lessonId: string, answers: Record<string, unknown>) {
  const result = await apiRequest<GrammarAttemptResult>(`/v1/grammar/lessons/${encodeURIComponent(lessonId)}/submit`, {
    method: 'POST',
    body: JSON.stringify({ answersJson: JSON.stringify(answers) }),
  });

  const progressCache = getGrammarProgressCache();
  progressCache[lessonId] = toGrammarLessonProgressFromAttempt(result);
  saveGrammarProgressCache(progressCache);

  return result;
}

export async function completeGrammarLesson(lessonId: string, score: number, answersJson: string) {
  return apiRequest(`/v1/grammar/lessons/${encodeURIComponent(lessonId)}/complete`, {
    method: 'POST',
    body: JSON.stringify({ score, answersJson }),
  });
}

export async function fetchGrammarOverview(examTypeCode = 'oet') {
  const rawLessons = await fetchGrammarLessonsRaw({ examTypeCode });
  const progressCache = getGrammarProgressCache();
  const lessons = deriveGrammarOverviewLessons(rawLessons, progressCache);
  const topicCache = getGrammarTopicCache();
  const dismissed = getDismissedGrammarRecommendations();

  const topics = buildGrammarTopicsFromLessons(lessons, examTypeCode, topicCache);
  const completedLessons = lessons.filter((lesson) => lesson.progress?.status === 'completed');
  const masteredLessons = completedLessons.filter((lesson) => (lesson.progress?.masteryScore ?? lesson.progress?.score ?? 0) >= 80);
  const scores = completedLessons
    .map((lesson) => lesson.progress?.masteryScore ?? lesson.progress?.score ?? 0)
    .filter((score) => typeof score === 'number' && Number.isFinite(score));

  return {
    examTypeCode,
    topics,
    lessonsMastered: masteredLessons.length,
    lessonsCompleted: completedLessons.length,
    lessonsTotal: lessons.length,
    overallMasteryScore: scores.length > 0 ? Math.round(scores.reduce((sum, score) => sum + score, 0) / scores.length) : 0,
    recommendations: buildGrammarRecommendations(lessons, dismissed),
  };
}

export async function fetchGrammarTopicDetail(slug: string) {
  const rawLessons = await fetchGrammarLessonsRaw({ category: slug });
  const progressCache = getGrammarProgressCache();
  const lessons = await fetchGrammarLessons({ category: slug });
  const baseLessons = deriveGrammarOverviewLessons(rawLessons, progressCache);
  const topicCache = getGrammarTopicCache();
  const topicMeta = topicCache[slug] ?? {};
  const sourceLessons = lessons.length > 0 ? lessons : baseLessons;

  return {
    topic: {
      id: typeof topicMeta.id === 'string' && topicMeta.id.length > 0 ? topicMeta.id : slug,
      slug,
      name: typeof topicMeta.name === 'string' && topicMeta.name.length > 0 ? topicMeta.name : titleCase(slug),
      description: toNullableString(topicMeta.description) ?? null,
      iconEmoji: toNullableString(topicMeta.iconEmoji) ?? null,
      levelHint: typeof topicMeta.levelHint === 'string' && topicMeta.levelHint.length > 0 ? topicMeta.levelHint : 'All levels',
    },
    lessons: sourceLessons,
  };
}

export async function dismissGrammarRecommendation(id: string) {
  markGrammarRecommendationDismissed(id);
  return { dismissed: true };
}

function createGrammarLessonPayload(payload: GrammarLessonUpsertPayload) {
  return {
    topicId: payload.topicId,
    category: payload.category,
    sourceProvenance: payload.sourceProvenance,
    prerequisiteLessonIds: payload.prerequisiteLessonIds,
    contentBlocks: payload.contentBlocks,
    exercises: payload.exercises,
    version: 1,
    updatedAt: new Date().toISOString(),
  };
}

function cacheAdminGrammarLesson(lessonId: string, data: ApiRecord) {
  const cache = getGrammarLessonCache();
  cache[lessonId] = data;
  saveGrammarLessonCache(cache);
}

function cacheAdminGrammarTopic(topic: AdminGrammarTopic) {
  const cache = getGrammarTopicCache();
  cache[topic.slug] = topic as ApiRecord;
  saveGrammarTopicCache(cache);
}

function buildGrammarTopicFromLessons(slug: string, lessons: GrammarLessonSummary[], examTypeCode: string): AdminGrammarTopic {
  const topicCache = getGrammarTopicCache();
  const cached = topicCache[slug] ?? {};
  return {
    id: typeof cached.id === 'string' && cached.id.length > 0 ? cached.id : slug,
    slug,
    name: typeof cached.name === 'string' && cached.name.length > 0 ? cached.name : titleCase(slug),
    description: toNullableString(cached.description) ?? null,
    iconEmoji: toNullableString(cached.iconEmoji) ?? '📘',
    levelHint: typeof cached.levelHint === 'string' && cached.levelHint.length > 0 ? cached.levelHint : 'All levels',
    lessonCount: lessons.length,
    masteredLessonCount: lessons.filter((lesson) => lesson.mastered).length,
    completedLessonCount: lessons.filter((lesson) => lesson.progress?.status === 'completed').length,
    examTypeCode,
    status: normalizeGrammarBackendStatus(cached.status ?? 'published'),
    sortOrder: Number(cached.sortOrder ?? 0) || 0,
    createdAt: typeof cached.createdAt === 'string' ? cached.createdAt : null,
    updatedAt: typeof cached.updatedAt === 'string' ? cached.updatedAt : null,
  };
}

export async function adminListGrammarTopics(params?: { examTypeCode?: string }) {
  const lessons = await adminListGrammarLessonsV2({ examTypeCode: params?.examTypeCode ?? 'oet', pageSize: 500 });
  const topicMap = new Map<string, GrammarLessonSummary[]>();

  lessons.items.forEach((lesson) => {
    const slug = normalizeGrammarTopicSlug(lesson.topicSlug ?? lesson.category ?? lesson.topicId ?? 'general');
    const list = topicMap.get(slug) ?? [];
    list.push(lesson);
    topicMap.set(slug, list);
  });

  const topics = Array.from(topicMap.entries()).map(([slug, groupedLessons]) => buildGrammarTopicFromLessons(slug, groupedLessons, params?.examTypeCode ?? 'oet'));
  const cachedTopics = Object.values(getGrammarTopicCache()).map((topic) => {
    const record = topic as ApiRecord;
    return {
      id: typeof record.id === 'string' && record.id.length > 0 ? record.id : normalizeGrammarTopicSlug(record.slug ?? record.name),
      slug: normalizeGrammarTopicSlug(record.slug ?? record.id ?? record.name),
      name: typeof record.name === 'string' && record.name.length > 0 ? record.name : titleCase(normalizeGrammarTopicSlug(record.slug ?? record.id ?? record.name)),
      description: toNullableString(record.description) ?? null,
      iconEmoji: toNullableString(record.iconEmoji) ?? '📘',
      levelHint: typeof record.levelHint === 'string' && record.levelHint.length > 0 ? record.levelHint : 'All levels',
      lessonCount: Number(record.lessonCount ?? 0) || 0,
      masteredLessonCount: Number(record.masteredLessonCount ?? 0) || 0,
      completedLessonCount: Number(record.completedLessonCount ?? 0) || 0,
      examTypeCode: typeof record.examTypeCode === 'string' && record.examTypeCode.length > 0 ? record.examTypeCode : params?.examTypeCode ?? 'oet',
      status: normalizeGrammarBackendStatus(record.status ?? 'published'),
      sortOrder: Number(record.sortOrder ?? 0) || 0,
      createdAt: typeof record.createdAt === 'string' ? record.createdAt : null,
      updatedAt: typeof record.updatedAt === 'string' ? record.updatedAt : null,
    } as AdminGrammarTopic;
  });

  const merged = new Map<string, AdminGrammarTopic>();
  [...topics, ...cachedTopics].forEach((topic) => {
    merged.set(topic.slug, topic);
  });

  return Array.from(merged.values()).filter((topic) => !params?.examTypeCode || topic.examTypeCode === params.examTypeCode);
}

export async function adminCreateGrammarTopic(payload: GrammarTopicUpsertPayload) {
  const topic: AdminGrammarTopic = {
    id: payload.slug,
    slug: payload.slug,
    name: payload.name,
    description: payload.description,
    iconEmoji: payload.iconEmoji ?? '📘',
    levelHint: payload.levelHint,
    lessonCount: 0,
    masteredLessonCount: 0,
    completedLessonCount: 0,
    examTypeCode: payload.examTypeCode,
    status: normalizeGrammarBackendStatus(payload.status ?? 'published'),
    sortOrder: payload.sortOrder,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  };
  cacheAdminGrammarTopic(topic);
  return topic;
}

export async function adminUpdateGrammarTopic(topicId: string, patch: Partial<GrammarTopicUpsertPayload & { status: string }>) {
  const cache = getGrammarTopicCache();
  const existing = cache[topicId] ?? cache[normalizeGrammarTopicSlug(topicId)] ?? {};
  const slug = normalizeGrammarTopicSlug(existing.slug ?? topicId);
  const updated: AdminGrammarTopic = {
    id: typeof existing.id === 'string' && existing.id.length > 0 ? existing.id : slug,
    slug,
    name: typeof patch.name === 'string' ? patch.name : typeof existing.name === 'string' ? existing.name : titleCase(slug),
    description: patch.description === undefined ? toNullableString(existing.description) ?? null : patch.description,
    iconEmoji: patch.iconEmoji === undefined ? toNullableString(existing.iconEmoji) ?? '📘' : patch.iconEmoji,
    levelHint: typeof patch.levelHint === 'string' ? patch.levelHint : typeof existing.levelHint === 'string' ? existing.levelHint : 'All levels',
    lessonCount: Number(existing.lessonCount ?? 0) || 0,
    masteredLessonCount: Number(existing.masteredLessonCount ?? 0) || 0,
    completedLessonCount: Number(existing.completedLessonCount ?? 0) || 0,
    examTypeCode: typeof patch.examTypeCode === 'string' ? patch.examTypeCode : typeof existing.examTypeCode === 'string' ? existing.examTypeCode : 'oet',
    status: normalizeGrammarBackendStatus(patch.status ?? existing.status ?? 'published'),
    sortOrder: typeof patch.sortOrder === 'number' ? patch.sortOrder : Number(existing.sortOrder ?? 0) || 0,
    createdAt: typeof existing.createdAt === 'string' ? existing.createdAt : new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  };
  cache[slug] = updated as ApiRecord;
  saveGrammarTopicCache(cache);
  return updated;
}

export async function adminArchiveGrammarTopic(topicId: string) {
  return adminUpdateGrammarTopic(topicId, { status: 'archived' });
}

export async function adminListGrammarLessonsV2(params?: { examTypeCode?: string; status?: string; topicId?: string; search?: string; page?: number; pageSize?: number }) {
  const query = new URLSearchParams();
  if (params?.examTypeCode) query.set('profession', params.examTypeCode);
  if (params?.status) query.set('status', normalizeGrammarRequestStatus(params.status));
  if (params?.search) query.set('search', params.search);
  query.set('page', String(params?.page ?? 1));
  query.set('pageSize', String(params?.pageSize ?? 20));

  const response = await apiRequest<{ total: number; page: number; pageSize: number; items: ApiRecord[] }>(`/v1/admin/grammar/lessons?${query}`);
  const cache = getGrammarLessonCache();

  const items: AdminGrammarLessonRow[] = (response.items ?? []).map((item) => {
    const category = normalizeGrammarTopicSlug(item.category ?? item.Category ?? 'general');
    const id = String(item.id ?? item.Id ?? '');
    const cached = cache[id] ?? {};
    const updatedAt = typeof cached.updatedAt === 'string'
      ? cached.updatedAt
      : typeof cached.updated_at === 'string'
        ? cached.updated_at
        : new Date().toISOString();
    const publishState = normalizeGrammarBackendStatus(item.status ?? 'draft');
    const cachedExercises = Array.isArray(cached.exercises) ? cached.exercises : [];
    const progress = getGrammarProgressCache()[id] ?? null;
    const masteryScore = progress?.masteryScore ?? progress?.score ?? 0;

    return {
      id,
      examTypeCode: toExamFamilyCode(item.profession ?? item.examTypeCode ?? 'oet'),
      topicId: typeof item.topicId === 'string' && item.topicId.length > 0 ? String(item.topicId) : category,
      topicSlug: category,
      topicName: titleCase(category),
      category,
      title: typeof item.title === 'string' ? item.title : '',
      description: toNullableString(item.description) ?? toNullableString(cached.description),
      level: normalizeGrammarLevel(item.difficulty ?? item.level),
      estimatedMinutes: Number(item.estimatedDurationMinutes ?? item.estimatedMinutes ?? 0) || 0,
      sortOrder: Number(item.sortOrder ?? cached.sortOrder ?? 0) || 0,
      exerciseCount: Number(item.exerciseCount ?? cachedExercises.length ?? 0) || 0,
      progress,
      mastered: progress?.status === 'completed' && masteryScore >= 80,
      statusLabel: publishState,
      status: publishState,
      publishState,
      updatedAt,
    };
  });

  const filtered = params?.topicId
    ? items.filter((item) => item.topicId === params.topicId || item.category === params.topicId)
    : items;

  return {
    total: params?.topicId ? filtered.length : response.total,
    page: response.page,
    pageSize: response.pageSize,
    items: filtered,
  };
}

export async function adminGetGrammarLessonV2(lessonId: string) {
  const raw = await apiRequest<ApiRecord>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}`);
  const document = parseGrammarLessonDocument(raw.content ?? raw.contentHtml ?? '', raw.exercisesJson ?? '[]');
  const publishState = normalizeGrammarBackendStatus(raw.status ?? raw.Status ?? 'draft');
  const lesson: AdminGrammarLessonFull = {
    id: String(raw.id ?? raw.Id ?? lessonId),
    examTypeCode: toExamFamilyCode(raw.profession ?? raw.examTypeCode ?? 'oet'),
    topicId: document.topicId ?? document.category,
    topicSlug: document.category,
    topicName: titleCase(document.category),
    category: document.category,
    title: typeof raw.title === 'string' ? raw.title : '',
    description: toNullableString(raw.description),
    level: normalizeGrammarLevel(raw.difficulty ?? raw.level),
    estimatedMinutes: Number(raw.estimatedDurationMinutes ?? raw.estimatedMinutes ?? 0) || 0,
    sortOrder: Number(raw.sortOrder ?? 0) || 0,
    exerciseCount: document.exercises.length,
    progress: getGrammarProgressCache()[lessonId] ?? null,
    mastered: false,
    statusLabel: publishState,
    contentBlocks: document.contentBlocks,
    exercises: document.exercises,
    publishState,
    version: document.version,
    sourceProvenance: document.sourceProvenance || '',
    prerequisiteLessonIds: document.prerequisiteLessonIds,
    status: normalizeGrammarBackendStatus(raw.status ?? 'draft'),
    createdAt: typeof raw.createdAt === 'string' ? raw.createdAt : null,
    updatedAt: document.updatedAt,
    publishedAt: typeof raw.publishedAt === 'string' ? raw.publishedAt : null,
  };
  cacheAdminGrammarLesson(lesson.id, { ...lesson, updatedAt: lesson.updatedAt });
  return lesson;
}

export async function adminCreateGrammarLessonV2(payload: GrammarLessonUpsertPayload) {
  const category = normalizeGrammarTopicSlug(payload.category || payload.topicId || 'general');
  const document = createGrammarLessonPayload(payload);
  const response = await apiRequest<ApiRecord>('/v1/admin/grammar/lessons', {
    method: 'POST',
    body: JSON.stringify({
      title: payload.title,
      professionId: payload.examTypeCode,
      category,
      description: payload.description,
      content: JSON.stringify(document),
      difficulty: payload.level,
      estimatedDurationMinutes: payload.estimatedMinutes,
      sortOrder: payload.sortOrder,
    }),
  });

  const lessonId = String(response.id ?? response.Id ?? `lesson-${Date.now()}`);
  cacheAdminGrammarLesson(lessonId, { ...document, id: lessonId, title: payload.title, description: payload.description, examTypeCode: payload.examTypeCode, category, status: 'draft', publishState: 'draft', updatedAt: document.updatedAt });
  return { id: lessonId, status: normalizeGrammarBackendStatus(response.status ?? 'draft') };
}

export async function adminUpdateGrammarLessonV2(lessonId: string, payload: GrammarLessonUpsertPayload & { status?: string }) {
  const existing = getGrammarLessonCache()[lessonId] ?? {};
  const category = normalizeGrammarTopicSlug(payload.category || payload.topicId || existing.category || 'general');
  const document = {
    ...createGrammarLessonPayload(payload),
    version: Number(existing.version ?? 0) + 1 || 1,
  };

  const response = await apiRequest<ApiRecord>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}`, {
    method: 'PUT',
    body: JSON.stringify({
      title: payload.title,
      professionId: payload.examTypeCode,
      category,
      description: payload.description,
      content: JSON.stringify(document),
      difficulty: payload.level,
      estimatedDurationMinutes: payload.estimatedMinutes,
      sortOrder: payload.sortOrder,
      status: payload.status,
    }),
  });

  cacheAdminGrammarLesson(lessonId, { ...document, id: lessonId, title: payload.title, description: payload.description, examTypeCode: payload.examTypeCode, category, status: normalizeGrammarBackendStatus(payload.status ?? response.status ?? 'draft'), publishState: normalizeGrammarBackendStatus(payload.status ?? response.status ?? 'draft'), updatedAt: document.updatedAt });
  return response;
}

export async function adminPublishGrammarLesson(lessonId: string) {
  return adminUpdateGrammarLessonV2(lessonId, { ...(getGrammarLessonCache()[lessonId] ?? {}), status: 'active' } as GrammarLessonUpsertPayload & { status?: string });
}

export async function adminUnpublishGrammarLesson(lessonId: string) {
  return adminUpdateGrammarLessonV2(lessonId, { ...(getGrammarLessonCache()[lessonId] ?? {}), status: 'draft' } as GrammarLessonUpsertPayload & { status?: string });
}

export async function adminArchiveGrammarLessonV2(lessonId: string) {
  const response = await apiRequest(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/archive`, { method: 'POST' });
  const cache = getGrammarLessonCache();
  if (cache[lessonId]) {
    cache[lessonId] = { ...cache[lessonId], status: 'archived', publishState: 'archived', updatedAt: new Date().toISOString() };
    saveGrammarLessonCache(cache);
  }
  return response;
}

/** Permanently deletes an archived grammar lesson + all learner progress. system_admin only. */
export async function adminForceDeleteGrammarLessonV2(lessonId: string) {
  const response = await apiRequest(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/force-delete`, { method: 'POST' });
  const cache = getGrammarLessonCache();
  if (cache[lessonId]) {
    delete cache[lessonId];
    saveGrammarLessonCache(cache);
  }
  return response;
}

export async function adminEvaluateGrammarPublishGate(lessonId: string) {
  const lesson = await adminGetGrammarLessonV2(lessonId);
  const errors: string[] = [];

  if (!lesson.title.trim()) errors.push('Title is required.');
  if (!lesson.description?.trim()) errors.push('Description is required.');
  if (!lesson.sourceProvenance?.trim()) errors.push('Source provenance is required.');
  if (!lesson.category?.trim()) errors.push('Category is required.');
  if (!lesson.contentBlocks.length) errors.push('Add at least one content block.');
  if (!lesson.exercises.length) errors.push('Add at least one exercise.');

  lesson.exercises.forEach((exercise, index) => {
    if (!exercise.promptMarkdown.trim()) errors.push(`Exercise ${index + 1} needs a prompt.`);
    if (exercise.type === 'matching' && (!Array.isArray(exercise.options) || exercise.options.length === 0)) errors.push(`Exercise ${index + 1} needs matching pairs.`);
    if (exercise.type !== 'matching' && exercise.correctAnswer == null) errors.push(`Exercise ${index + 1} needs a correct answer.`);
    if (!exercise.explanationMarkdown.trim()) errors.push(`Exercise ${index + 1} needs an explanation.`);
  });

  return {
    canPublish: errors.length === 0,
    errors,
  };
}

export async function adminGenerateGrammarAiDraft(params: { examTypeCode: string; topicSlug?: string; prompt: string; level: string; targetExerciseCount: number; profession?: string; }) {
  // Grounded, platform-only. The backend builds the AI prompt via
  // IAiGatewayService and refuses ungrounded prompts. The client must not
  // create local lesson content when the backend is unavailable.
  return apiRequest<{
    lessonId: string;
    title: string;
    contentBlockCount: number;
    exerciseCount: number;
    rulebookVersion: string;
    appliedRuleIds: string[];
    warning: string | null;
  }>('/v1/admin/grammar/ai-draft', {
    method: 'POST',
    body: JSON.stringify({
      examTypeCode: params.examTypeCode,
      topicSlug: params.topicSlug ?? null,
      prompt: params.prompt,
      level: params.level,
      targetExerciseCount: params.targetExerciseCount,
      profession: params.profession ?? 'medicine',
    }),
  });
}

export interface GrammarEntitlement {
  allowed: boolean;
  tier: string;
  remaining: number | null;
  limitPerWindow: number | null;
  windowDays: number;
  resetAt: string | null;
  reason: string;
}

export async function adminGenerateWritingAiDraft(params: {
  prompt: string;
  profession: string;
  letterType: string;
  recipientSpecialty?: string;
  difficulty: string;
  targetCaseNoteCount: number;
}) {
  // Grounded, platform-only. The backend builds the AI prompt via
  // IAiGatewayService and refuses ungrounded prompts. On AI-parse failure
  // the server returns a deterministic starter template + `warning`.
  return apiRequest<{
    contentId: string;
    title: string;
    caseNoteCount: number;
    modelLetterWordCount: number;
    rulebookVersion: string;
    appliedRuleIds: string[];
    warning: string | null;
  }>('/v1/admin/writing/ai-draft', {
    method: 'POST',
    body: JSON.stringify({
      prompt: params.prompt,
      profession: params.profession,
      letterType: params.letterType,
      recipientSpecialty: params.recipientSpecialty ?? null,
      difficulty: params.difficulty,
      targetCaseNoteCount: params.targetCaseNoteCount,
    }),
  });
}

export async function fetchGrammarEntitlement(): Promise<GrammarEntitlement> {
  return apiRequest<GrammarEntitlement>('/v1/grammar/entitlement');
}

// ── Writing Options (admin: AI kill-switch + entitlement) ──

export interface AdminWritingOptions {
  aiGradingEnabled: boolean;
  aiCoachEnabled: boolean;
  killSwitchReason: string | null;
  freeTierEnabled: boolean;
  freeTierLimit: number;
  freeTierWindowDays: number;
  updatedAt: string | null;
  updatedByAdminId: string | null;
}

export async function adminGetWritingOptions(): Promise<AdminWritingOptions> {
  return apiRequest<AdminWritingOptions>('/v1/admin/writing/options');
}

export async function adminUpdateWritingOptions(
  input: Omit<AdminWritingOptions, 'updatedAt' | 'updatedByAdminId'>,
): Promise<AdminWritingOptions> {
  return apiRequest<AdminWritingOptions>('/v1/admin/writing/options', {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

// ── Writing Rule-Violation Analytics (admin: P22 dashboard) ──

export interface AdminWritingRuleViolationSummary {
  totalViolations: number;
  distinctRules: number;
  distinctAttempts: number;
  distinctLearners: number;
  ruleEngineCount: number;
  aiCount: number;
}

export interface AdminWritingProfessionCount {
  profession: string;
  count: number;
}

export interface AdminWritingLetterTypeCount {
  letterType: string;
  count: number;
}

export interface AdminWritingRuleViolationGroup {
  ruleId: string;
  totalCount: number;
  distinctAttempts: number;
  distinctLearners: number;
  criticalCount: number;
  majorCount: number;
  minorCount: number;
  infoCount: number;
  professions: AdminWritingProfessionCount[];
}

export interface AdminWritingRuleViolationDashboard {
  generatedAt: string;
  windowDays: number;
  professionFilter: string | null;
  summary: AdminWritingRuleViolationSummary;
  topRules: AdminWritingRuleViolationGroup[];
  professionBreakdown: AdminWritingProfessionCount[];
  letterTypeBreakdown: AdminWritingLetterTypeCount[];
}

export interface AdminWritingRuleViolationRow {
  id: string;
  ruleId: string;
  severity: string;
  source: string;
  message: string;
  quote: string | null;
  generatedAt: string;
}

export interface AdminWritingAttemptViolations {
  attemptId: string;
  evaluationId: string | null;
  userId: string | null;
  profession: string | null;
  letterType: string | null;
  generatedAt: string | null;
  items: AdminWritingRuleViolationRow[];
}

export async function adminGetWritingRuleViolationDashboard(opts?: {
  days?: number;
  profession?: string;
}): Promise<AdminWritingRuleViolationDashboard> {
  const params = new URLSearchParams();
  if (opts?.days != null) params.set('days', String(opts.days));
  if (opts?.profession) params.set('profession', opts.profession);
  const qs = params.toString();
  return apiRequest<AdminWritingRuleViolationDashboard>(
    `/v1/admin/writing/analytics/rule-violations${qs ? `?${qs}` : ''}`,
  );
}

export async function adminGetWritingAttemptViolations(
  attemptId: string,
): Promise<AdminWritingAttemptViolations> {
  return apiRequest<AdminWritingAttemptViolations>(
    `/v1/admin/writing/analytics/rule-violations/${encodeURIComponent(attemptId)}`,
  );
}

export async function adminFetchGrammarPublishGate(lessonId: string) {
  return apiRequest<{ canPublish: boolean; errors: string[] }>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/publish-gate`);
}

export async function adminPublishGrammarLessonV2(lessonId: string) {
  return apiRequest<{ published: boolean; status: string; errors: string[] }>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/publish`, { method: 'POST' });
}

export async function adminUnpublishGrammarLessonV2(lessonId: string) {
  return apiRequest<{ id: string; status: string }>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/unpublish`, { method: 'POST' });
}

export async function adminFetchGrammarStats(lessonId: string) {
  return apiRequest<{
    lessonId: string;
    attempts: number;
    uniqueLearners: number;
    averageMasteryScore: number;
    reviewItemsCreated: number;
  }>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/stats`);
}


// ── Video Lessons ─────────────────────────────────────────────────────────────

export async function fetchVideoLessons(params?: { examTypeCode?: string; subtestCode?: string; category?: string }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.subtestCode) p.set('subtestCode', params.subtestCode);
  if (params?.category) p.set('category', params.category);
  return apiRequest<VideoLessonListItem[]>(`/v1/lessons?${p}`);
}

export async function fetchVideoLesson(lessonId: string) {
  return apiRequest<VideoLessonDetail>(`/v1/lessons/${encodeURIComponent(lessonId)}`);
}

export async function updateVideoProgress(lessonId: string, watchedSeconds: number) {
  return apiRequest<VideoProgressUpdateResponse>(`/v1/lessons/${encodeURIComponent(lessonId)}/progress`, {
    method: 'POST',
    body: JSON.stringify({ watchedSeconds }),
  });
}

export async function fetchVideoLessonProgram(programId: string) {
  return apiRequest<VideoLessonProgram>(`/v1/lessons/programs/${encodeURIComponent(programId)}`);
}

// ── Strategy Guides ───────────────────────────────────────────────────────────

export async function fetchStrategyGuides(params?: { examTypeCode?: string; subtestCode?: string; category?: string; q?: string; recommended?: boolean }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.subtestCode) p.set('subtestCode', params.subtestCode);
  if (params?.category) p.set('category', params.category);
  if (params?.q) p.set('q', params.q);
  if (params?.recommended) p.set('recommended', 'true');
  return apiRequest<StrategyGuideLibrary>(`/v1/strategies?${p}`);
}

export async function fetchStrategyGuide(guideId: string) {
  return apiRequest<StrategyGuideDetail>(`/v1/strategies/${encodeURIComponent(guideId)}`);
}

export async function updateStrategyGuideProgress(guideId: string, readPercent: number) {
  return apiRequest<StrategyGuideProgressUpdateResponse>(`/v1/strategies/${encodeURIComponent(guideId)}/progress`, {
    method: 'POST',
    body: JSON.stringify({ readPercent }),
  });
}

export async function setStrategyGuideBookmark(guideId: string, bookmarked: boolean) {
  return apiRequest<StrategyGuideBookmarkUpdateResponse>(`/v1/strategies/${encodeURIComponent(guideId)}/bookmark`, {
    method: 'POST',
    body: JSON.stringify({ bookmarked }),
  });
}

export async function adminListStrategyGuides(params?: { status?: string; examTypeCode?: string; search?: string }) {
  const p = new URLSearchParams();
  if (params?.status) p.set('status', params.status);
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.search) p.set('search', params.search);
  return apiRequest<StrategyGuideAdminItem[]>(`/v1/admin/strategies?${p}`);
}

export async function adminGetStrategyGuide(guideId: string) {
  return apiRequest<StrategyGuideAdminItem>(`/v1/admin/strategies/${encodeURIComponent(guideId)}`);
}

export async function adminCreateStrategyGuide(payload: StrategyGuideUpsertPayload) {
  return apiRequest<StrategyGuideAdminItem>('/v1/admin/strategies', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function adminUpdateStrategyGuide(guideId: string, payload: StrategyGuideUpsertPayload) {
  return apiRequest<StrategyGuideAdminItem>(`/v1/admin/strategies/${encodeURIComponent(guideId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function adminValidateStrategyGuidePublish(guideId: string) {
  return apiRequest<StrategyGuidePublishValidation>(`/v1/admin/strategies/${encodeURIComponent(guideId)}/publish-gate`);
}

export async function adminPublishStrategyGuide(guideId: string) {
  return apiRequest<StrategyGuidePublishResult>(`/v1/admin/strategies/${encodeURIComponent(guideId)}/publish`, {
    method: 'POST',
  });
}

export async function adminArchiveStrategyGuide(guideId: string) {
  return apiRequest<StrategyGuideAdminItem>(`/v1/admin/strategies/${encodeURIComponent(guideId)}/archive`, {
    method: 'POST',
  });
}

/** Permanently deletes an archived strategy guide + all learner progress. system_admin only. */
export async function adminForceDeleteStrategyGuide(guideId: string) {
  return apiRequest(`/v1/admin/strategies/${encodeURIComponent(guideId)}/force-delete`, {
    method: 'POST',
  });
}

// ── Pronunciation ─────────────────────────────────────────────────────────────
// All pronunciation endpoints are protected by the backend's LearnerOnly policy
// and the `pronunciation_analysis` feature flag. The recording+scoring flow:
//   1. pronunciationInitAttempt(drillId) → { attemptId, uploadUrl, ... }
//   2. pronunciationUploadAudio(drillId, attemptId, blob, durationMs)
//   3. fetchPronunciationAssessment(assessmentId) for the result detail page.

export type PronunciationDrillSummary = {
  id: string;
  targetPhoneme: string;
  label: string;
  profession: string;
  focus: string;
  primaryRuleId: string | null;
  exampleWordsJson: string;
  minimalPairsJson: string;
  sentencesJson: string;
  difficulty: string;
  audioModelUrl: string | null;
  audioModelAssetId: string | null;
  tipsHtml: string;
};

export type PronunciationProgressItem = {
  phonemeCode: string;
  averageScore: number;
  attemptCount: number;
  lastPracticedAt: string | null;
  nextDueAt?: string | null;
  intervalDays?: number;
};

export type PronunciationEntitlement = {
  allowed: boolean;
  tier: string;
  remaining: number;
  limitPerWindow: number;
  windowDays: number;
  resetAt: string | null;
  reason: string;
};

export type PronunciationAssessmentDetail = {
  id: string;
  drillId: string | null;
  attemptId: string | null;
  accuracy: number;
  fluency: number;
  completeness: number;
  prosody: number;
  overall: number;
  projectedSpeakingScaled: number;
  projectedSpeakingGrade: string;
  wordScoresJson: string;
  problematicPhonemesJson: string;
  fluencyMarkersJson: string;
  findingsJson: string;
  feedbackJson: string;
  provider: string;
  rulebookVersion: string;
  createdAt: string;
};

export async function fetchPronunciationDrills(params?: {
  profession?: string;
  difficulty?: string;
  focus?: string;
}) {
  const q = new URLSearchParams();
  if (params?.profession) q.set('profession', params.profession);
  if (params?.difficulty) q.set('difficulty', params.difficulty);
  if (params?.focus) q.set('focus', params.focus);
  const suffix = q.toString() ? `?${q.toString()}` : '';
  return apiRequest(`/v1/pronunciation/drills${suffix}`);
}

export async function fetchPronunciationDueDrills(limit = 6) {
  return apiRequest(`/v1/pronunciation/drills/due?limit=${limit}`);
}

export async function fetchPronunciationDrill(drillId: string) {
  return apiRequest(`/v1/pronunciation/drills/${encodeURIComponent(drillId)}`);
}

export async function fetchMyPronunciationProgress() {
  return apiRequest('/v1/pronunciation/my-progress');
}

export async function fetchPronunciationProfile() {
  return apiRequest('/v1/pronunciation/profile');
}

export async function fetchPronunciationEntitlement() {
  return apiRequest('/v1/pronunciation/entitlement');
}

/** Reserve an attempt id + upload slot. Returns entitlement info too. */
export async function pronunciationInitAttempt(drillId: string) {
  return apiRequest(`/v1/pronunciation/drills/${encodeURIComponent(drillId)}/attempt/init`, {
    method: 'POST',
    body: JSON.stringify({}),
  });
}

/**
 * Upload the recorded audio blob and synchronously receive the scored
 * assessment. The request uses raw-body POST with the audio Content-Type set
 * directly — no multipart wrapper needed. Bypasses the JSON-first apiRequest.
 */
export async function pronunciationUploadAudio(
  drillId: string,
  attemptId: string,
  blob: Blob,
  options?: { durationMs?: number; signal?: AbortSignal }
) {
  const path = `/v1/pronunciation/drills/${encodeURIComponent(drillId)}/attempt/${encodeURIComponent(attemptId)}/audio`;
  const headers = new Headers(
    await getHeaders(path, undefined, { json: false })
  );
  headers.set('Content-Type', blob.type || 'application/octet-stream');
  if (typeof options?.durationMs === 'number') {
    headers.set('X-Audio-Duration-Ms', String(Math.round(options.durationMs)));
  }
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'POST',
    headers,
    body: blob,
    signal: options?.signal,
  }, 120_000);
  if (!response.ok) {
    let message = `Upload failed: ${response.status}`;
    let code = 'upload_failed';
    try {
      const err = await response.json();
      message = err.message ?? err.title ?? message;
      code = err.code ?? code;
    } catch {
      /* non-JSON error */
    }
    throw new ApiError(response.status, code, message, false);
  }
  return response.json();
}

export async function fetchPronunciationAssessment(assessmentId: string) {
  return apiRequest(`/v1/pronunciation/assessment/${encodeURIComponent(assessmentId)}`);
}

export async function fetchPronunciationSpeakingLinked(limit = 20) {
  return apiRequest(`/v1/pronunciation/speaking-linked?limit=${limit}`);
}

export async function submitPronunciationDiscrimination(
  drillId: string,
  roundsTotal: number,
  roundsCorrect: number,
) {
  return apiRequest(
    `/v1/pronunciation/drills/${encodeURIComponent(drillId)}/discrimination`,
    {
      method: 'POST',
      body: JSON.stringify({ roundsTotal, roundsCorrect }),
    }
  );
}

// ── Admin: Pronunciation CMS ───────────────────────────────────────────────
export async function fetchAdminPronunciationDrills(params?: {
  profession?: string;
  difficulty?: string;
  status?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}) {
  const q = new URLSearchParams();
  if (params?.profession) q.set('profession', params.profession);
  if (params?.difficulty) q.set('difficulty', params.difficulty);
  if (params?.status) q.set('status', params.status);
  if (params?.search) q.set('search', params.search);
  if (params?.page) q.set('page', String(params.page));
  if (params?.pageSize) q.set('pageSize', String(params.pageSize));
  const suffix = q.toString() ? `?${q.toString()}` : '';
  return apiRequest(`/v1/admin/pronunciation/drills${suffix}`);
}

export async function fetchAdminPronunciationDrill(drillId: string) {
  return apiRequest(`/v1/admin/pronunciation/drills/${encodeURIComponent(drillId)}`);
}

export async function createAdminPronunciationDrill(body: Record<string, unknown>) {
  return apiRequest('/v1/admin/pronunciation/drills', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export async function updateAdminPronunciationDrill(drillId: string, body: Record<string, unknown>) {
  return apiRequest(`/v1/admin/pronunciation/drills/${encodeURIComponent(drillId)}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

export async function archiveAdminPronunciationDrill(drillId: string) {
  return apiRequest(`/v1/admin/pronunciation/drills/${encodeURIComponent(drillId)}/archive`, {
    method: 'POST',
    body: JSON.stringify({}),
  });
}

/** Permanently deletes an archived pronunciation drill + all learner attempts/assessments. system_admin only. */
export async function forceDeleteAdminPronunciationDrill(drillId: string) {
  return apiRequest(`/v1/admin/pronunciation/drills/${encodeURIComponent(drillId)}/force-delete`, {
    method: 'POST',
  });
}

export async function adminPronunciationAiDraft(body: {
  phoneme?: string;
  focus?: string;
  profession?: string;
  difficulty?: string;
  prompt?: string;
  primaryRuleId?: string;
}) {
  return apiRequest('/v1/admin/pronunciation/drills/ai-draft', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export type AdminPronunciationGenerateAudioResponse = {
  mediaAssetId: string;
  sha256: string;
  durationMs: number;
  bytes: number;
  providerName: string;
  mimeType: string;
  storageKey: string;
  url: string;
};

export async function generateAdminPronunciationModelAudio(
  drillId: string,
  body: { text: string; voiceId?: string },
): Promise<AdminPronunciationGenerateAudioResponse> {
  return apiRequest(
    `/v1/admin/pronunciation/drills/${encodeURIComponent(drillId)}/generate-model-audio`,
    {
      method: 'POST',
      body: JSON.stringify(body),
    },
  ) as Promise<AdminPronunciationGenerateAudioResponse>;
}

// ── Certificates ──────────────────────────────────────────────────────────────

export async function fetchMyCertificates() {
  return apiRequest('/v1/certificates');
}

export async function verifyCertificate(code: string) {
  return apiRequest(`/v1/certificates/verify/${encodeURIComponent(code)}`);
}

// ── Referrals ─────────────────────────────────────────────────────────────────

export async function fetchMyReferralCode() {
  return apiRequest('/v1/referrals/my-code');
}

export async function fetchMyReferrals() {
  return apiRequest('/v1/referrals/my-referrals');
}

export async function applyReferralCode(code: string) {
  return apiRequest('/v1/referrals/apply', {
    method: 'POST',
    body: JSON.stringify({ code }),
  });
}

// ── Exam Booking ──────────────────────────────────────────────────────────────

export async function fetchExamBookings() {
  return apiRequest('/v1/exam-bookings');
}

export async function createExamBooking(payload: { examTypeCode: string; examDate: string; bookingReference?: string; externalUrl?: string; testCenter?: string }) {
  return apiRequest('/v1/exam-bookings', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function deleteExamBooking(bookingId: string) {
  return apiRequest(`/v1/exam-bookings/${encodeURIComponent(bookingId)}`, { method: 'DELETE' });
}

// ── Tutoring ──────────────────────────────────────────────────────────────────

export async function fetchTutoringSessions() {
  return apiRequest('/v1/tutoring/sessions');
}

export async function bookTutoringSession(payload: { expertUserId: string; examTypeCode: string; subtestFocus?: string; scheduledAt: string; durationMinutes: number; learnerNotes?: string; price: number }) {
  return apiRequest('/v1/tutoring/sessions', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function rateTutoringSession(sessionId: string, rating: number, feedback?: string) {
  return apiRequest(`/v1/tutoring/sessions/${encodeURIComponent(sessionId)}/rate`, {
    method: 'POST',
    body: JSON.stringify({ rating, feedback }),
  });
}

// ── AI Conversation ─────────────────────────────────────────────────────

export async function createConversation(params: {
  contentId?: string;
  examFamilyCode?: string;
  taskTypeCode: string;
  profession?: string;
  difficulty?: string;
}) {
  return apiRequest('/v1/conversations', {
    method: 'POST',
    body: JSON.stringify(params),
  });
}

export async function getConversation(sessionId: string) {
  return apiRequest(`/v1/conversations/${encodeURIComponent(sessionId)}`);
}

export async function resumeConversation(sessionId: string, resumeToken?: string) {
  return apiClient.post(`/v1/conversations/${encodeURIComponent(sessionId)}/resume`, resumeToken ? { resumeToken } : {});
}

export function conversationTranscriptExportUrl(sessionId: string, format: 'txt' | 'pdf' = 'txt') {
  return resolveApiUrl(`/v1/conversations/${encodeURIComponent(sessionId)}/transcript/export?format=${encodeURIComponent(format)}`);
}

export async function downloadConversationTranscript(sessionId: string, format: 'txt' | 'pdf' = 'txt'): Promise<Blob> {
  const path = `/v1/conversations/${encodeURIComponent(sessionId)}/transcript/export?format=${encodeURIComponent(format)}`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    method: 'GET',
    headers: await getHeaders(path, undefined, { json: false }),
  });
  if (!response.ok) {
    throw new ApiError(response.status, 'transcript_export_failed', `Transcript export failed: ${response.status}`, isRetryable(response.status));
  }
  return response.blob();
}

export async function completeConversation(sessionId: string) {
  return apiRequest(`/v1/conversations/${encodeURIComponent(sessionId)}/complete`, {
    method: 'POST',
  });
}

export async function getConversationEvaluation(sessionId: string) {
  return apiRequest(`/v1/conversations/${encodeURIComponent(sessionId)}/evaluation`);
}

export async function getConversationHistory(page = 1, pageSize = 10) {
  return apiRequest(`/v1/conversations/history?page=${page}&pageSize=${pageSize}`);
}

export async function getConversationTaskTypes() {
  return apiRequest('/v1/conversations/task-types');
}

export async function getConversationEntitlement() {
  return apiRequest('/v1/conversations/entitlement');
}

// ── Admin: Conversation Templates ──────────────────────────────────────

export async function fetchAdminConversationTemplates(params?: {
  profession?: string;
  status?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}) {
  const q = new URLSearchParams();
  if (params?.profession) q.set('profession', params.profession);
  if (params?.status) q.set('status', params.status);
  if (params?.search) q.set('search', params.search);
  if (params?.page) q.set('page', String(params.page));
  if (params?.pageSize) q.set('pageSize', String(params.pageSize));
  const qs = q.toString();
  return apiRequest(`/v1/admin/conversation/templates${qs ? `?${qs}` : ''}`);
}

export async function fetchAdminConversationTemplate(templateId: string) {
  return apiRequest(`/v1/admin/conversation/templates/${encodeURIComponent(templateId)}`);
}

export async function createAdminConversationTemplate(body: Record<string, unknown>) {
  return apiRequest('/v1/admin/conversation/templates', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export async function updateAdminConversationTemplate(templateId: string, body: Record<string, unknown>) {
  return apiRequest(`/v1/admin/conversation/templates/${encodeURIComponent(templateId)}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

export async function publishAdminConversationTemplate(templateId: string) {
  return apiRequest(`/v1/admin/conversation/templates/${encodeURIComponent(templateId)}/publish`, {
    method: 'POST',
  });
}

export async function archiveAdminConversationTemplate(templateId: string) {
  return apiRequest(`/v1/admin/conversation/templates/${encodeURIComponent(templateId)}/archive`, {
    method: 'POST',
  });
}

/** Permanently deletes an archived conversation template + all learner sessions. system_admin only. */
export async function forceDeleteAdminConversationTemplate(templateId: string) {
  return apiRequest(`/v1/admin/conversation/templates/${encodeURIComponent(templateId)}/force-delete`, {
    method: 'POST',
  });
}

export async function fetchAdminConversationSettings() {
  return apiRequest('/v1/admin/conversation/settings');
}

export async function updateAdminConversationSettings(body: Record<string, unknown>) {
  return apiRequest('/v1/admin/conversation/settings', {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

export interface AdminLaunchReadinessSettings {
  mobileMinSupportedVersion: string;
  mobileLatestVersion: string;
  mobileForceUpdate: boolean;
  iosAppStoreUrl: string | null;
  androidPlayStoreUrl: string | null;
  iosBundleId: string | null;
  appleTeamId: string | null;
  appleAssociatedDomainStatus: string | null;
  appleUniversalLinksStatus: string | null;
  iosSigningProfileReference: string | null;
  iosIapStatus: string | null;
  iosPushStatus: string | null;
  androidPackageName: string | null;
  androidSha256Fingerprints: string | null;
  androidSigningKeyReference: string | null;
  androidAssetLinksStatus: string | null;
  androidIapStatus: string | null;
  androidPushStatus: string | null;
  desktopMinSupportedVersion: string;
  desktopLatestVersion: string;
  desktopForceUpdate: boolean;
  desktopUpdateFeedUrl: string | null;
  desktopUpdateChannel: string | null;
  windowsSigningStatus: string | null;
  macSigningStatus: string | null;
  linuxSigningStatus: string | null;
  deviceValidationEvidenceUrl: string | null;
  deviceValidationNotes: string | null;
  realtimeLegalApprovalStatus: string | null;
  realtimePrivacyApprovalStatus: string | null;
  realtimeProtectedSmokeStatus: string | null;
  realtimeEvidenceUrl: string | null;
  realtimeSpendCapApproved: boolean;
  realtimeTopologyApproved: boolean;
  releaseOwnerApprovalStatus: string | null;
  launchNotes: string | null;
  updatedAt: string;
  updatedByAdminId: string | null;
  updatedByAdminName: string | null;
}

export async function fetchAdminLaunchReadinessSettings(): Promise<AdminLaunchReadinessSettings> {
  return apiRequest<AdminLaunchReadinessSettings>('/v1/admin/launch-readiness/settings');
}

export async function updateAdminLaunchReadinessSettings(
  body: Partial<AdminLaunchReadinessSettings>,
): Promise<AdminLaunchReadinessSettings> {
  return apiRequest<AdminLaunchReadinessSettings>('/v1/admin/launch-readiness/settings', {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

export async function adminConversationTtsPreview(body: { text?: string; voice?: string; locale?: string; modelVariant?: string; instructions?: string }) {
  return apiBlobRequest('/v1/admin/conversation/tts-preview', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

// A voice from the ElevenLabs catalogue (GET /v1/voices), surfaced so admins
// can browse, audition, and pick the platform default voice id.
export interface AdminElevenLabsVoice {
  voiceId: string;
  name: string;
  category?: string | null;
  previewUrl?: string | null;
  labels?: Record<string, string> | null;
}

export async function getElevenLabsVoices(): Promise<{ voices: AdminElevenLabsVoice[] }> {
  return apiRequest<{ voices: AdminElevenLabsVoice[] }>('/v1/admin/voice-design/elevenlabs/voices');
}

export async function fetchAdminConversationSessions(params?: {
  userId?: string;
  state?: string;
  taskTypeCode?: string;
  page?: number;
  pageSize?: number;
}) {
  const q = new URLSearchParams();
  if (params?.userId) q.set('userId', params.userId);
  if (params?.state) q.set('state', params.state);
  if (params?.taskTypeCode) q.set('taskTypeCode', params.taskTypeCode);
  if (params?.page) q.set('page', String(params.page));
  if (params?.pageSize) q.set('pageSize', String(params.pageSize));
  const qs = q.toString();
  return apiRequest(`/v1/admin/conversation/sessions${qs ? `?${qs}` : ''}`);
}

export async function fetchAdminConversationSessionDetail(sessionId: string) {
  return apiRequest(`/v1/admin/conversation/sessions/${encodeURIComponent(sessionId)}`);
}

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

// ── Mocks Module Phase 6 — admin QC pipeline + item retire ──

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

export interface AdminMockBookingRow extends MockBooking {
  learnerId?: string | null;
  learnerDisplayName?: string | null;
  learnerEmail?: string | null;
  assignedTutorId?: string | null;
  assignedTutorDisplayName?: string | null;
  assignedInterlocutorId?: string | null;
  assignedInterlocutorDisplayName?: string | null;
  mockBundleTitle?: string | null;
}

export async function fetchAdminMockBookings(
  params?: { from?: string; to?: string },
): Promise<{ items: AdminMockBookingRow[] }> {
  const q = new URLSearchParams();
  if (params?.from) q.set('from', params.from);
  if (params?.to) q.set('to', params.to);
  const qs = q.toString();
  const response = await apiRequest<ApiRecord>(`/v1/admin/mocks/bookings${qs ? `?${qs}` : ''}`);
  const items = asArray(response.items).map((row): AdminMockBookingRow => {
    const base = mapMockBooking(row);
    return {
      ...base,
      learnerId: typeof row.learnerId === 'string' ? row.learnerId : null,
      learnerDisplayName: typeof row.learnerDisplayName === 'string' ? row.learnerDisplayName : null,
      learnerEmail: typeof row.learnerEmail === 'string' ? row.learnerEmail : null,
      assignedTutorId: typeof row.assignedTutorId === 'string' ? row.assignedTutorId : null,
      assignedTutorDisplayName: typeof row.assignedTutorDisplayName === 'string' ? row.assignedTutorDisplayName : null,
      assignedInterlocutorId: typeof row.assignedInterlocutorId === 'string' ? row.assignedInterlocutorId : null,
      assignedInterlocutorDisplayName: typeof row.assignedInterlocutorDisplayName === 'string' ? row.assignedInterlocutorDisplayName : null,
      mockBundleTitle: typeof row.mockBundleTitle === 'string' ? row.mockBundleTitle : null,
    };
  });
  return { items };
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

export async function transitionAdminMockBookingLiveRoomState(
  bookingId: string,
  targetState: MockLiveRoomTargetState,
  options?: MockLiveRoomTransitionOptions,
): Promise<MockBooking> {
  return transitionAdminMockBookingLiveRoom(bookingId, targetState, options);
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

export async function fetchAdminMockAnalytics() {
  return apiRequest('/v1/admin/mocks/analytics');
}

export async function fetchAdminMockRiskList() {
  return apiRequest('/v1/admin/mocks/risk-list');
}

export async function fetchExpertMockBookings() {
  const response = await apiRequest<ApiRecord>('/v1/expert/mocks/bookings');
  return asArray(response.items).map(mapMockBooking);
}

/**
 * Mocks V2 Wave 6 — fetch a single booking projection from the tutor-side
 * endpoint. Returns the raw API record (instead of the learner-stripped
 * {@link MockBooking} shape) because the expert projection embeds the
 * `speakingContent.interlocutorCard` payload that the learner DTO must never
 * expose. The caller (expert speaking-room page) needs that raw payload to
 * render the cue prompts and patient background.
 */
export interface ExpertMockBookingDetail extends Omit<MockBooking, 'speakingContent' | 'speakingPaperId'> {
  assignedTutorId?: string | null;
  assignedInterlocutorId?: string | null;
  zoomStartUrl?: string | null;
  speakingPaperId?: string | null;
  speakingContent?: ExpertSpeakingContent | null;
}

export interface ExpertSpeakingInterlocutorCard {
  background?: string;
  patientProfile?: string;
  cuePrompts?: string[];
  prompts?: string[];
  objectives?: string[];
  hiddenInformation?: string;
  [key: string]: unknown;
}

export interface ExpertSpeakingContent {
  candidateCard?: Record<string, unknown>;
  interlocutorCard?: ExpertSpeakingInterlocutorCard;
  warmUpQuestions?: string[];
  prepTimeSeconds?: number;
  roleplayTimeSeconds?: number;
  patientEmotion?: string;
  communicationGoal?: string;
  clinicalTopic?: string;
  criteriaFocus?: string[];
  disclaimer?: string;
  background?: string;
  setting?: string;
  patient?: string;
  task?: string;
  role?: string;
  [key: string]: unknown;
}

export async function fetchExpertMockBookingDetail(bookingId: string): Promise<ExpertMockBookingDetail> {
  const response = await apiRequest<ApiRecord>(`/v1/expert/mocks/bookings/${encodeURIComponent(bookingId)}`);
  const base = mapMockBooking(response);
  return {
    ...base,
    assignedTutorId: typeof response.assignedTutorId === 'string' ? response.assignedTutorId : null,
    assignedInterlocutorId: typeof response.assignedInterlocutorId === 'string' ? response.assignedInterlocutorId : null,
    zoomStartUrl: typeof response.zoomStartUrl === 'string' ? response.zoomStartUrl : null,
    speakingPaperId: typeof response.speakingPaperId === 'string' ? response.speakingPaperId : null,
    speakingContent: (response.speakingContent as ExpertSpeakingContent | null | undefined) ?? null,
  };
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
 * JSON below case-insensitively (matches the rest of this file's POST style).
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

// ── Writing Coach ───────────────────────────────────────────────────────

export async function coachCheckText(attemptId: string, currentText: string, cursorPosition?: number) {
  return apiRequest(`/v1/writing/attempts/${encodeURIComponent(attemptId)}/coach-check`, {
    method: 'POST',
    body: JSON.stringify({ currentText, cursorPosition }),
  });
}

export async function resolveCoachSuggestion(suggestionId: string, resolution: 'accepted' | 'dismissed') {
  return apiRequest(`/v1/writing/coach-suggestions/${encodeURIComponent(suggestionId)}/resolve`, {
    method: 'POST',
    body: JSON.stringify({ resolution }),
  });
}

export async function fetchCoachStats(attemptId: string) {
  return apiRequest(`/v1/writing/attempts/${encodeURIComponent(attemptId)}/coach-stats`);
}

// ── Content Generation (Admin) ──────────────────────────────────────────

export async function queueContentGeneration(params: {
  examTypeCode: string;
  subtestCode: string;
  taskTypeId?: string;
  professionId?: string;
  difficulty?: string;
  count: number;
  customInstructions?: string;
}) {
  return apiRequest('/v1/admin/content/generate', {
    method: 'POST',
    body: JSON.stringify(params),
  });
}

export async function fetchContentGenerationJobs(page = 1, pageSize = 20) {
  return apiRequest(`/v1/admin/content/generation-jobs?page=${page}&pageSize=${pageSize}`);
}

export async function fetchContentGenerationJob(jobId: string) {
  return apiRequest(`/v1/admin/content/generation-jobs/${encodeURIComponent(jobId)}`);
}

// ── Content Marketplace ─────────────────────────────────────────────────

export async function fetchMarketplaceProfile() {
  return apiRequest('/v1/marketplace/profile');
}

export async function updateMarketplaceProfile(data: { displayName?: string; bio?: string }) {
  // FE-026: backend registers PATCH /v1/marketplace/profile (not PUT) → PUT 405s.
  return apiRequest('/v1/marketplace/profile', {
    method: 'PATCH',
    body: JSON.stringify(data),
  });
}

export async function createMarketplaceSubmission(data: {
  examFamilyCode?: string;
  subtestCode: string;
  title: string;
  description?: string;
  contentPayloadJson?: string;
  contentType?: string;
  professionId?: string;
  difficulty?: string;
  tags?: string;
}) {
  return apiRequest('/v1/marketplace/submissions', {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export async function fetchMyMarketplaceSubmissions(page = 1, pageSize = 20) {
  return apiRequest(`/v1/marketplace/submissions?page=${page}&pageSize=${pageSize}`);
}

export async function fetchMarketplaceSubmission(submissionId: string) {
  return apiRequest(`/v1/marketplace/submissions/${encodeURIComponent(submissionId)}`);
}

export async function browseMarketplace(params?: { examTypeCode?: string; subtest?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.examTypeCode) qs.set('examTypeCode', params.examTypeCode);
  if (params?.subtest) qs.set('subtest', params.subtest);
  if (params?.search) qs.set('search', params.search);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/marketplace/browse?${qs}`);
}

export async function reviewMarketplaceSubmission(submissionId: string, data: { decision: 'approved' | 'rejected'; notes?: string; createContentItem?: boolean }) {
  return apiRequest(`/v1/admin/marketplace/submissions/${encodeURIComponent(submissionId)}/review`, {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export async function fetchPendingMarketplaceSubmissions(page = 1, pageSize = 20) {
  return apiRequest(`/v1/admin/marketplace/pending?page=${page}&pageSize=${pageSize}`);
}

// ── Admin Permissions (RBAC) ──────────────────────────

export async function fetchAllPermissions() {
  return apiRequest('/v1/admin/permissions');
}

export async function fetchAdminPermissions(userId: string) {
  return apiRequest(`/v1/admin/permissions/${encodeURIComponent(userId)}`);
}

export async function updateAdminPermissions(userId: string, permissions: string[]) {
  return apiRequest(`/v1/admin/permissions/${encodeURIComponent(userId)}`, {
    method: 'PUT',
    body: JSON.stringify({ permissions }),
  });
}

// ── Permission Templates ──────────────────────────────

export async function fetchPermissionTemplates() {
  return apiRequest('/v1/admin/permission-templates');
}

export async function createPermissionTemplate(name: string, description: string, permissions: string[]) {
  return apiRequest('/v1/admin/permission-templates', {
    method: 'POST',
    body: JSON.stringify({ name, description, permissions }),
  });
}

export async function deletePermissionTemplate(id: string) {
  return apiRequest(`/v1/admin/permission-templates/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  });
}

export async function applyPermissionTemplate(userId: string, templateId: string) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/apply-template/${encodeURIComponent(templateId)}`, {
    method: 'POST',
  });
}

// ── Content Publishing Workflow ────────────────────────

export async function requestContentPublish(contentId: string, note?: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/request-publish`, {
    method: 'POST',
    body: JSON.stringify({ note }),
  });
}

export async function submitContentForReview(contentId: string, note?: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/submit-for-review`, {
    method: 'POST',
    body: JSON.stringify({ note }),
  });
}

export async function editorApproveContent(contentId: string, notes?: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/editor-approve`, {
    method: 'POST',
    body: JSON.stringify({ notes }),
  });
}

export async function editorRejectContent(contentId: string, reason: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/editor-reject`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

export async function publisherApproveContent(contentId: string, notes?: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/publisher-approve`, {
    method: 'POST',
    body: JSON.stringify({ notes }),
  });
}

export async function publisherRejectContent(contentId: string, reason: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/publisher-reject`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

export async function fetchPendingReviewContent(params?: { stage?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.stage) qs.set('stage', params.stage);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/admin/content/pending-review?${qs}`);
}

export async function fetchPublishRequests(params?: { status?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/admin/publish-requests?${qs}`);
}

export async function approvePublishRequest(requestId: string, note?: string) {
  return apiRequest(`/v1/admin/publish-requests/${encodeURIComponent(requestId)}/approve`, {
    method: 'POST',
    body: JSON.stringify({ note }),
  });
}

export async function rejectPublishRequest(requestId: string, note?: string) {
  return apiRequest(`/v1/admin/publish-requests/${encodeURIComponent(requestId)}/reject`, {
    method: 'POST',
    body: JSON.stringify({ note }),
  });
}

// ── Webhook Monitoring ────────────────────────────────

export async function fetchWebhookEvents(params?: { gateway?: string; status?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.gateway) qs.set('gateway', params.gateway);
  if (params?.status) qs.set('status', params.status);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/admin/webhooks?${qs}`);
}

export async function fetchWebhookSummary() {
  return apiRequest('/v1/admin/webhooks/summary');
}

export async function retryWebhook(eventId: string) {
  return apiRequest(`/v1/admin/webhooks/${encodeURIComponent(eventId)}/retry`, {
    method: 'POST',
  });
}

// ── Review Escalations ────────────────────────────────

export async function fetchReviewEscalations(params?: { status?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/admin/escalations?${qs}`);
}

export async function assignEscalationReviewer(escalationId: string, secondReviewerId: string) {
  return apiRequest(`/v1/admin/escalations/${encodeURIComponent(escalationId)}/assign`, {
    method: 'POST',
    body: JSON.stringify({ secondReviewerId }),
  });
}

export async function resolveEscalation(escalationId: string, finalScore: number, resolutionNote?: string) {
  return apiRequest(`/v1/admin/escalations/${encodeURIComponent(escalationId)}/resolve`, {
    method: 'POST',
    body: JSON.stringify({ finalScore, resolutionNote }),
  });
}

// ── Learner Escalations (Disputes) ────────────────────

export async function submitEscalation(submissionId: string, reason: string, details: string) {
  return apiRequest('/v1/learner/escalations', {
    method: 'POST',
    body: JSON.stringify({ submissionId, reason, details }),
  });
}

export async function fetchMyEscalations() {
  const res = await apiRequest('/v1/learner/escalations');
  return res?.items ?? res;
}

export async function fetchEscalationDetails(id: string) {
  return apiRequest(`/v1/learner/escalations/${encodeURIComponent(id)}`);
}

// ── Score Guarantee (Learner) ─────────────────────────

export async function fetchScoreGuarantee() {
  return apiRequest('/v1/learner/score-guarantee');
}

export async function activateScoreGuarantee(baselineScore: number) {
  return apiRequest('/v1/learner/score-guarantee/activate', {
    method: 'POST',
    body: JSON.stringify({ baselineScore }),
  });
}

export async function submitScoreGuaranteeClaim(actualScore: number, proofDocumentUrl?: string, note?: string) {
  return apiRequest('/v1/learner/score-guarantee/claim', {
    method: 'POST',
    body: JSON.stringify({ actualScore, proofDocumentUrl, note }),
  });
}

// ── Score Equivalences ────────────────────────────────

export async function fetchScoreEquivalences() {
  return apiRequest('/v1/reference/score-equivalences');
}

// ── Study Commitment ──────────────────────────────────

export async function fetchStudyCommitment() {
  return apiRequest('/v1/learner/study-commitment');
}

export async function setStudyCommitment(dailyMinutes: number) {
  return apiRequest('/v1/learner/study-commitment', {
    method: 'POST',
    body: JSON.stringify({ dailyMinutes }),
  });
}

// ── Certificates ──────────────────────────────────────

export async function fetchCertificates() {
  return apiRequest('/v1/learner/certificates');
}

// ── Referral ──────────────────────────────────────────

export async function fetchReferralInfo() {
  return apiRequest('/v1/learner/referral');
}

export async function generateReferralCode() {
  return apiRequest('/v1/learner/referral/generate', {
    method: 'POST',
  });
}

// ── Expert Annotation Templates ───────────────────────

export async function fetchAnnotationTemplates(params?: { subtestCode?: string; criterionCode?: string; search?: string }) {
  const qs = new URLSearchParams();
  if (params?.subtestCode) qs.set('subtestCode', params.subtestCode);
  if (params?.criterionCode) qs.set('criterionCode', params.criterionCode);
  if (params?.search) qs.set('search', params.search);
  const q = qs.toString();
  return apiRequest(`/v1/expert/annotation-templates${q ? `?${q}` : ''}`);
}

export async function createAnnotationTemplate(payload: {
  subtestCode: string; criterionCode: string; label: string; templateText: string; isShared: boolean;
}) {
  return apiRequest('/v1/expert/annotation-templates', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateAnnotationTemplate(templateId: string, payload: {
  subtestCode: string; criterionCode: string; label: string; templateText: string; isShared: boolean;
}) {
  return apiRequest(`/v1/expert/annotation-templates/${encodeURIComponent(templateId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function deleteAnnotationTemplate(templateId: string) {
  return apiRequest(`/v1/expert/annotation-templates/${encodeURIComponent(templateId)}`, {
    method: 'DELETE',
  });
}

// ── Expert Amend Review ──────────────────────────────

export async function fetchAmendEligibility(reviewRequestId: string) {
  return apiRequest(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/amend-eligibility`);
}

export async function amendReview(reviewRequestId: string, payload: {
  scores: Record<string, number>;
  criterionComments: Record<string, string>;
  finalComment: string;
}) {
  return apiRequest(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/amend`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

// ── Expert Rework Chain ──────────────────────────────

export async function fetchReworkChain(reviewRequestId: string) {
  return apiRequest(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/rework-chain`);
}

// ── Expert Bulk Operations ────────────────────────────

export async function bulkClaimReviews(reviewRequestIds: string[]) {
  return apiRequest('/v1/expert/queue/bulk-claim', {
    method: 'POST',
    body: JSON.stringify({ reviewRequestIds }),
  });
}

export async function bulkReleaseReviews(reviewRequestIds: string[]) {
  return apiRequest('/v1/expert/queue/bulk-release', {
    method: 'POST',
    body: JSON.stringify({ reviewRequestIds }),
  });
}

// ── Expert Messaging ──────────────────────────────────

export async function fetchExpertMessageThreads() {
  return apiRequest('/v1/expert/messages');
}

export async function createExpertMessageThread(payload: {
  title: string; body: string; linkedReviewRequestId?: string;
  linkedCalibrationCaseId?: string; linkedLearnerId?: string;
}) {
  return apiRequest('/v1/expert/messages', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function fetchExpertMessageThread(threadId: string) {
  return apiRequest(`/v1/expert/messages/${encodeURIComponent(threadId)}`);
}

export async function postExpertMessageReply(threadId: string, body: string) {
  return apiRequest(`/v1/expert/messages/${encodeURIComponent(threadId)}/replies`, {
    method: 'POST',
    body: JSON.stringify({ body }),
  });
}

// ── Expert Compensation ───────────────────────────────

export async function fetchExpertCompensationSummary() {
  return apiRequest('/v1/expert/compensation');
}

export async function fetchExpertEarningsHistory(page?: number, pageSize?: number) {
  const qs = new URLSearchParams();
  if (page) qs.set('page', String(page));
  if (pageSize) qs.set('pageSize', String(pageSize));
  const q = qs.toString();
  return apiRequest(`/v1/expert/compensation/earnings${q ? `?${q}` : ''}`);
}

export async function fetchExpertPayouts() {
  return apiRequest('/v1/expert/compensation/payouts');
}

// ── Admin: Score Guarantee Claims ─────────────────────

export async function fetchAdminScoreGuaranteeClaims(params?: { status?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/admin/score-guarantee-claims?${qs}`);
}

export async function reviewScoreGuaranteeClaim(pledgeId: string, decision: 'approve' | 'reject', note?: string) {
  return apiRequest(`/v1/admin/score-guarantee-claims/${encodeURIComponent(pledgeId)}/review`, {
    method: 'POST',
    body: JSON.stringify({ decision, note }),
  });
}

// ── Private Speaking Sessions ─────────────────────────────

export interface PrivateSpeakingBookingResult {
  bookingId: string;
  checkoutSessionId?: string | null;
  checkoutUrl?: string | null;
  entitlementUsed: boolean;
  speakingSessionsRemaining?: number | null;
}

export interface PrivateSpeakingCalendarStatus {
  connected: boolean;
  provider?: string | null;
  calendarId?: string | null;
  connectedEmail?: string | null;
  connectedAt?: string | null;
  lastCheckedAt?: string | null;
  lastSyncedAt?: string | null;
  lastError?: string | null;
}

export interface PrivateSpeakingCalendarConnectResult {
  authorizationUrl: string;
  expiresAt: string;
}

export async function fetchPrivateSpeakingConfig() {
  return apiRequest('/v1/private-speaking/config');
}

export async function fetchPrivateSpeakingTutors() {
  return apiRequest('/v1/private-speaking/tutors');
}

export async function fetchPrivateSpeakingSlots(tutorProfileId: string, from: string, to: string) {
  return apiRequest(`/v1/private-speaking/tutors/${encodeURIComponent(tutorProfileId)}/slots?from=${from}&to=${to}`);
}

export async function fetchAllPrivateSpeakingSlots(from: string, to: string) {
  return apiRequest(`/v1/private-speaking/slots?from=${from}&to=${to}`);
}

export async function createPrivateSpeakingBooking(payload: {
  tutorProfileId: string;
  sessionStartUtc: string;
  durationMinutes: number;
  learnerTimezone: string;
  learnerNotes?: string;
  /** Candidate profession track (Medicine, Nursing, Pharmacy, Dentistry, Other). */
  professionTrack?: string | null;
  idempotencyKey: string;
  /** Speaking module rebuild (2026-06-11): "practice" (default) or "exam". */
  sessionFormat?: string | null;
  /** "paypal" pays the catalog price via embedded PayPal; omit/"entitlement" uses a credit. */
  paymentMethod?: 'paypal' | 'entitlement' | null;
}): Promise<PrivateSpeakingBookingResult> {
  return apiRequest<PrivateSpeakingBookingResult>('/v1/private-speaking/bookings', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function reschedulePrivateSpeakingBooking(bookingId: string, payload: {
  sessionStartUtc: string;
  learnerTimezone: string;
  learnerNotes?: string;
  idempotencyKey: string;
}): Promise<PrivateSpeakingBookingResult> {
  return apiRequest<PrivateSpeakingBookingResult>(`/v1/private-speaking/bookings/${encodeURIComponent(bookingId)}/reschedule`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function fetchLearnerPrivateSpeakingBookings(status?: string) {
  const qs = status ? `?status=${status}` : '';
  return apiRequest(`/v1/private-speaking/bookings${qs}`);
}

export async function fetchPrivateSpeakingBookingDetail(bookingId: string) {
  return apiRequest(`/v1/private-speaking/bookings/${encodeURIComponent(bookingId)}`);
}

export async function cancelPrivateSpeakingBooking(bookingId: string, reason?: string) {
  return apiRequest(`/v1/private-speaking/bookings/${encodeURIComponent(bookingId)}/cancel`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

export async function fetchPrivateSpeakingJoinToken(bookingId: string): Promise<LiveClassJoinToken> {
  return apiRequest<LiveClassJoinToken>(`/v1/private-speaking/bookings/${encodeURIComponent(bookingId)}/join-token`, {
    method: 'POST',
  });
}

export async function downloadPrivateSpeakingCalendarInvite(bookingId: string): Promise<Blob> {
  const path = `/v1/private-speaking/bookings/${encodeURIComponent(bookingId)}/calendar.ics`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    headers: await getHeaders(path, undefined, { json: false }),
  });
  if (!response.ok) {
    throw new ApiError(response.status, 'calendar_invite_download_failed', 'Could not download the calendar invite.', false);
  }
  return response.blob();
}

export async function ratePrivateSpeakingSession(bookingId: string, rating: number, feedback?: string) {
  return apiRequest(`/v1/private-speaking/bookings/${encodeURIComponent(bookingId)}/rate`, {
    method: 'POST',
    body: JSON.stringify({ rating, feedback }),
  });
}

// ── Private Speaking: Expert ──────────────────────────────

export async function fetchExpertPrivateSpeakingProfile() {
  return apiRequest('/v1/expert/private-speaking/profile');
}

export async function fetchExpertPrivateSpeakingSessions(status?: string) {
  const qs = status ? `?status=${status}` : '';
  return apiRequest(`/v1/expert/private-speaking/sessions${qs}`);
}

export async function fetchExpertPrivateSpeakingSessionDetail(bookingId: string) {
  return apiRequest(`/v1/expert/private-speaking/sessions/${encodeURIComponent(bookingId)}`);
}

export async function fetchExpertPrivateSpeakingAvailability() {
  return apiRequest('/v1/expert/private-speaking/availability');
}

export async function updateExpertPrivateSpeakingAvailability(payload: { dayOfWeek: number; startTime: string; endTime: string; effectiveFrom?: string; effectiveTo?: string }) {
  return apiRequest('/v1/expert/private-speaking/availability', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateExpertPrivateSpeakingAvailabilityRule(ruleId: string, payload: { dayOfWeek: number; startTime: string; endTime: string; effectiveFrom?: string | null; effectiveTo?: string | null; isActive: boolean }) {
  return apiRequest(`/v1/expert/private-speaking/availability/${encodeURIComponent(ruleId)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function deleteExpertPrivateSpeakingAvailability(ruleId: string) {
  return apiRequest(`/v1/expert/private-speaking/availability/${encodeURIComponent(ruleId)}`, { method: 'DELETE' });
}

export async function cancelExpertPrivateSpeakingSession(bookingId: string, reason?: string) {
  return apiRequest(`/v1/expert/private-speaking/sessions/${encodeURIComponent(bookingId)}/cancel`, {
    method: 'POST',
    body: JSON.stringify({ reason: reason || null }),
  });
}

export async function fetchExpertPrivateSpeakingJoinToken(bookingId: string): Promise<LiveClassJoinToken> {
  return apiRequest<LiveClassJoinToken>(`/v1/expert/private-speaking/sessions/${encodeURIComponent(bookingId)}/join-token`, {
    method: 'POST',
  });
}

export async function downloadExpertPrivateSpeakingCalendarInvite(bookingId: string): Promise<Blob> {
  const path = `/v1/expert/private-speaking/sessions/${encodeURIComponent(bookingId)}/calendar.ics`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    headers: await getHeaders(path, undefined, { json: false }),
  });
  if (!response.ok) {
    throw new ApiError(response.status, 'calendar_invite_download_failed', 'Could not download the calendar invite.', false);
  }
  return response.blob();
}

export async function fetchExpertPrivateSpeakingCalendarStatus(): Promise<PrivateSpeakingCalendarStatus> {
  return apiRequest<PrivateSpeakingCalendarStatus>('/v1/expert/private-speaking/calendar/status');
}

export async function connectExpertPrivateSpeakingGoogleCalendar(): Promise<PrivateSpeakingCalendarConnectResult> {
  return apiRequest<PrivateSpeakingCalendarConnectResult>('/v1/expert/private-speaking/calendar/google/connect', {
    method: 'POST',
  });
}

export async function disconnectExpertPrivateSpeakingCalendar(): Promise<{ disconnected: boolean }> {
  return apiRequest<{ disconnected: boolean }>('/v1/expert/private-speaking/calendar', {
    method: 'DELETE',
  });
}

// ── Private Speaking: Admin ───────────────────────────────

export async function fetchAdminPrivateSpeakingConfig() {
  return apiRequest('/v1/admin/private-speaking/config');
}

export async function updateAdminPrivateSpeakingConfig(payload: Record<string, unknown>) {
  return apiRequest('/v1/admin/private-speaking/config', { method: 'PUT', body: JSON.stringify(payload) });
}

export async function fetchAdminPrivateSpeakingStats() {
  return apiRequest('/v1/admin/private-speaking/stats');
}

export async function fetchAdminPrivateSpeakingTutors(activeOnly?: boolean) {
  const qs = activeOnly !== undefined ? `?activeOnly=${activeOnly}` : '';
  return apiRequest(`/v1/admin/private-speaking/tutors${qs}`);
}

export async function fetchAdminPrivateSpeakingTutor(profileId: string) {
  return apiRequest(`/v1/admin/private-speaking/tutors/${encodeURIComponent(profileId)}`);
}

export async function createAdminPrivateSpeakingTutor(payload: {
  expertUserId: string; displayName: string; timezone: string; bio?: string;
  priceOverrideMinorUnits?: number; slotDurationOverrideMinutes?: number; specialtiesJson?: string;
}) {
  return apiRequest('/v1/admin/private-speaking/tutors', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminPrivateSpeakingTutor(profileId: string, payload: Record<string, unknown>) {
  return apiRequest(`/v1/admin/private-speaking/tutors/${encodeURIComponent(profileId)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function fetchAdminPrivateSpeakingAvailability(profileId: string) {
  return apiRequest(`/v1/admin/private-speaking/tutors/${encodeURIComponent(profileId)}/availability`);
}

export async function createAdminPrivateSpeakingAvailabilityRule(profileId: string, payload: {
  dayOfWeek: number; startTime: string; endTime: string; effectiveFrom?: string; effectiveTo?: string;
}) {
  return apiRequest(`/v1/admin/private-speaking/tutors/${encodeURIComponent(profileId)}/availability`, {
    method: 'POST', body: JSON.stringify(payload),
  });
}

export async function deleteAdminPrivateSpeakingAvailabilityRule(profileId: string, ruleId: string) {
  return apiRequest(`/v1/admin/private-speaking/tutors/${encodeURIComponent(profileId)}/availability/${encodeURIComponent(ruleId)}`, {
    method: 'DELETE',
  });
}

export async function fetchAdminPrivateSpeakingBookings(params?: {
  tutorProfileId?: string; status?: string; learnerId?: string;
  from?: string; to?: string; page?: number; pageSize?: number;
}) {
  const qs = new URLSearchParams();
  if (params?.tutorProfileId) qs.set('tutorProfileId', params.tutorProfileId);
  if (params?.status) qs.set('status', params.status);
  if (params?.learnerId) qs.set('learnerId', params.learnerId);
  if (params?.from) qs.set('from', params.from);
  if (params?.to) qs.set('to', params.to);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/admin/private-speaking/bookings?${qs}`);
}

export async function cancelAdminPrivateSpeakingBooking(bookingId: string, reason?: string) {
  return apiRequest(`/v1/admin/private-speaking/bookings/${encodeURIComponent(bookingId)}/cancel`, {
    method: 'POST', body: JSON.stringify({ reason }),
  });
}

export async function completeAdminPrivateSpeakingBooking(bookingId: string) {
  return apiRequest(`/v1/admin/private-speaking/bookings/${encodeURIComponent(bookingId)}/complete`, { method: 'POST' });
}

export async function retryAdminPrivateSpeakingZoom(bookingId: string) {
  return apiRequest(`/v1/admin/private-speaking/bookings/${encodeURIComponent(bookingId)}/retry-zoom`, { method: 'POST' });
}

export async function adminOverridePrivateSpeakingRefund(
  bookingId: string,
  payload: { amountMinorUnits?: number | null; reason?: string | null },
) {
  return apiRequest(`/v1/admin/private-speaking/bookings/${encodeURIComponent(bookingId)}/override-refund`, {
    method: 'POST', body: JSON.stringify(payload),
  });
}

export async function adminManualReschedulePrivateSpeaking(
  bookingId: string,
  payload: { newSessionStartUtc: string; reason?: string | null },
) {
  return apiRequest(`/v1/admin/private-speaking/bookings/${encodeURIComponent(bookingId)}/manual-reschedule`, {
    method: 'POST', body: JSON.stringify(payload),
  });
}

export async function adminEditPrivateSpeakingBooking(
  bookingId: string,
  payload: { sessionStartUtc?: string | null; durationMinutes?: number | null; professionTrack?: string | null; tutorNotes?: string | null },
) {
  return apiRequest(`/v1/admin/private-speaking/bookings/${encodeURIComponent(bookingId)}`, {
    method: 'PUT', body: JSON.stringify(payload),
  });
}

export async function adminMarkPrivateSpeakingNoShow(bookingId: string) {
  return apiRequest(`/v1/admin/private-speaking/bookings/${encodeURIComponent(bookingId)}/mark-no-show`, { method: 'POST' });
}

export async function adminUpdatePrivateSpeakingAvailabilityRule(
  profileId: string,
  ruleId: string,
  payload: { dayOfWeek: number; startTime: string; endTime: string; effectiveFrom?: string | null; effectiveTo?: string | null; isActive: boolean },
) {
  return apiRequest(`/v1/admin/private-speaking/tutors/${encodeURIComponent(profileId)}/availability/${encodeURIComponent(ruleId)}`, {
    method: 'PUT', body: JSON.stringify(payload),
  });
}

export async function downloadAdminPrivateSpeakingBookingsCsv(params?: {
  tutorProfileId?: string; status?: string; learnerId?: string; from?: string; to?: string;
}): Promise<Blob> {
  const qs = new URLSearchParams();
  if (params?.tutorProfileId) qs.set('tutorProfileId', params.tutorProfileId);
  if (params?.status) qs.set('status', params.status);
  if (params?.learnerId) qs.set('learnerId', params.learnerId);
  if (params?.from) qs.set('from', params.from);
  if (params?.to) qs.set('to', params.to);
  const query = qs.toString();
  const path = `/v1/admin/private-speaking/bookings/export${query ? `?${query}` : ''}`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    headers: await getHeaders(path, undefined, { json: false }),
  });
  if (!response.ok) {
    throw new ApiError(response.status, 'bookings_export_failed', 'Could not export bookings.', false);
  }
  return response.blob();
}

export async function fetchAdminPrivateSpeakingAuditLogs(params?: { bookingId?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.bookingId) qs.set('bookingId', params.bookingId);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 50));
  return apiRequest(`/v1/admin/private-speaking/audit-logs?${qs}`);
}

// ── Zoom Live Classes ───────────────────────────────────

export interface LiveClassSessionSummary {
  id: string;
  scheduledStartAt: string;
  scheduledEndAt: string;
  capacity: number;
  enrolledCount: number;
  status: string;
  isEnrolled: boolean;
  isJoinAvailable: boolean;
  creditCost: number;
  /** Only present on admin endpoints */
  zoomMeetingId?: number | null;
  /** Only present on admin endpoints */
  zoomError?: string | null;
}

export interface LiveClassListItem {
  id: string;
  slug: string;
  title: string;
  titleAr?: string | null;
  description: string;
  descriptionAr?: string | null;
  type: string;
  professionTrack: string;
  level: string;
  tutorProfileId?: string | null;
  tutorDisplayName?: string | null;
  creditCost: number;
  status: string;
  coverImageUrl?: string | null;
  sessions: LiveClassSessionSummary[];
}

export interface LiveClassDetail extends LiveClassListItem {
  defaultDurationMinutes: number;
  defaultCapacity: number;
  tags: string[];
}

export interface LiveClassEnrollment {
  id: string;
  classSessionId: string;
  userId: string;
  enrolledAt: string;
  creditsCharged: number;
  status: string;
  cancelledAt?: string | null;
  cancellationReason?: string | null;
}

export interface LiveClassJoinToken {
  provider: 'zoom';
  sdkKey?: string | null;
  signature?: string | null;
  meetingNumber: string;
  userName: string;
  userEmail?: string | null;
  role: number;
  passWord?: string | null;
  zak?: string | null;
  joinUrl?: string | null;
  expiresAt: string;
}

export interface LiveClassRecording {
  id: string;
  classSessionId: string;
  status: string;
  videoUrl?: string | null;
  transcriptUrl?: string | null;
  transcriptText?: string | null;
  aiSummary?: string | null;
  aiSummaryAr?: string | null;
  chapters: Array<{ startSeconds: number; title: string; summary: string }>;
  actionItems: string[];
  expiresAt?: string | null;
}

export interface AdminLiveClassUpsertPayload {
  title: string;
  titleAr?: string | null;
  description: string;
  descriptionAr?: string | null;
  type: string;
  professionTrack: string;
  level: string;
  tutorProfileId?: string | null;
  scheduledStartAt: string;
  durationMinutes: number;
  capacity: number;
  creditCost: number;
  coverImageUrl?: string | null;
  tags?: string[];
  autoPublish?: boolean;
}

export interface LiveClassQueryParams {
  professionTrack?: string;
  type?: string;
  tutorProfileId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

function liveClassQuery(params?: LiveClassQueryParams) {
  const qs = new URLSearchParams();
  if (params?.professionTrack) qs.set('professionTrack', params.professionTrack);
  if (params?.type) qs.set('type', params.type);
  if (params?.tutorProfileId) qs.set('tutorProfileId', params.tutorProfileId);
  if (params?.from) qs.set('from', params.from);
  if (params?.to) qs.set('to', params.to);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return qs.toString();
}

export async function fetchLiveClasses(params?: LiveClassQueryParams): Promise<LiveClassListItem[]> {
  return apiRequest<LiveClassListItem[]>(`/v1/classes?${liveClassQuery(params)}`);
}

export async function fetchLiveClassDetail(idOrSlug: string): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>(`/v1/classes/${encodeURIComponent(idOrSlug)}`);
}

export async function enrollLiveClassSession(sessionId: string, idempotencyKey?: string): Promise<LiveClassEnrollment> {
  return apiRequest<LiveClassEnrollment>(`/v1/classes/sessions/${encodeURIComponent(sessionId)}/enroll`, {
    method: 'POST',
    body: JSON.stringify({ idempotencyKey }),
  });
}

export async function cancelLiveClassEnrollment(sessionId: string, reason?: string): Promise<LiveClassEnrollment> {
  return apiRequest<LiveClassEnrollment>(`/v1/classes/sessions/${encodeURIComponent(sessionId)}/cancel-enrollment`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

export async function fetchLiveClassJoinToken(sessionId: string): Promise<LiveClassJoinToken> {
  return apiRequest<LiveClassJoinToken>(`/v1/classes/sessions/${encodeURIComponent(sessionId)}/join-token`, {
    method: 'POST',
  });
}

export async function fetchMyUpcomingLiveClasses(): Promise<LiveClassListItem[]> {
  return apiRequest<LiveClassListItem[]>('/v1/classes/me/upcoming');
}

export async function fetchMyPastLiveClasses(): Promise<LiveClassListItem[]> {
  return apiRequest<LiveClassListItem[]>('/v1/classes/me/past');
}

export async function fetchLiveClassRecording(sessionId: string): Promise<LiveClassRecording> {
  return apiRequest<LiveClassRecording>(`/v1/classes/sessions/${encodeURIComponent(sessionId)}/recording`);
}

export async function fetchExpertLiveClasses(): Promise<LiveClassListItem[]> {
  return apiRequest<LiveClassListItem[]>('/v1/expert/live-classes');
}

export async function fetchExpertLiveClassJoinToken(sessionId: string): Promise<LiveClassJoinToken> {
  return apiRequest<LiveClassJoinToken>(`/v1/expert/live-classes/sessions/${encodeURIComponent(sessionId)}/join-token`, {
    method: 'POST',
  });
}

export async function fetchAdminLiveClasses(params?: LiveClassQueryParams): Promise<LiveClassListItem[]> {
  return apiRequest<LiveClassListItem[]>(`/v1/admin/live-classes?${liveClassQuery({ ...params, pageSize: params?.pageSize ?? 50 })}`);
}

export async function createAdminLiveClass(payload: AdminLiveClassUpsertPayload): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>('/v1/admin/live-classes', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function publishAdminLiveClass(liveClassId: string): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>(`/v1/admin/live-classes/${encodeURIComponent(liveClassId)}/publish`, {
    method: 'POST',
  });
}

export async function updateAdminLiveClassSession(sessionId: string, payload: { scheduledStartAt?: string; durationMinutes?: number; capacity?: number; cancellationReason?: string }): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>(`/v1/admin/live-classes/sessions/${encodeURIComponent(sessionId)}`, {
    method: 'PATCH',
    body: JSON.stringify(payload),
  });
}

export async function cancelAdminLiveClassSession(sessionId: string, reason?: string): Promise<void> {
  await apiRequest(`/v1/admin/live-classes/sessions/${encodeURIComponent(sessionId)}/cancel`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

export async function fetchAdminLiveClassAnalytics() {
  return apiRequest('/v1/admin/live-classes/analytics');
}

export async function fetchAdminLiveClassDetail(idOrSlug: string): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>(`/v1/admin/live-classes/${encodeURIComponent(idOrSlug)}`);
}

export async function addAdminLiveClassSession(
  classId: string,
  payload: { scheduledStartAt: string; durationMinutes?: number; capacity?: number },
): Promise<LiveClassSessionSummary> {
  return apiRequest<LiveClassSessionSummary>(`/v1/admin/live-classes/${encodeURIComponent(classId)}/sessions`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function retryAdminLiveClassSessionZoom(sessionId: string): Promise<void> {
  await apiRequest(`/v1/admin/live-classes/sessions/${encodeURIComponent(sessionId)}/retry-zoom`, {
    method: 'POST',
  });
}

// ── Tutor (Zoom-backed live classes — wave B1) ──────────

export type DayOfWeekString =
  | 'Sunday'
  | 'Monday'
  | 'Tuesday'
  | 'Wednesday'
  | 'Thursday'
  | 'Friday'
  | 'Saturday';

export interface TutorProfile {
  id: string;
  userId: string;
  displayName: string;
  displayNameAr?: string | null;
  bio: string;
  bioAr?: string | null;
  avatarUrl?: string | null;
  specialties: string[];
  languages: string[];
  hourlyRateUsd?: number | null;
  timeZone: string;
  zoomUserId?: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface TutorUpsertPayload {
  displayName: string;
  displayNameAr?: string | null;
  bio?: string | null;
  bioAr?: string | null;
  avatarUrl?: string | null;
  specialties?: string[];
  languages?: string[];
  hourlyRateUsd?: number | null;
  timeZone?: string | null;
  isActive?: boolean | null;
}

export interface TutorAvailabilitySlot {
  id: string;
  /** Server emits .NET DayOfWeek either as enum number (0–6) or name. We accept both. */
  dayOfWeek: number | DayOfWeekString;
  /** TimeOnly serializes as `HH:mm:ss`. */
  startTime: string;
  /** TimeOnly serializes as `HH:mm:ss`. */
  endTime: string;
  isActive: boolean;
}

export interface TutorAvailabilityUpsertPayload {
  /** Accepts the .NET DayOfWeek enum index or name. */
  dayOfWeek: number | DayOfWeekString;
  startTime: string;
  endTime: string;
  isActive: boolean;
}

export interface TutorEarningsLine {
  classSessionId: string;
  liveClassId: string;
  classTitle: string;
  scheduledStartAt: string;
  attendedCount: number;
  creditCost: number;
  creditUsdValue: number;
  revenueSharePercent: number;
  grossUsd: number;
  netUsd: number;
}

export interface TutorEarnings {
  from?: string | null;
  to?: string | null;
  grossUsd: number;
  netUsd: number;
  revenueSharePercent: number;
  lines: TutorEarningsLine[];
}

export interface TutorClassCreatePayload {
  title: string;
  titleAr?: string | null;
  description: string;
  descriptionAr?: string | null;
  type: string;
  professionTrack: string;
  level: string;
  scheduledStartAt: string;
  durationMinutes: number;
  capacity: number;
  creditCost: number;
  coverImageUrl?: string | null;
  tags?: string[];
  autoPublish?: boolean;
}

export interface TutorClassUpdatePayload {
  title?: string | null;
  titleAr?: string | null;
  description?: string | null;
  descriptionAr?: string | null;
  coverImageUrl?: string | null;
  creditCost?: number | null;
  defaultCapacity?: number | null;
  defaultDurationMinutes?: number | null;
  tags?: string[] | null;
}

export interface TutorClassSessionCreatePayload {
  scheduledStartAt: string;
  durationMinutes?: number | null;
  capacity?: number | null;
}

export interface TutorClassSessionUpdatePayload {
  scheduledStartAt?: string;
  durationMinutes?: number;
  capacity?: number;
  cancellationReason?: string;
}

export interface TutorAttendanceLine {
  userId: string;
  displayName?: string | null;
  joinedAt: string;
  leftAt?: string | null;
  durationSeconds: number;
}

export interface ClassFeedbackSubmitPayload {
  rating: number;
  comment?: string | null;
  recommendToFriend?: boolean | null;
}

export interface ClassFeedbackEntry {
  id: string;
  classSessionId: string;
  userId: string;
  rating: number;
  comment?: string | null;
  recommendToFriend: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ClassWaitlistEntry {
  id: string;
  classSessionId: string;
  userId: string;
  position: number;
  joinedAt: string;
}

export interface LiveClassTranscript {
  classSessionId: string;
  transcriptText: string;
  processedAt?: string | null;
}

export async function fetchTutorProfile(): Promise<TutorProfile | null> {
  try {
    return await apiRequest<TutorProfile>('/v1/tutor/me');
  } catch (err) {
    if (isApiError(err) && err.status === 404) return null;
    throw err;
  }
}

export async function createTutorProfile(payload: TutorUpsertPayload): Promise<TutorProfile> {
  return apiRequest<TutorProfile>('/v1/tutor/me', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateTutorProfile(payload: TutorUpsertPayload): Promise<TutorProfile> {
  return apiRequest<TutorProfile>('/v1/tutor/me', {
    method: 'PATCH',
    body: JSON.stringify(payload),
  });
}

export async function fetchTutorAvailability(): Promise<TutorAvailabilitySlot[]> {
  return apiRequest<TutorAvailabilitySlot[]>('/v1/tutor/me/availability');
}

export async function replaceTutorAvailability(
  slots: TutorAvailabilityUpsertPayload[],
): Promise<TutorAvailabilitySlot[]> {
  return apiRequest<TutorAvailabilitySlot[]>('/v1/tutor/me/availability', {
    method: 'PUT',
    body: JSON.stringify(slots),
  });
}

export async function fetchTutorEarnings(from?: string, to?: string): Promise<TutorEarnings> {
  const qs = new URLSearchParams();
  if (from) qs.set('from', from);
  if (to) qs.set('to', to);
  const tail = qs.toString();
  return apiRequest<TutorEarnings>(`/v1/tutor/me/earnings${tail ? `?${tail}` : ''}`);
}

export async function provisionTutorZoomUser(): Promise<{ zoomUserId: string | null }> {
  return apiRequest<{ zoomUserId: string | null }>('/v1/tutor/me/zoom-user', {
    method: 'POST',
  });
}

export async function fetchTutorClasses(): Promise<LiveClassListItem[]> {
  return apiRequest<LiveClassListItem[]>('/v1/tutor/me/classes');
}

export async function createTutorClass(payload: TutorClassCreatePayload): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>('/v1/tutor/me/classes', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateTutorClass(
  classId: string,
  payload: TutorClassUpdatePayload,
): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>(`/v1/tutor/me/classes/${encodeURIComponent(classId)}`, {
    method: 'PATCH',
    body: JSON.stringify(payload),
  });
}

export async function addTutorClassSession(
  classId: string,
  payload: TutorClassSessionCreatePayload,
): Promise<LiveClassSessionSummary> {
  return apiRequest<LiveClassSessionSummary>(
    `/v1/tutor/me/classes/${encodeURIComponent(classId)}/sessions`,
    {
      method: 'POST',
      body: JSON.stringify(payload),
    },
  );
}

export async function updateTutorClassSession(
  sessionId: string,
  payload: TutorClassSessionUpdatePayload,
): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>(
    `/v1/tutor/me/classes/sessions/${encodeURIComponent(sessionId)}`,
    {
      method: 'PATCH',
      body: JSON.stringify(payload),
    },
  );
}

export async function cancelTutorClassSession(sessionId: string): Promise<void> {
  await apiRequest(`/v1/tutor/me/classes/sessions/${encodeURIComponent(sessionId)}`, {
    method: 'DELETE',
  });
}

export async function fetchTutorSessionAttendance(sessionId: string): Promise<TutorAttendanceLine[]> {
  return apiRequest<TutorAttendanceLine[]>(
    `/v1/tutor/me/classes/sessions/${encodeURIComponent(sessionId)}/attendance`,
  );
}

export async function submitClassFeedback(
  sessionId: string,
  payload: ClassFeedbackSubmitPayload,
): Promise<ClassFeedbackEntry> {
  return apiRequest<ClassFeedbackEntry>(
    `/v1/classes/sessions/${encodeURIComponent(sessionId)}/feedback`,
    {
      method: 'POST',
      body: JSON.stringify(payload),
    },
  );
}

export async function joinClassWaitlist(sessionId: string): Promise<ClassWaitlistEntry> {
  return apiRequest<ClassWaitlistEntry>(
    `/v1/classes/sessions/${encodeURIComponent(sessionId)}/waitlist`,
    { method: 'POST' },
  );
}

export async function leaveClassWaitlist(sessionId: string): Promise<void> {
  await apiRequest(`/v1/classes/sessions/${encodeURIComponent(sessionId)}/waitlist`, {
    method: 'DELETE',
  });
}

export async function fetchClassTranscript(sessionId: string): Promise<LiveClassTranscript> {
  return apiRequest<LiveClassTranscript>(
    `/v1/me/classes/sessions/${encodeURIComponent(sessionId)}/transcript`,
  );
}

// ── Orphan Endpoint Wiring ────────────────────────────

export async function fetchStudyPlanDrift() {
  return apiRequest('/v1/learner/study-plan/drift');
}

export async function regenerateStudyPlan(): Promise<ApiRecord> {
  return apiRequest<ApiRecord>('/v1/study-plan/regenerate', { method: 'POST' });
}

export async function fetchReadinessRisk() {
  return apiRequest('/v1/learner/readiness/risk');
}

export async function applyStreakFreeze(): Promise<{ applied: boolean; message: string }> {
  return apiRequest('/v1/learner/engagement/streak-freeze', { method: 'POST' });
}

export async function fetchFluencyTimeline(attemptId: string) {
  return apiRequest(`/v1/learner/speaking/${encodeURIComponent(attemptId)}/fluency-timeline`);
}

export async function fetchDiagnosticPersonalization() {
  return apiRequest('/v1/learner/diagnostic-personalization');
}

// ── Sponsor Dashboard ──

export interface SponsorDashboardData {
  sponsorName: string;
  organizationName: string | null;
  learnersSponsored: number;
  activeSponsorships: number;
  pendingSponsorships: number;
  totalSpend: number;
  currency: string | null;
}

export interface SponsoredLearner {
  id: string;
  learnerEmail: string;
  learnerUserId: string | null;
  status: string;
  createdAt: string;
  revokedAt: string | null;
}

export interface SponsorInvoice {
  id: string;
  sponsorshipId: string;
  learnerUserId: string;
  learnerEmail: string;
  gateway: string;
  gatewayTransactionId: string;
  transactionType: string;
  productType: string | null;
  productId: string | null;
  amount: number;
  currency: string;
  status: string;
  createdAt: string;
}

export interface SponsorBillingData {
  sponsorName: string;
  organizationName: string | null;
  totalSponsorships: number;
  totalSpend: number;
  currentMonthSpend: number;
  currency: string | null;
  billingCycle: string;
  invoices: SponsorInvoice[];
}

export async function fetchSponsorDashboard(): Promise<SponsorDashboardData> {
  return apiRequest<SponsorDashboardData>('/v1/sponsor/dashboard');
}

export async function fetchSponsoredLearners(params?: { page?: number; pageSize?: number }): Promise<{ items: SponsoredLearner[]; total: number; page: number; pageSize: number }> {
  const queryParams = new URLSearchParams();
  if (params?.page) queryParams.set('page', String(params.page));
  if (params?.pageSize) queryParams.set('pageSize', String(params.pageSize));
  const qs = queryParams.toString();
  return apiRequest(`/v1/sponsor/learners${qs ? `?${qs}` : ''}`);
}

export async function inviteSponsoredLearner(email: string): Promise<SponsoredLearner> {
  return apiRequest<SponsoredLearner>('/v1/sponsor/learners/invite', {
    method: 'POST',
    body: JSON.stringify({ email }),
  });
}

export async function removeSponsoredLearner(id: string): Promise<{ revoked: boolean }> {
  return apiRequest(`/v1/sponsor/learners/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  });
}

export async function fetchSponsorBilling(): Promise<SponsorBillingData> {
  return apiRequest<SponsorBillingData>('/v1/sponsor/billing');
}



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
  return apiRequest(`/v1/admin/rulebooks/${encodeURIComponent(id)}`, { method: 'DELETE' });
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

// === SUBAGENT_B: listening-authoring START ===
// Typed wrappers for the Listening authoring workspace.
// Mirrors backend routes in:
//   - Endpoints/ListeningAuthoringAdminEndpoints.cs
//   - Endpoints/ListeningAdminAnalyticsEndpoints.cs
// Canonical paper shape: A1=12, A2=12, B=6, C1=6, C2=6 → 42 items.

import type {
  ListeningAuthoredExtract,
  ListeningAuthoredQuestion,
  ListeningAuthoredQuestionList,
  ListeningBackfillAllResponse,
  ListeningExtractPatch,
  ListeningExtractsResponse,
  ListeningQuestionPatch,
  ListeningValidationReport,
} from '@/lib/types/admin/listening-authoring';

const lap = (paperId: string) =>
  `/v1/admin/papers/${encodeURIComponent(paperId)}/listening`;

// ── Validate ────────────────────────────────────────────────────────────
export async function adminListeningValidate(paperId: string) {
  return apiRequest<ListeningValidationReport>(`${lap(paperId)}/validate`);
}

// ── Structure (42 items) ────────────────────────────────────────────────
export async function adminListeningGetStructure(paperId: string) {
  return apiRequest<ListeningAuthoredQuestionList>(`${lap(paperId)}/structure`);
}

export async function adminListeningReplaceStructure(
  paperId: string,
  questions: ListeningAuthoredQuestion[],
) {
  return apiRequest<ListeningAuthoredQuestionList>(`${lap(paperId)}/structure`, {
    method: 'PUT',
    body: JSON.stringify({ questions }),
  });
}

export async function adminListeningPatchQuestion(
  paperId: string,
  questionId: string,
  patch: ListeningQuestionPatch,
) {
  return apiRequest<ListeningAuthoredQuestionList>(
    `${lap(paperId)}/structure/${encodeURIComponent(questionId)}`,
    { method: 'PATCH', body: JSON.stringify(patch) },
  );
}

// ── Extracts (5 per paper) ──────────────────────────────────────────────
export async function adminListeningGetExtracts(paperId: string) {
  return apiRequest<ListeningExtractsResponse>(`${lap(paperId)}/extracts`);
}

export async function adminListeningReplaceExtracts(
  paperId: string,
  extracts: ListeningAuthoredExtract[],
) {
  return apiRequest<ListeningExtractsResponse>(`${lap(paperId)}/extracts`, {
    method: 'PUT',
    body: JSON.stringify({ extracts }),
  });
}

export async function adminListeningPatchExtract(
  paperId: string,
  extractCode: string,
  patch: ListeningExtractPatch,
) {
  return apiRequest<ListeningExtractsResponse>(
    `${lap(paperId)}/extracts/${encodeURIComponent(extractCode)}`,
    { method: 'PATCH', body: JSON.stringify(patch) },
  );
}

// ── System-wide listening admin endpoints ───────────────────────────────
export async function adminListeningBackfillAll() {
  return apiRequest<ListeningBackfillAllResponse>(`/v1/admin/listening/backfill`, {
    method: 'POST',
  });
}

export async function adminListeningGetAnalytics(days?: number) {
  const qs = typeof days === 'number' ? `?days=${days}` : '';
  return apiRequest<unknown>(`/v1/admin/listening/analytics${qs}`);
}

export async function adminListeningExportAttempt(attemptId: string) {
  return apiRequest<unknown>(
    `/v1/admin/listening/attempts/${encodeURIComponent(attemptId)}/export`,
  );
}
// === SUBAGENT_B: listening-authoring END ===

// === SUBAGENT_C: bulk-import-and-generation START ===
// Typed wrappers for the bulk-import (ZIP), rulebook-import, and
// content-generation orchestration endpoints used by the Wave-2 admin UI
// (`/admin/content/papers/import-zip`, `/admin/rulebooks/import`,
// `/admin/content/generation/jobs`). Rulebook list/publish/import helpers
// already exist above (`adminListRulebooks`, `adminImportRulebook`,
// `adminPublishRulebook`) and are re-used by the new pages without
// duplication.
import type {
  BulkImportApprovalInput,
  BulkImportCommitResult,
  BulkImportSessionResponse,
  GenerationJobListResponse,
  GenerationJobSummary,
  QueueGenerationInput,
} from '@/lib/types/admin/bulk-import';

/**
 * Stage a ZIP payload for bulk content import. The backend accepts a single
 * `multipart/form-data` field named `file`; we drop the JSON Content-Type so
 * the browser produces the correct boundary. Returns the parsed session +
 * manifest so the UI can render an approval table before commit.
 */
export async function adminStartZipImport(
  file: File,
): Promise<BulkImportSessionResponse> {
  const form = new FormData();
  form.append('file', file, file.name);
  return apiRequest<BulkImportSessionResponse>(
    '/v1/admin/imports/zip',
    { method: 'POST', body: form },
    { json: false },
  );
}

/**
 * Commit a previously-staged ZIP import session with the admin's per-paper
 * approval decisions. `approvals` MUST include one entry per proposalId
 * returned by `adminStartZipImport`; the backend treats unknown ids as
 * skipped and refuses unknown sessions / cross-admin commits.
 */
export async function adminCommitZipImport(
  sessionId: string,
  approvals: BulkImportApprovalInput[],
): Promise<BulkImportCommitResult> {
  return apiRequest<BulkImportCommitResult>(
    `/v1/admin/imports/zip/${encodeURIComponent(sessionId)}/commit`,
    { method: 'POST', body: JSON.stringify(approvals) },
  );
}

/**
 * Abort an in-flight chunked upload session. The dedicated ZIP-import flow
 * does not use chunked uploads (the `/imports/zip` endpoint takes a single
 * multipart payload directly), but this wrapper is exported so the UI can
 * discard orphaned upload sessions if a different flow staged them.
 */
export async function adminDiscardUpload(uploadId: string): Promise<void> {
  await apiRequest<void>(
    `/v1/admin/uploads/${encodeURIComponent(uploadId)}`,
    { method: 'DELETE' },
  );
}

/**
 * Convenience wrapper that returns the strongly-typed jobs list. The
 * underlying `fetchContentGenerationJobs(page, pageSize)` helper already
 * exists above and is kept unchanged; this wrapper just narrows the return
 * type so the new jobs page can avoid `unknown` casts.
 */
export async function adminListGenerationJobs(
  page = 1,
  pageSize = 20,
): Promise<GenerationJobListResponse> {
  return apiRequest<GenerationJobListResponse>(
    `/v1/admin/content/generation-jobs?page=${page}&pageSize=${pageSize}`,
  );
}

export async function adminGetGenerationJob(
  jobId: string,
): Promise<GenerationJobSummary> {
  return apiRequest<GenerationJobSummary>(
    `/v1/admin/content/generation-jobs/${encodeURIComponent(jobId)}`,
  );
}

/**
 * Strongly-typed wrapper around `POST /v1/admin/content/generate`. The
 * existing `queueContentGeneration` helper above accepts the same shape but
 * returns `unknown`; this wrapper documents the field set the new launcher
 * UI uses and narrows the return type.
 */
export async function adminQueueContentGeneration(
  payload: QueueGenerationInput,
): Promise<{ jobId?: string } & Record<string, unknown>> {
  return apiRequest<{ jobId?: string } & Record<string, unknown>>(
    '/v1/admin/content/generate',
    { method: 'POST', body: JSON.stringify(payload) },
  );
}
// === SUBAGENT_C: bulk-import-and-generation END ===

// === SUBAGENT_E: bulk-ops START ===
// Typed helpers used by the Wave-2 bulk admin pages:
//   - app/admin/content/mocks/bulk/page.tsx
//   - app/admin/content/vocabulary/publish-batch/page.tsx
//   - app/admin/content/papers/republish-drafts/page.tsx
// These are thin re-exports / wrappers around endpoints that already exist
// on the backend. They live behind the SUBAGENT_E marker so future merges
// stay non-overlapping with SUBAGENT_B/C/D blocks.

/** Minimal projection of /v1/admin/papers list rows used by the bulk pages. */
export interface AdminBulkPaperRow {
  id: string;
  title: string;
  subtestCode: string;
  status: 'Draft' | 'InReview' | 'Published' | 'Archived';
  professionId: string | null;
  appliesToAllProfessions: boolean;
  difficulty?: string | null;
  sourceProvenance?: string | null;
  updatedAt?: string | null;
}

/** Lightweight draft vocab row used by the publish-batch UI. */
export interface AdminBulkVocabRow {
  id: string;
  term: string;
  definition: string | null;
  category: string;
  professionId: string | null;
  difficulty?: string | null;
  exampleSentence: string | null;
  status: 'draft' | 'active' | 'archived';
}

/** Full vocab item detail (subset of fields the publish-batch UI consumes). */
export interface AdminBulkVocabDetail extends AdminBulkVocabRow {
  ipaPronunciation: string | null;
  audioUrl: string | null;
  contextNotes: string | null;
  sourceProvenance: string | null;
}

/** Minimal mock-bundle row used by the bulk page to count existing bundles. */
export interface AdminBulkMockBundleRow {
  id: string;
  title: string;
  mockType: string;
  status: string;
  professionId: string | null;
}

/** Bulk list papers (admin). Returns flat array — backend does not paginate. */
export async function adminBulkListPapers(query: {
  subtest?: string;
  profession?: string;
  status?: 'Draft' | 'Published' | 'Archived' | 'InReview';
  pageSize?: number;
}): Promise<AdminBulkPaperRow[]> {
  const qs = new URLSearchParams();
  if (query.subtest) qs.set('subtest', query.subtest);
  if (query.profession) qs.set('profession', query.profession);
  if (query.status) qs.set('status', query.status);
  if (query.pageSize) qs.set('pageSize', String(query.pageSize));
  const suffix = qs.toString();
  return apiRequest<AdminBulkPaperRow[]>(
    `/v1/admin/papers${suffix ? `?${suffix}` : ''}`,
  );
}

/** Bulk list draft vocab items (paginated). */
export async function adminBulkListDraftVocab(params: {
  page?: number;
  pageSize?: number;
  profession?: string;
  category?: string;
  search?: string;
}): Promise<{ total: number; page: number; pageSize: number; items: AdminBulkVocabRow[] }> {
  const qs = new URLSearchParams();
  qs.set('status', 'draft');
  if (params.page) qs.set('page', String(params.page));
  if (params.pageSize) qs.set('pageSize', String(params.pageSize));
  if (params.profession) qs.set('profession', params.profession);
  if (params.category) qs.set('category', params.category);
  if (params.search) qs.set('search', params.search);
  return apiRequest(`/v1/admin/vocabulary/items?${qs.toString()}`);
}

/** Get one vocab item with full detail (includes IPA / provenance / audio). */
export async function adminBulkGetVocabItem(itemId: string) {
  return apiRequest<AdminBulkVocabDetail>(
    `/v1/admin/vocabulary/items/${encodeURIComponent(itemId)}`,
  );
}

/**
 * Update one vocab item. Pass `{ status: 'active' }` to publish; the backend's
 * EnforceVocabularyPublishGate validates required fields and may 4xx.
 */
export async function adminBulkUpdateVocabItem(
  itemId: string,
  body: Record<string, unknown>,
) {
  return apiRequest<AdminBulkVocabDetail>(
    `/v1/admin/vocabulary/items/${encodeURIComponent(itemId)}`,
    { method: 'PUT', body: JSON.stringify(body) },
  );
}

/** List mock bundles (used by the bulk-mocks page to count existing rows). */
export async function adminBulkListMockBundles(query: {
  mockType?: string;
  status?: string;
} = {}) {
  const qs = new URLSearchParams();
  if (query.mockType) qs.set('mockType', query.mockType);
  if (query.status) qs.set('status', query.status);
  const suffix = qs.toString();
  return apiRequest<AdminBulkMockBundleRow[]>(
    `/v1/admin/mock-bundles${suffix ? `?${suffix}` : ''}`,
  );
}
// === SUBAGENT_E: bulk-ops END ===

// === SUBAGENT_D: speaking-conv-pron START ===
// Typed helpers for the Wave-2 SUBAGENT D admin UI:
//   - Speaking workspace + create + backfill pages
//   - Conversation AI-draft + bulk-runner
//   - Pronunciation bulk-runner + analytics
//
// Backend status notes (verified against backend/src/OetLearner.Api):
//   * Speaking list / paper CRUD / chunked uploads / publish / unarchive
//     are already exposed via /v1/admin/papers/* and consumed via the
//     `lib/content-upload-api.ts` helpers. We re-export thin wrappers here
//     so the new pages can stay self-contained.
//   * `POST /v1/admin/conversation/templates/ai-draft` was added by
//     SUBAGENT_BACKEND (see `adminConversationAiDraft` above).
//   * `POST /v1/admin/pronunciation/drills/ai-draft` exists (see
//     `adminPronunciationAiDraft` above).
//   * NO speaking asset-backfill endpoint exists (the
//     `scripts/admin/generate-speaking-assets.mjs` Node script is the
//     only mechanism). Surface as "missing backend endpoint" in the UI.
//   * NO pronunciation analytics endpoint exists. Surface the same way.

export interface AdminSpeakingPaperRow {
  id: string;
  subtestCode: string;
  title: string;
  slug: string;
  professionId: string | null;
  appliesToAllProfessions: boolean;
  difficulty: string;
  status: 'Draft' | 'InReview' | 'Published' | 'Archived';
  cardType: string | null;
  tagsCsv: string;
  updatedAt: string;
  publishedAt: string | null;
}

export interface AdminSpeakingListResult {
  items: AdminSpeakingPaperRow[];
}

export async function adminListSpeakingPapers(params?: {
  status?: string;
  profession?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}): Promise<AdminSpeakingListResult> {
  const qs = new URLSearchParams();
  qs.set('subtest', 'speaking');
  if (params?.status) qs.set('status', params.status);
  if (params?.profession) qs.set('profession', params.profession);
  if (params?.search) qs.set('search', params.search);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 100));
  const data = await apiRequest<AdminSpeakingPaperRow[] | AdminSpeakingListResult>(
    `/v1/admin/papers?${qs.toString()}`,
  );
  if (Array.isArray(data)) return { items: data };
  return data;
}

export interface AdminSpeakingCreatePayload {
  title: string;
  professionId?: string | null;
  appliesToAllProfessions?: boolean;
  difficulty?: string;
  examCode?: string | null;
  estimatedDurationMinutes?: number;
  sourceProvenance?: string;
}

export async function adminCreateSpeakingPaper(body: AdminSpeakingCreatePayload) {
  const payload = {
    subtestCode: 'speaking',
    title: body.title,
    professionId: body.appliesToAllProfessions === false ? body.professionId ?? null : null,
    appliesToAllProfessions: body.appliesToAllProfessions ?? true,
    difficulty: body.difficulty ?? 'standard',
    estimatedDurationMinutes: body.estimatedDurationMinutes ?? 12,
    priority: 0,
    tagsCsv: body.examCode ? `exam:${body.examCode}` : null,
    sourceProvenance:
      body.sourceProvenance ?? 'admin-authored:speaking-workspace',
  };
  return apiRequest<{ id: string; title: string; slug: string }>(
    '/v1/admin/papers',
    { method: 'POST', body: JSON.stringify(payload) },
  );
}

export interface AdminSpeakingStructure {
  candidateCard?: Record<string, unknown> | null;
  interlocutorCard?: Record<string, unknown> | null;
  warmUpQuestions?: string[];
  prepTimeSeconds?: number;
  roleplayTimeSeconds?: number;
  patientEmotion?: string;
  communicationGoal?: string;
  clinicalTopic?: string;
  criteriaFocus?: string[];
  complianceNotes?: string;
}

export interface AdminSpeakingStructureResponse {
  paperId: string;
  structure: AdminSpeakingStructure;
  validation?: {
    isPublishReady?: boolean;
    isValid?: boolean;
    issues?: Array<{ code: string; severity: string; message: string }>;
  };
  updatedAt?: string;
}

export async function adminGetSpeakingStructure(paperId: string) {
  return apiRequest<AdminSpeakingStructureResponse>(
    `/v1/admin/papers/${encodeURIComponent(paperId)}/speaking-structure`,
  );
}

export async function adminUpdateSpeakingStructure(
  paperId: string,
  structure: AdminSpeakingStructure,
) {
  return apiRequest<AdminSpeakingStructureResponse>(
    `/v1/admin/papers/${encodeURIComponent(paperId)}/speaking-structure`,
    { method: 'PUT', body: JSON.stringify({ structure }) },
  );
}

export async function adminArchiveSpeakingPaper(paperId: string) {
  return apiRequest<void>(
    `/v1/admin/papers/${encodeURIComponent(paperId)}`,
    { method: 'DELETE' },
  );
}

// Conversation bulk runner uses existing helpers. Re-export a small
// typed orchestrator that the bulk page imports.

export interface ConversationBulkCellInput {
  profession: string;
  taskType: 'oet-roleplay' | 'oet-handover';
}

// Pronunciation bulk runner orchestration result type (page-local helper).
export interface PronunciationBulkResultRow {
  topic: string;
  profession: string;
  ok: boolean;
  drillId?: string;
  status?: string;
  error?: string;
  warning?: string | null;
}

// Pronunciation analytics — endpoint NOT implemented yet on the backend.
// The analytics page will catch the 404 and render TBD-state. We still
// expose a typed helper so the call-site is small and easy to migrate
// the moment the endpoint lands.
export interface AdminPronunciationAnalytics {
  totalAttempts: number;
  averageScore: number | null;
  topPhonemes: Array<{ phoneme: string; attempts: number; averageScore: number | null }>;
  weakestPhonemes: Array<{ phoneme: string; attempts: number; averageScore: number | null }>;
  source?: 'live' | 'tbd';
}

export async function adminFetchPronunciationAnalytics(params?: {
  windowDays?: number;
  profession?: string;
}): Promise<AdminPronunciationAnalytics> {
  const qs = new URLSearchParams();
  if (params?.windowDays) qs.set('windowDays', String(params.windowDays));
  if (params?.profession) qs.set('profession', params.profession);
  const suffix = qs.toString() ? `?${qs.toString()}` : '';
  return apiRequest<AdminPronunciationAnalytics>(
    `/v1/admin/pronunciation/analytics${suffix}`,
  );
}
// === SUBAGENT_D: speaking-conv-pron END ===

// === SUBAGENT_A: reading-authoring START ===
// Typed wrappers for the Reading Authoring admin surface backed by
// `ReadingAuthoringAdminEndpoints` under `/v1/admin/papers/{paperId}/reading`.
// All correct-answer / explanation / synonym fields here are intentionally
// included — these helpers MUST only be called from admin UI behind
// `AdminContentWrite`.
import type {
  ReadingDistractorsPayload,
  ReadingExtractionDraft,
  ReadingPartUpsertDto,
  ReadingPartView,
  ReadingQuestionDto,
  ReadingQuestionReviewLogEntry,
  ReadingQuestionUpsertDto,
  ReadingReviewTransitionPayload,
  ReadingStructure,
  ReadingStructureImportResult,
  ReadingStructureManifest,
  ReadingStructureManifestImportPayload,
  ReadingTextDto,
  ReadingTextUpsertDto,
  ReadingValidationReport,
  ReorderDto,
} from './types/admin/reading-authoring';

const readingAdminBase = (paperId: string) =>
  `/v1/admin/papers/${encodeURIComponent(paperId)}/reading`;

export async function adminReadingGetStructure(paperId: string): Promise<ReadingStructure> {
  return apiRequest<ReadingStructure>(`${readingAdminBase(paperId)}/structure`);
}

export async function adminReadingGetManifest(paperId: string): Promise<ReadingStructureManifest> {
  return apiRequest<ReadingStructureManifest>(`${readingAdminBase(paperId)}/manifest`);
}

export async function adminReadingImportManifest(
  paperId: string,
  payload: ReadingStructureManifestImportPayload,
): Promise<ReadingStructureImportResult> {
  return apiRequest<ReadingStructureImportResult>(`${readingAdminBase(paperId)}/manifest`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function adminReadingEnsureCanonical(paperId: string): Promise<void> {
  await apiRequest<void>(`${readingAdminBase(paperId)}/ensure-canonical`, {
    method: 'POST',
  });
}

export async function adminReadingUpsertPart(
  paperId: string,
  partCode: 'A' | 'B' | 'C',
  dto: ReadingPartUpsertDto,
): Promise<ReadingPartView> {
  return apiRequest<ReadingPartView>(
    `${readingAdminBase(paperId)}/parts/${encodeURIComponent(partCode)}`,
    { method: 'PUT', body: JSON.stringify(dto) },
  );
}

export async function adminReadingUpsertText(
  paperId: string,
  dto: ReadingTextUpsertDto,
): Promise<ReadingTextDto> {
  return apiRequest<ReadingTextDto>(`${readingAdminBase(paperId)}/texts`, {
    method: 'POST',
    body: JSON.stringify(dto),
  });
}

export async function adminReadingDeleteText(paperId: string, textId: string): Promise<void> {
  await apiRequest<void>(
    `${readingAdminBase(paperId)}/texts/${encodeURIComponent(textId)}`,
    { method: 'DELETE' },
  );
}

export async function adminReadingUpsertQuestion(
  paperId: string,
  dto: ReadingQuestionUpsertDto,
): Promise<ReadingQuestionDto> {
  return apiRequest<ReadingQuestionDto>(`${readingAdminBase(paperId)}/questions`, {
    method: 'POST',
    body: JSON.stringify(dto),
  });
}

export async function adminReadingDeleteQuestion(paperId: string, questionId: string): Promise<void> {
  await apiRequest<void>(
    `${readingAdminBase(paperId)}/questions/${encodeURIComponent(questionId)}`,
    { method: 'DELETE' },
  );
}

export async function adminReadingReorderTexts(
  paperId: string,
  partId: string,
  orderedIds: string[],
): Promise<void> {
  const body: ReorderDto = { orderedIds };
  await apiRequest<void>(
    `${readingAdminBase(paperId)}/parts/${encodeURIComponent(partId)}/reorder-texts`,
    { method: 'POST', body: JSON.stringify(body) },
  );
}

export async function adminReadingReorderQuestions(
  paperId: string,
  partId: string,
  orderedIds: string[],
): Promise<void> {
  const body: ReorderDto = { orderedIds };
  await apiRequest<void>(
    `${readingAdminBase(paperId)}/parts/${encodeURIComponent(partId)}/reorder-questions`,
    { method: 'POST', body: JSON.stringify(body) },
  );
}

export async function adminReadingValidate(paperId: string): Promise<ReadingValidationReport> {
  return apiRequest<ReadingValidationReport>(`${readingAdminBase(paperId)}/validate`);
}

export async function adminReadingSetDistractors(
  paperId: string,
  questionId: string,
  payload: ReadingDistractorsPayload,
): Promise<{ id: string; optionDistractorsJson: string | null }> {
  return apiRequest<{ id: string; optionDistractorsJson: string | null }>(
    `${readingAdminBase(paperId)}/questions/${encodeURIComponent(questionId)}/distractors`,
    { method: 'PUT', body: JSON.stringify(payload) },
  );
}

export async function adminReadingGetReviewHistory(
  paperId: string,
  questionId: string,
): Promise<ReadingQuestionReviewLogEntry[]> {
  return apiRequest<ReadingQuestionReviewLogEntry[]>(
    `${readingAdminBase(paperId)}/questions/${encodeURIComponent(questionId)}/review-history`,
  );
}

export async function adminReadingTransitionReview(
  paperId: string,
  questionId: string,
  payload: ReadingReviewTransitionPayload,
): Promise<unknown> {
  return apiRequest<unknown>(
    `${readingAdminBase(paperId)}/questions/${encodeURIComponent(questionId)}/review-transition`,
    { method: 'POST', body: JSON.stringify(payload) },
  );
}

export async function adminReadingGetAnalytics(paperId: string): Promise<unknown> {
  return apiRequest<unknown>(`${readingAdminBase(paperId)}/analytics`);
}

export async function adminReadingCreateExtraction(
  paperId: string,
  mediaAssetId?: string | null,
): Promise<ReadingExtractionDraft> {
  return apiRequest<ReadingExtractionDraft>(
    `${readingAdminBase(paperId)}/extractions`,
    { method: 'POST', body: JSON.stringify({ mediaAssetId: mediaAssetId ?? null }) },
  );
}

export async function adminReadingListExtractions(paperId: string): Promise<ReadingExtractionDraft[]> {
  return apiRequest<ReadingExtractionDraft[]>(`${readingAdminBase(paperId)}/extractions`);
}

export async function adminReadingGetExtraction(
  paperId: string,
  draftId: string,
): Promise<ReadingExtractionDraft> {
  return apiRequest<ReadingExtractionDraft>(
    `${readingAdminBase(paperId)}/extractions/${encodeURIComponent(draftId)}`,
  );
}

export async function adminReadingApproveExtraction(
  paperId: string,
  draftId: string,
): Promise<ReadingExtractionDraft> {
  return apiRequest<ReadingExtractionDraft>(
    `${readingAdminBase(paperId)}/extractions/${encodeURIComponent(draftId)}/approve`,
    { method: 'POST' },
  );
}

export async function adminReadingRejectExtraction(
  paperId: string,
  draftId: string,
  reason?: string,
): Promise<ReadingExtractionDraft> {
  return apiRequest<ReadingExtractionDraft>(
    `${readingAdminBase(paperId)}/extractions/${encodeURIComponent(draftId)}/reject`,
    { method: 'POST', body: JSON.stringify({ reason: reason ?? null }) },
  );
}
// === SUBAGENT_A: reading-authoring END ===

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

export interface AdminMocksAnalyticsRevenueRow {
  packageCode: string;
  packageName: string;
  totalRevenue: number;
  currency: string;
}

export interface AdminMocksAnalyticsTutorWorkloadRow {
  tutorId: string;
  tutorName: string;
  pendingBookings: number;
  completedThisWeek: number;
}

export interface AdminMocksAnalyticsLowQualityRow {
  bundleId: string;
  bundleTitle: string;
  itemCount: number;
  flags: string[];
}

export interface AdminMocksAnalyticsWindow {
  start: string;
  end: string;
}

export interface AdminMocksAnalyticsAttemptsCompletion {
  started: number;
  completed: number;
  completionRate: number;
  window: AdminMocksAnalyticsWindow;
}

export interface AdminMocksAnalyticsReadinessDistribution {
  red: number;
  amber: number;
  green: number;
  darkGreen: number;
}

export interface AdminMocksAnalyticsAverageReadiness {
  sampleSize: number;
  averageScore: number | null;
  distribution: AdminMocksAnalyticsReadinessDistribution;
  window: AdminMocksAnalyticsWindow;
}

export interface AdminMocksAnalyticsPassPredictionProfessionRow {
  profession: string;
  sampleSize: number;
  predictedPassRate: number;
}

export interface AdminMocksAnalyticsPassPrediction {
  sampleSize: number;
  predictedPassRate: number | null;
  byProfession: AdminMocksAnalyticsPassPredictionProfessionRow[];
  window: AdminMocksAnalyticsWindow;
}

export interface AdminMocksAnalyticsMarkingDelayRow {
  subtest: 'writing' | 'speaking';
  sampleSize: number;
  avgDelayHours: number;
  p95DelayHours: number;
}

export interface AdminMocksAnalyticsMarkingDelay {
  perSubtest: AdminMocksAnalyticsMarkingDelayRow[];
  window: AdminMocksAnalyticsWindow;
}

/**
 * Phase 2 closure — Reading subtest aggregate across mock sessions.
 * Backend computes this from `MockSectionAttempt` rows with
 * `SubtestCode == "reading"`. Surfaced on the mocks analytics dashboard
 * so operators see Reading-in-mocks performance without drilling into
 * `/admin/analytics/reading`. Every field is nullable because the mock
 * pipeline may not yet have written any Reading sections.
 */
export interface AdminMocksAnalyticsReadingSection {
  started: number;
  submitted: number;
  completionRatePercent: number | null;
  averageRawScore: number | null;
  averageScaledScore: number | null;
  averageCompletionSeconds: number | null;
}

export interface AdminMocksAnalyticsResponse {
  revenueByPackage: AdminMocksAnalyticsRevenueRow[];
  tutorWorkload: AdminMocksAnalyticsTutorWorkloadRow[];
  lowQualityFlags: AdminMocksAnalyticsLowQualityRow[];
  readingSection: AdminMocksAnalyticsReadingSection;
  attemptsCompletion: AdminMocksAnalyticsAttemptsCompletion;
  averageReadiness: AdminMocksAnalyticsAverageReadiness;
  passPrediction: AdminMocksAnalyticsPassPrediction;
  markingDelay: AdminMocksAnalyticsMarkingDelay;
}

interface AdminMocksAnalyticsRootPayload {
  revenueByPackage?: AdminMocksAnalyticsRevenueRow[];
  tutorWorkload?: AdminMocksAnalyticsTutorWorkloadRow[];
  lowQualityFlags?: AdminMocksAnalyticsLowQualityRow[];
  readingSection?: AdminMocksAnalyticsReadingSection;
}

function emptyReadingSection(): AdminMocksAnalyticsReadingSection {
  return {
    started: 0,
    submitted: 0,
    completionRatePercent: null,
    averageRawScore: null,
    averageScaledScore: null,
    averageCompletionSeconds: null,
  };
}

function emptyAdminMocksAnalyticsWindow(): AdminMocksAnalyticsWindow {
  const now = new Date().toISOString();
  return { start: now, end: now };
}

function emptyAttemptsCompletion(): AdminMocksAnalyticsAttemptsCompletion {
  return { started: 0, completed: 0, completionRate: 0, window: emptyAdminMocksAnalyticsWindow() };
}

function emptyAverageReadiness(): AdminMocksAnalyticsAverageReadiness {
  return {
    sampleSize: 0,
    averageScore: null,
    distribution: { red: 0, amber: 0, green: 0, darkGreen: 0 },
    window: emptyAdminMocksAnalyticsWindow(),
  };
}

function emptyPassPrediction(): AdminMocksAnalyticsPassPrediction {
  return {
    sampleSize: 0,
    predictedPassRate: null,
    byProfession: [],
    window: emptyAdminMocksAnalyticsWindow(),
  };
}

function emptyMarkingDelay(): AdminMocksAnalyticsMarkingDelay {
  return { perSubtest: [], window: emptyAdminMocksAnalyticsWindow() };
}

async function fetchAdminMocksAnalyticsSubroute<T>(path: string, fallback: T): Promise<T> {
  try {
    return await apiRequest<T>(path);
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return fallback;
    }
    throw err;
  }
}

/**
 * GET /v1/admin/analytics/mocks (root) + 4 Phase 8a sub-routes fetched in parallel.
 *
 * Root returns revenue-by-package, tutor workload, and low-quality flagged
 * bundles. The four Phase 8a sub-routes layer on attempts-completion, average-
 * readiness, pass-prediction, and marking-delay aggregations. Tolerates 404 on
 * any individual route (endpoint not yet deployed) by substituting an empty
 * skeleton so the page can render its empty states.
 */
export async function fetchAdminMocksAnalytics(): Promise<AdminMocksAnalyticsResponse> {
  const rootPromise: Promise<AdminMocksAnalyticsRootPayload> = (async () => {
    try {
      return await apiRequest<AdminMocksAnalyticsRootPayload>('/v1/admin/analytics/mocks');
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        return {};
      }
      throw err;
    }
  })();

  const [root, attemptsCompletion, averageReadiness, passPrediction, markingDelay] = await Promise.all([
    rootPromise,
    fetchAdminMocksAnalyticsSubroute<AdminMocksAnalyticsAttemptsCompletion>(
      '/v1/admin/analytics/mocks/attempts-completion',
      emptyAttemptsCompletion(),
    ),
    fetchAdminMocksAnalyticsSubroute<AdminMocksAnalyticsAverageReadiness>(
      '/v1/admin/analytics/mocks/average-readiness',
      emptyAverageReadiness(),
    ),
    fetchAdminMocksAnalyticsSubroute<AdminMocksAnalyticsPassPrediction>(
      '/v1/admin/analytics/mocks/pass-prediction',
      emptyPassPrediction(),
    ),
    fetchAdminMocksAnalyticsSubroute<AdminMocksAnalyticsMarkingDelay>(
      '/v1/admin/analytics/mocks/marking-delay',
      emptyMarkingDelay(),
    ),
  ]);

  return {
    revenueByPackage: root.revenueByPackage ?? [],
    tutorWorkload: root.tutorWorkload ?? [],
    lowQualityFlags: root.lowQualityFlags ?? [],
    readingSection: root.readingSection ?? emptyReadingSection(),
    attemptsCompletion,
    averageReadiness,
    passPrediction,
    markingDelay,
  };
}

// ── Admin speaking calibration (Phase 7a) ───────────────────────────────
//
// Backed by `AdminEndpoints.cs` → `AdminService.SpeakingCalibration.cs`:
//   GET /v1/admin/speaking/calibration/samples       (list of calibration sets)
//   GET /v1/admin/speaking/calibration/drift         (per-tutor drift report)
//
// The backend stores per-tutor mean absolute error (MAE) across the 9 rubric
// criteria. We surface it as the σ-style drift signal the calibration page
// renders. Lower = closer to gold scores. The legacy admin nomenclature
// ("sigma") reads MAE in our case.

export interface SpeakingCalibrationDriftTutorRow {
  tutorId: string;
  tutorName: string;
  submissionCount: number;
  meanAbsoluteError: number;
  totalAbsoluteError: number;
  lastSubmittedAt: string;
}

export interface SpeakingCalibrationDriftSummary {
  tutors: SpeakingCalibrationDriftTutorRow[];
  sampleSize: number;
  samplesPublished: number;
}

export interface SpeakingCalibrationSampleSummaryRow {
  sampleId: string;
  title: string;
  description: string;
  sourceAttemptId: string;
  professionId: string;
  difficulty: string;
  status: string;
  goldScores: Record<string, number>;
  tutorSubmissionCount: number;
  createdAt: string;
  publishedAt: string | null;
}

export interface SpeakingCalibrationSamplesResponse {
  samples: SpeakingCalibrationSampleSummaryRow[];
}

/**
 * GET /v1/admin/speaking/calibration/drift?minSubmissions=1
 *
 * Drift report — for each tutor that has submitted ≥1 calibration rubric,
 * returns the mean absolute error vs the gold rubric across all 9 criteria.
 * Tolerates 404 (endpoint not yet wired) by returning empty arrays so the
 * page renders its empty state cleanly.
 */
export async function fetchSpeakingCalibrationSummary(
  minSubmissions: number = 1,
): Promise<SpeakingCalibrationDriftSummary> {
  const qs = `?minSubmissions=${encodeURIComponent(String(Math.max(1, minSubmissions)))}`;
  try {
    return await apiRequest<SpeakingCalibrationDriftSummary>(
      `/v1/admin/speaking/calibration/drift${qs}`,
    );
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return { tutors: [], sampleSize: 0, samplesPublished: 0 };
    }
    throw err;
  }
}

/**
 * GET /v1/admin/speaking/calibration/samples
 *
 * Returns the curated calibration sample set ("sets" in the calibration UI).
 * Each row carries the profession, status, and how many tutors have already
 * submitted rubric scores for that sample.
 */
export async function fetchSpeakingCalibrationSets(
  status?: string,
): Promise<SpeakingCalibrationSampleSummaryRow[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : '';
  try {
    const response = await apiRequest<SpeakingCalibrationSamplesResponse>(
      `/v1/admin/speaking/calibration/samples${qs}`,
    );
    return Array.isArray(response?.samples) ? response.samples : [];
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return [];
    }
    throw err;
  }
}

// ── Admin interlocutor onboarding (Phase 7a) ────────────────────────────
//
// Backed by `InterlocutorTrainingEndpoints.cs` →
// `InterlocutorTrainingService.cs`:
//   GET /v1/admin/speaking/interlocutor-training/modules
//
// The admin onboarding page consolidates two signals to derive a "trainee"
// view: every tutor that has appeared in the calibration drift report (i.e.
// has started submitting calibration rubrics) is treated as a trainee, and
// the published-vs-required modules drive their training status.
//
// Mark-trained and start-practice helpers tolerate 404s so the UI degrades
// gracefully if the backend has not yet wired the admin-side completion
// shortcut.

export type InterlocutorTrainingStatusLabel = 'In Progress' | 'Trained' | 'Failed';

export interface InterlocutorTraineeRow {
  traineeId: string;
  traineeName: string;
  startedAt: string | null;
  rolePlaysCompleted: number;
  calibrationSigma: number | null;
  status: InterlocutorTrainingStatusLabel;
  lastActivityAt: string | null;
}

export interface InterlocutorTraineesResponse {
  trainees: InterlocutorTraineeRow[];
  totalInOnboarding: number;
  totalTrained: number;
  totalDroppedOff: number;
}

interface AdminInterlocutorModuleRaw {
  id: string;
  title: string;
  orderIndex: number;
  contentMarkdown: string;
  requiredForCalibration: boolean;
  stage: string;
  status: string;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
}

function classifyTrainingStatus(
  sigma: number | null,
  rolePlays: number,
): InterlocutorTrainingStatusLabel {
  if (sigma === null) return 'In Progress';
  if (sigma <= 0.5 && rolePlays >= 1) return 'Trained';
  if (sigma > 1.0 && rolePlays >= 3) return 'Failed';
  return 'In Progress';
}

/**
 * GET /v1/admin/speaking/interlocutor-training/modules (+ drift report)
 *
 * Synthesises the admin-side trainee queue from the two signals the
 * backend currently exposes: published training modules and the per-tutor
 * calibration drift report. Each tutor with at least one calibration
 * submission is treated as a trainee in the onboarding pipeline.
 */
export async function fetchInterlocutorTrainees(): Promise<InterlocutorTraineesResponse> {
  // Modules are fetched purely to confirm onboarding pipeline state; the
  // synthesised trainee rows are derived from the drift report below.
  try {
    await apiRequest<AdminInterlocutorModuleRaw[]>(
      '/v1/admin/speaking/interlocutor-training/modules',
    );
  } catch (err) {
    if (!(err instanceof ApiError) || err.status !== 404) throw err;
  }

  const drift = await fetchSpeakingCalibrationSummary(1);

  const trainees: InterlocutorTraineeRow[] = drift.tutors.map((tutor) => {
    const sigma = Number.isFinite(tutor.meanAbsoluteError) ? tutor.meanAbsoluteError : null;
    const status = classifyTrainingStatus(sigma, tutor.submissionCount);
    return {
      traineeId: tutor.tutorId,
      traineeName: tutor.tutorName,
      startedAt: tutor.lastSubmittedAt ?? null,
      rolePlaysCompleted: tutor.submissionCount,
      calibrationSigma: sigma,
      status,
      lastActivityAt: tutor.lastSubmittedAt ?? null,
    };
  });

  const totalTrained = trainees.filter((t) => t.status === 'Trained').length;
  const totalDroppedOff = trainees.filter((t) => t.status === 'Failed').length;
  const totalInOnboarding = trainees.length - totalTrained - totalDroppedOff;

  return {
    trainees,
    totalInOnboarding: Math.max(0, totalInOnboarding),
    totalTrained,
    totalDroppedOff,
  };
}

export interface MarkInterlocutorTrainedResult {
  traineeId: string;
  status: InterlocutorTrainingStatusLabel;
  acknowledgedAt: string;
}

/**
 * POST /v1/admin/speaking/interlocutor-training/trainees/{id}/mark-trained
 *
 * Admin-side shortcut to mark a trainee as Trained. Tolerates 404 so the
 * UI can still surface the action even before the backend wires the
 * dedicated route — in that case the helper returns a synthetic
 * acknowledgement that the caller can use to optimistically update local
 * state.
 */
export async function markInterlocutorTrained(
  traineeId: string,
): Promise<MarkInterlocutorTrainedResult> {
  try {
    return await apiRequest<MarkInterlocutorTrainedResult>(
      `/v1/admin/speaking/interlocutor-training/trainees/${encodeURIComponent(traineeId)}/mark-trained`,
      { method: 'POST', body: JSON.stringify({}) },
      { json: true },
    );
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return {
        traineeId,
        status: 'Trained',
        acknowledgedAt: new Date().toISOString(),
      };
    }
    throw err;
  }
}

export interface InterlocutorPracticeSessionStart {
  sessionId: string;
  prepHref: string;
}

/**
 * POST /v1/admin/speaking/interlocutor-training/trainees/{id}/practice-session
 *
 * Creates (or resumes) a practice role-play session for an interlocutor
 * trainee and returns the session id + the prep-page URL the admin should
 * route to. Tolerates 404 by returning a placeholder route the page can
 * surface as a disabled state.
 */
export async function startInterlocutorPracticeSession(
  traineeId: string,
): Promise<InterlocutorPracticeSessionStart> {
  try {
    return await apiRequest<InterlocutorPracticeSessionStart>(
      `/v1/admin/speaking/interlocutor-training/trainees/${encodeURIComponent(traineeId)}/practice-session`,
      { method: 'POST', body: JSON.stringify({}) },
      { json: true },
    );
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return {
        sessionId: '',
        prepHref: '/speaking/select-profession',
      };
    }
    throw err;
  }
}

// ── Phase 7a spec-named helper aliases ──────────────────────────────────
//
// The Phase 7a admin calibration + interlocutor pages spec asks for these
// helper names. They are thin re-exports / wrappers over the helpers above
// so the admin pages can opt into the spec naming without duplicating the
// underlying network logic.

/**
 * Phase 7a alias for {@link fetchSpeakingCalibrationSummary}.
 *
 * Returns the per-tutor calibration drift overview surfaced on the admin
 * speaking calibration page.
 */
export async function fetchSpeakingCalibrationOverview(
  minSubmissions: number = 1,
): Promise<SpeakingCalibrationDriftSummary> {
  return fetchSpeakingCalibrationSummary(minSubmissions);
}

/**
 * Phase 7a alias for {@link fetchInterlocutorTrainees}.
 *
 * Returns the synthesised interlocutor trainee table for the admin
 * onboarding page.
 */
export async function fetchInterlocutorTraineeList(): Promise<InterlocutorTraineesResponse> {
  return fetchInterlocutorTrainees();
}

/**
 * Practice queue row — pending interlocutor practice recording under
 * review by the calibration team.
 */
export interface InterlocutorPracticeQueueRow {
  recordingId: string;
  traineeId: string;
  traineeName: string;
  submittedAt: string;
  durationSeconds: number;
  status: 'Pending' | 'UnderReview' | 'Returned';
}

export interface InterlocutorPracticeQueueResponse {
  recordings: InterlocutorPracticeQueueRow[];
  totalPending: number;
}

/**
 * GET /v1/admin/speaking/interlocutor-training/practice-queue
 *
 * Backend gap (Phase 7a): the dedicated practice queue endpoint is not
 * wired yet. The helper tolerates 404 by returning an empty queue so the
 * admin onboarding page renders the empty state instead of erroring.
 *
 * When the backend ships the endpoint this helper will start surfacing
 * real rows without UI changes.
 */
export async function fetchInterlocutorPracticeQueue(): Promise<InterlocutorPracticeQueueResponse> {
  try {
    return await apiRequest<InterlocutorPracticeQueueResponse>(
      '/v1/admin/speaking/interlocutor-training/practice-queue',
    );
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return { recordings: [], totalPending: 0 };
    }
    throw err;
  }
}

// ── Voice Design Studio API ─────────────────────────────────────────

/** Preview an ElevenLabs voice (returns an MP3 Blob). */
export async function previewAdminVoiceDesign(body: {
  voiceId?: string;
  text: string;
  locale?: string;
}): Promise<Blob> {
  return apiBlobRequest('/v1/admin/voice-design/preview', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

/** Bulk regenerate audio across the platform with specified voice config */
export interface AdminAudioRegenerateRequest {
  audioType: 'all' | 'listening' | 'vocabulary' | 'conversation' | 'recalls';
  scope: 'all' | 'missing' | 'different-voice';
  modelVariant?: 'flash' | 'voicedesign' | string;
  voiceId?: string;
  instructions?: string;
  speed?: number;
  pitch?: number;
  emotion?: string;
  providerName?: string;
  forceRegenerate?: boolean;
  dryRun?: boolean;
}

export interface AdminAudioRegenerateBatchResult {
  batchId: string;
  audioType: string;
  scope: string;
  totalItems: number;
  dryRun: boolean;
  modelVariant: string;
  voiceId?: string;
  providerName?: string | null;
}

export async function regenerateAllAudio(
  body: AdminAudioRegenerateRequest
): Promise<AdminAudioRegenerateBatchResult> {
  return apiRequest<AdminAudioRegenerateBatchResult>('/v1/admin/voice-design/regenerate', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

/** Get active/completed audio regeneration batches */
export interface AdminAudioBatch {
  batchId: string;
  audioType: 'all' | 'listening' | 'vocabulary' | 'conversation' | 'recalls';
  scope: string;
  status: 'running' | 'completed' | 'failed' | 'cancelled';
  totalItems: number;
  completedItems: number;
  failedItems: number;
  voiceId: string;
  modelVariant: string;
  providerName: string;
  speed: number;
  pitch: number;
  emotion: string;
  startedAt: string;
  completedAt: string | null;
  requestedBy: string;
}

export async function getAudioRegenerationBatches(): Promise<{ batches: AdminAudioBatch[] }> {
  return apiRequest<{ batches: AdminAudioBatch[] }>('/v1/admin/voice-design/batches');
}

/** Get progress details for a specific batch */
export async function getAudioRegenerationBatchProgress(batchId: string): Promise<AdminAudioBatch> {
  return apiRequest<AdminAudioBatch>(`/v1/admin/voice-design/batches/${encodeURIComponent(batchId)}`);
}

/** Cancel an in-progress batch */
export async function cancelAudioRegenerationBatch(batchId: string): Promise<{ cancelled: boolean }> {
  return apiRequest<{ cancelled: boolean }>(`/v1/admin/voice-design/batches/${encodeURIComponent(batchId)}/cancel`, {
    method: 'POST',
  });
}

/** Retry failed or incomplete recall audio jobs in a batch */
export async function retryAudioRegenerationBatch(batchId: string): Promise<AdminAudioBatch> {
  return apiRequest<AdminAudioBatch>(`/v1/admin/voice-design/batches/${encodeURIComponent(batchId)}/retry`, {
    method: 'POST',
  });
}

/** Get the current globally configured voice settings */
export interface AdminVoiceDesignConfig {
  elevenLabsTtsBaseUrl: string;
  elevenLabsDefaultVoiceId: string;
  elevenLabsModel: string;
  elevenLabsOutputFormat: string;
  elevenLabsPronunciationDictionaryId: string | null;
  elevenLabsPronunciationDictionaryVersionId: string | null;
  elevenLabsStability: number;
  elevenLabsSimilarityBoost: number;
  elevenLabsStyle: number;
  elevenLabsUseSpeakerBoost: boolean;
  elevenLabsApiKeyPresent: boolean;
  lastUpdatedAt: string | null;
  lastUpdatedBy: string | null;
}

export async function getAdminVoiceDesignConfig(): Promise<AdminVoiceDesignConfig> {
  return apiRequest<AdminVoiceDesignConfig>('/v1/admin/voice-design/config');
}

/** Save voice design configuration globally */
export async function saveAdminVoiceDesignConfig(body: {
  elevenLabsApiKey?: string;
  elevenLabsTtsBaseUrl?: string;
  elevenLabsDefaultVoiceId?: string;
  elevenLabsModel?: string;
  elevenLabsOutputFormat?: string;
  elevenLabsPronunciationDictionaryId?: string;
  elevenLabsPronunciationDictionaryVersionId?: string;
  elevenLabsStability?: number;
  elevenLabsSimilarityBoost?: number;
  elevenLabsStyle?: number;
  elevenLabsUseSpeakerBoost?: boolean;
}): Promise<{ saved: boolean }> {
  return apiRequest<{ saved: boolean }>('/v1/admin/voice-design/config', {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

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

export async function startAdminRecallsAudioBackfill(
  body: Omit<AdminAudioRegenerateRequest, 'audioType'>,
): Promise<AdminAudioRegenerateBatchResult> {
  return apiRequest<AdminAudioRegenerateBatchResult>('/v1/admin/recalls/audio/backfill', {
    method: 'POST',
    body: JSON.stringify({ ...body, audioType: 'recalls', providerName: body.providerName ?? 'elevenlabs' }),
  });
}

export async function getAdminRecallsAudioBatchProgress(batchId: string): Promise<AdminAudioBatch> {
  return apiRequest<AdminAudioBatch>(`/v1/admin/recalls/audio/batches/${encodeURIComponent(batchId)}`);
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

export interface ListeningPolicyDto {
  id: string;
  // §1 Retry
  attemptsPerPaperPerUser: number;
  attemptCooldownMinutes: number;
  bestScoreDisplay: string;
  showPastAttempts: boolean;
  // §2 Timer
  fullPaperTimerMinutes: number;
  gracePeriodSeconds: number;
  onExpirySubmitPolicy: string;
  countdownWarningsJson: string;
  // §3 Audio replay
  examReplayAllowed: boolean;
  learningReplayAllowed: boolean;
  learningEvidenceLoopEnabled: boolean;
  // §4 Grading
  shortAnswerNormalisation: string;
  shortAnswerAcceptSynonyms: boolean;
  // §5 AI extraction
  aiExtractionEnabled: boolean;
  aiExtractionRequireHumanApproval: boolean;
  aiExtractionMaxRetriesPerPaper: number;
  // §6 Review
  showExplanationsAfterSubmit: boolean;
  showExplanationsOnlyIfWrong: boolean;
  showCorrectAnswerOnReview: boolean;
  // §7 Accessibility
  defaultExtraTimePct: number;
  screenReaderOptimised: boolean;
  // §8 Lifecycle
  autoExpireWorkerEnabled: boolean;
  autoExpireAfterMinutes: number;
  allowResumeAfterExpiry: boolean;
  // §9 Retention
  retainAnswerRowsDays: number;
  retainAttemptHeadersDays: number;
  anonymiseOnAccountDelete: boolean;
  // Listening V2 fields (nullable)
  previewWindowMsA1?: number | null;
  previewWindowMsA2?: number | null;
  previewWindowMsC1?: number | null;
  previewWindowMsC2?: number | null;
  reviewWindowMsA1?: number | null;
  reviewWindowMsA2?: number | null;
  reviewWindowMsC1?: number | null;
  reviewWindowMsC2FinalCbt?: number | null;
  reviewWindowMsC2FinalPaper?: number | null;
  betweenSectionTransitionMs?: number | null;
  partBQuestionWindowMs?: number | null;
  oneWayLocksEnabled?: boolean | null;
  confirmDialogRequired?: boolean | null;
  unansweredWarningRequired?: boolean | null;
  confirmTokenTtlMs?: number | null;
  highlightingEnabledPartA?: boolean | null;
  highlightingEnabledPartBC?: boolean | null;
  optionStrikethroughEnabled?: boolean | null;
  inAppZoomEnabled?: boolean | null;
  ctrlZoomBlocked?: boolean | null;
  annotationsPersistOnAdvance?: boolean | null;
  techReadinessRequired?: boolean | null;
  techReadinessTtlMs?: number | null;
  finalReviewAllPartsMsPaper?: number | null;
  rowVersion: number;
  updatedAt: string;
  updatedByAdminId?: string | null;
}

export interface ListeningUserPolicyOverrideDto {
  userId: string;
  extraTimeEntitlementPct: number;
  blockAttempts: boolean;
  accessibilityModeEnabled: boolean;
  reason?: string | null;
  grantedByAdminId?: string | null;
  createdAt: string;
  updatedAt: string;
  expiresAt?: string | null;
}

export async function adminGetListeningPolicy(): Promise<ListeningPolicyDto> {
  return apiRequest<ListeningPolicyDto>('/v1/admin/listening-policy');
}

export async function adminUpsertListeningPolicy(payload: ListeningPolicyDto): Promise<ListeningPolicyDto> {
  return apiRequest<ListeningPolicyDto>('/v1/admin/listening-policy', {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function adminGetListeningUserPolicyOverride(userId: string): Promise<ListeningUserPolicyOverrideDto | null> {
  return apiRequest<ListeningUserPolicyOverrideDto | null>(
    `/v1/admin/listening-policy/users/${encodeURIComponent(userId)}`
  );
}

export async function adminUpsertListeningUserPolicyOverride(
  userId: string,
  payload: Omit<ListeningUserPolicyOverrideDto, 'userId' | 'createdAt' | 'updatedAt' | 'grantedByAdminId'>
): Promise<ListeningUserPolicyOverrideDto> {
  return apiRequest<ListeningUserPolicyOverrideDto>(
    `/v1/admin/listening-policy/users/${encodeURIComponent(userId)}`,
    { method: 'PUT', body: JSON.stringify({ ...payload, userId }) }
  );
}

// ── Wave B2: Cart / Checkout / Subscription self-service / Admin products & coupons ──
//
// All endpoints here align with the plan in OET_BILLING_SUBSCRIPTION_PLAN.md §5/§7/§22.
// When the backend route is not yet ready (e.g. analytics + refunds), the helper still
// resolves so the page can degrade to a graceful "not yet available" message instead
// of a hard 500. We surface 404 by returning `null`.

export interface CartLineItem {
  itemId: string;
  productCode: string;
  productName: string;
  description?: string | null;
  quantity: number;
  unitAmount: number;
  totalAmount: number;
  currency: string;
  productType?: string | null;
  /** Billing interval ("one_time", "month", "year"...). Recurring items can't be paid via PayPal. */
  interval?: string | null;
  imageUrl?: string | null;
}

export interface CartPromoCode {
  code: string;
  discountAmount: number;
  description?: string | null;
}

export interface Cart {
  cartId: string;
  currency: string;
  items: CartLineItem[];
  promoCodes: CartPromoCode[];
  subtotalAmount: number;
  discountAmount: number;
  taxAmount: number;
  totalAmount: number;
  updatedAt: string;
}

function emptyCart(): Cart {
  return {
    cartId: '',
    currency: 'AUD',
    items: [],
    promoCodes: [],
    subtotalAmount: 0,
    discountAmount: 0,
    taxAmount: 0,
    totalAmount: 0,
    updatedAt: new Date().toISOString(),
  };
}

// FE-017: the backend serialises CartDto/CartItemDto in camelCase with different
// field names than the FE `Cart` shape the UI consumes (id↔cartId, subtotal↔
// subtotalAmount, unitPrice↔unitAmount, appliedPromoCodes:string[]↔promoCodes).
// Map at the API boundary so the cart renders real numbers (not 0/undefined) and
// the UI components stay untouched.
interface BackendCartItemDto {
  id: string;
  productCode: string;
  productName: string;
  productType: string;
  billingPriceId: string;
  unitPrice: number;
  currency: string;
  interval?: string | null;
  quantity: number;
  lineTotal: number;
}
interface BackendCartDto {
  id: string;
  status: string;
  items: BackendCartItemDto[];
  appliedPromoCodes: string[];
  subtotal: number;
  discount: number;
  total: number;
  currency: string;
  expiresAt: string;
}
function mapCart(dto: BackendCartDto): Cart {
  return {
    cartId: dto.id,
    currency: dto.currency,
    items: (dto.items ?? []).map((it) => ({
      itemId: it.id,
      productCode: it.productCode,
      productName: it.productName,
      description: null,
      quantity: it.quantity,
      unitAmount: it.unitPrice,
      totalAmount: it.lineTotal,
      currency: it.currency,
      productType: it.productType,
      interval: it.interval ?? null,
      imageUrl: null,
    })),
    promoCodes: (dto.appliedPromoCodes ?? []).map((code) => ({ code, discountAmount: 0 })),
    subtotalAmount: dto.subtotal,
    discountAmount: dto.discount,
    taxAmount: 0,
    totalAmount: dto.total,
    updatedAt: dto.expiresAt,
  };
}

async function maybe<T>(promise: Promise<T>, fallback: T | null = null): Promise<T | null> {
  try {
    return await promise;
  } catch (err) {
    if (err instanceof ApiError && (err.status === 404 || err.status === 501)) {
      return fallback;
    }
    throw err;
  }
}

export async function fetchCart(): Promise<Cart> {
  try {
    return mapCart(await apiRequest<BackendCartDto>('/v1/cart'));
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return emptyCart();
    }
    throw err;
  }
}

export async function createCart(): Promise<Cart> {
  return mapCart(await apiRequest<BackendCartDto>('/v1/cart', { method: 'POST' }));
}

export async function addCartItem(payload: {
  productCode: string;
  billingPriceId: string;
  quantity?: number;
}): Promise<Cart> {
  // FE-017: backend AddCartItemRequest requires billingPriceId (Guid).
  return mapCart(await apiRequest<BackendCartDto>('/v1/cart/items', {
    method: 'POST',
    body: JSON.stringify({
      productCode: payload.productCode,
      billingPriceId: payload.billingPriceId,
      quantity: payload.quantity ?? 1,
    }),
  }));
}

// FE-017: the backend cart-item / promo mutation endpoints require the owning
// cart id as a query param (object-level authorization); thread it from callers.
export async function updateCartItem(cartId: string, itemId: string, quantity: number): Promise<Cart> {
  return mapCart(await apiRequest<BackendCartDto>(
    `/v1/cart/items/${encodeURIComponent(itemId)}?cartId=${encodeURIComponent(cartId)}`,
    { method: 'PATCH', body: JSON.stringify({ quantity }) },
  ));
}

export async function removeCartItem(cartId: string, itemId: string): Promise<Cart> {
  return mapCart(await apiRequest<BackendCartDto>(
    `/v1/cart/items/${encodeURIComponent(itemId)}?cartId=${encodeURIComponent(cartId)}`,
    { method: 'DELETE' },
  ));
}

export async function applyCartPromoCode(cartId: string, code: string): Promise<Cart> {
  return mapCart(await apiRequest<BackendCartDto>(
    `/v1/cart/promo-codes?cartId=${encodeURIComponent(cartId)}`,
    { method: 'POST', body: JSON.stringify({ code }) },
  ));
}

export async function removeCartPromoCode(cartId: string, code: string): Promise<Cart> {
  return mapCart(await apiRequest<BackendCartDto>(
    `/v1/cart/promo-codes/${encodeURIComponent(code)}?cartId=${encodeURIComponent(cartId)}`,
    { method: 'DELETE' },
  ));
}

export interface CheckoutSessionResponse {
  sessionId: string;
  url: string;
}

export async function createCheckoutSession(payload?: {
  successUrl?: string;
  cancelUrl?: string;
  cartId?: string | null;
}): Promise<CheckoutSessionResponse> {
  const response = await apiRequest<ApiRecord>('/v1/checkout/sessions', {
    method: 'POST',
    body: JSON.stringify({
      successUrl: payload?.successUrl ?? null,
      cancelUrl: payload?.cancelUrl ?? null,
      cartId: payload?.cartId ?? null,
    }),
  });
  return {
    sessionId: String(response.sessionId ?? response.stripeSessionId ?? response.id ?? ''),
    url: String(response.url ?? ''),
  };
}

export interface PayPalCartOrder {
  orderId: string;
  checkoutSessionId: string;
  totalAmount: number;
  currency: string;
}

/**
 * Creates a PayPal (embedded) order for the cart. The returned order id is fed to the
 * embedded PayPal SDK's createOrder; onApprove captures it via captureBillingCheckout.
 * Rejects with a 400 ("paypal_recurring_unsupported") for carts containing subscriptions.
 */
export async function createCartPaypalOrder(cartId: string): Promise<PayPalCartOrder> {
  const response = await apiRequest<ApiRecord>('/v1/checkout/sessions/paypal', {
    method: 'POST',
    body: JSON.stringify({ cartId }),
  });
  return {
    orderId: String(response.orderId ?? ''),
    checkoutSessionId: String(response.checkoutSessionId ?? ''),
    totalAmount: Number(response.totalAmount ?? 0),
    currency: String(response.currency ?? 'AUD'),
  };
}

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
}

export async function fetchCheckoutSessionStatus(sessionId: string): Promise<CheckoutSessionStatus> {
  return apiRequest<CheckoutSessionStatus>(`/v1/checkout/sessions/${encodeURIComponent(sessionId)}/status`);
}

export interface CatalogRecommendation {
  productCode: string;
  name: string;
  description?: string | null;
  price: number;
  currency: string;
  imageUrl?: string | null;
}

export async function fetchCatalogRecommendations(): Promise<CatalogRecommendation[]> {
  const result = await maybe<{ items: CatalogRecommendation[] } | CatalogRecommendation[]>(
    apiRequest('/v1/catalog/recommendations'),
    [],
  );
  if (!result) return [];
  if (Array.isArray(result)) return result;
  return result.items ?? [];
}

// ── Subscription self-service ───────────────────────────────────────

export interface SubscriptionMe {
  subscriptionId: string;
  status: string;
  planCode: string;
  planName: string;
  price: number;
  currency: string;
  interval: string;
  startedAt: string | null;
  nextRenewalAt: string | null;
  cancelledAt: string | null;
  pausedUntil: string | null;
  cancelAtPeriodEnd: boolean;
  trialEndsAt: string | null;
  walletBalance?: number;
  walletCurrency?: string;
  productCategory?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  durationDays?: number;
  remainingDays?: number;
  expiringSoon?: boolean;
  totalFreezeDaysUsed?: number;
  maxFreezeDays?: number;
  freezeAllowanceRemaining?: number;
  preservedRemainingDays?: number | null;
  pendingFreezeRequestDate?: string | null;
  frozenSince?: string | null;
}

export async function fetchSubscriptionMe(): Promise<SubscriptionMe | null> {
  return maybe<SubscriptionMe>(apiRequest<SubscriptionMe>('/v1/subscriptions/me'));
}

export interface SubscriptionMeListItem extends SubscriptionMe {
  productCategory?: string | null;
}

export async function fetchSubscriptionsMe(): Promise<SubscriptionMeListItem[]> {
  const result = await maybe<{ items: SubscriptionMeListItem[] } | SubscriptionMeListItem[]>(
    apiRequest('/v1/subscriptions/me/list'),
    [],
  );
  if (!result) {
    const one = await fetchSubscriptionMe();
    return one ? [one] : [];
  }
  if (Array.isArray(result)) return result;
  return result.items ?? [];
}

export async function createSubscriptionPortalSession(returnUrl?: string): Promise<{ url: string }> {
  return apiRequest<{ url: string }>('/v1/subscriptions/me/portal-session', {
    method: 'POST',
    body: JSON.stringify({ returnUrl: returnUrl ?? null }),
  });
}

export async function cancelSubscription(reason?: string): Promise<SubscriptionMe> {
  return apiRequest<SubscriptionMe>('/v1/subscriptions/me/cancel', {
    method: 'POST',
    body: JSON.stringify({ reason: reason ?? null }),
  });
}

export async function pauseSubscriptionSelf(days?: number, reason?: string): Promise<SubscriptionMe> {
  return apiRequest<SubscriptionMe>('/v1/subscriptions/me/pause', {
    method: 'POST',
    body: JSON.stringify({ days: days ?? null, reason: reason ?? null }),
  });
}

export async function resumeSubscriptionSelf(): Promise<SubscriptionMe> {
  return apiRequest<SubscriptionMe>('/v1/subscriptions/me/resume', { method: 'POST' });
}

export async function requestSubscriptionFreeze(subscriptionId: string): Promise<SubscriptionMe> {
  return apiRequest<SubscriptionMe>(`/v1/subscriptions/${encodeURIComponent(subscriptionId)}/request-freeze`, {
    method: 'POST',
  });
}

export async function resumeSubscriptionById(subscriptionId: string): Promise<SubscriptionMe> {
  return apiRequest<SubscriptionMe>(`/v1/subscriptions/${encodeURIComponent(subscriptionId)}/resume`, {
    method: 'POST',
  });
}

export async function changeSubscriptionPlanSelf(planCode: string, prorate?: boolean): Promise<SubscriptionMe> {
  return apiRequest<SubscriptionMe>('/v1/subscriptions/me/change-plan', {
    method: 'POST',
    body: JSON.stringify({ planCode, prorate: prorate ?? true }),
  });
}

export interface SubscriptionInvoice {
  invoiceId: string;
  number?: string | null;
  date: string;
  amount: number;
  currency: string;
  status: string;
  description?: string | null;
  pdfUrl?: string | null;
  hostedInvoiceUrl?: string | null;
}

export async function fetchSubscriptionInvoices(params?: { page?: number; pageSize?: number }): Promise<{ items: SubscriptionInvoice[]; total: number; page: number; pageSize: number }> {
  const qs = new URLSearchParams();
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  const result = await maybe<{ items: SubscriptionInvoice[]; total: number; page?: number; pageSize?: number } | SubscriptionInvoice[]>(
    apiRequest(`/v1/subscriptions/me/invoices${q ? `?${q}` : ''}`),
    null,
  );
  if (result == null) return { items: [], total: 0, page: params?.page ?? 1, pageSize: params?.pageSize ?? 20 };
  if (Array.isArray(result)) return { items: result, total: result.length, page: params?.page ?? 1, pageSize: params?.pageSize ?? 20 };
  return { items: result.items ?? [], total: result.total ?? (result.items ?? []).length, page: result.page ?? params?.page ?? 1, pageSize: result.pageSize ?? params?.pageSize ?? 20 };
}

// ── Admin: Billing products (catalog) — Wave B2 thin CRUD ───────────

export interface AdminBillingProductPrice {
  priceId: string;
  amount: number;
  currency: string;
  interval: string;
  trialDays?: number;
  isDefault?: boolean;
}

export interface AdminBillingProduct {
  productCode: string;
  name: string;
  description?: string | null;
  productType: string;
  status: string;
  imageUrl?: string | null;
  displayOrder?: number;
  prices: AdminBillingProductPrice[];
  metadata?: Record<string, unknown>;
}

export async function fetchAdminBillingProducts(params?: { status?: string; search?: string }): Promise<AdminBillingProduct[]> {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.search) qs.set('search', params.search);
  const q = qs.toString();
  const result = await maybe<{ items: AdminBillingProduct[] } | AdminBillingProduct[]>(
    apiRequest(`/v1/admin/billing/products${q ? `?${q}` : ''}`),
    [],
  );
  if (!result) return [];
  if (Array.isArray(result)) return result;
  return result.items ?? [];
}

export async function fetchAdminBillingProduct(productCode: string): Promise<AdminBillingProduct | null> {
  return maybe<AdminBillingProduct>(
    apiRequest<AdminBillingProduct>(`/v1/admin/billing/products/${encodeURIComponent(productCode)}`),
  );
}

export async function updateAdminBillingProduct(productCode: string, payload: Partial<AdminBillingProduct>): Promise<AdminBillingProduct> {
  return apiRequest<AdminBillingProduct>(`/v1/admin/billing/products/${encodeURIComponent(productCode)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export interface AdminRefundRequest {
  id: string;
  invoiceId: string;
  userId: string;
  userName: string;
  amount: number;
  currency: string;
  reason: string;
  status: 'pending' | 'approved' | 'denied' | 'issued' | string;
  requestedAt: string;
  reviewedAt?: string | null;
  reviewerNotes?: string | null;
}

export async function fetchAdminRefunds(params?: { status?: string; page?: number; pageSize?: number }): Promise<{ items: AdminRefundRequest[]; total: number }> {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.page) qs.set('page', String(params.page));
  if (params?.pageSize) qs.set('pageSize', String(params.pageSize));
  const q = qs.toString();
  const result = await maybe<{ items: AdminRefundRequest[]; total?: number } | AdminRefundRequest[]>(
    apiRequest(`/v1/admin/refunds${q ? `?${q}` : ''}`),
    null,
  );
  if (result == null) return { items: [], total: 0 };
  if (Array.isArray(result)) return { items: result, total: result.length };
  return { items: result.items ?? [], total: result.total ?? (result.items ?? []).length };
}

export async function postAdminRefundAction(payload: { refundId: string; action: 'approve' | 'deny' | 'issue'; notes?: string | null; amount?: number | null }): Promise<AdminRefundRequest> {
  return apiRequest<AdminRefundRequest>('/v1/admin/refunds', {
    method: 'POST',
    body: JSON.stringify({
      refundId: payload.refundId,
      action: payload.action,
      notes: payload.notes ?? null,
      amount: payload.amount ?? null,
    }),
  });
}

export interface AdminBillingAnalyticsSeriesPoint {
  date: string;
  value: number;
}

export interface AdminBillingAnalyticsResponse {
  mrr: AdminBillingAnalyticsSeriesPoint[];
  churnRate: AdminBillingAnalyticsSeriesPoint[];
  ltv: AdminBillingAnalyticsSeriesPoint[];
  currency: string;
  available: boolean;
}

export async function fetchAdminBillingAnalytics(params?: { from?: string; to?: string }): Promise<AdminBillingAnalyticsResponse> {
  const qs = new URLSearchParams();
  if (params?.from) qs.set('from', params.from);
  if (params?.to) qs.set('to', params.to);
  const q = qs.toString();
  const result = await maybe<AdminBillingAnalyticsResponse>(
    apiRequest<AdminBillingAnalyticsResponse>(`/v1/admin/billing/analytics${q ? `?${q}` : ''}`),
    null,
  );
  if (result) return { ...result, available: true };
  return { mrr: [], churnRate: [], ltv: [], currency: 'AUD', available: false };
}
