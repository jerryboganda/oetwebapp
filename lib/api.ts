import { ensureFreshAccessToken } from './auth-client';
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
  ChecklistItem,
  SpeakingTask,
  RoleCard,
  SpeakingResult,
  TranscriptLine,
  PhrasingSegment,
  ReadingTask,
  ReadingResult,
  ListeningTask,
  ListeningResult,
  ListeningDrill,
  ListeningReview,
  MockConfig,
  MockReport,
  MockSession,
  ReadinessData,
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
  Confidence,
  SubTest,
  SettingsSectionData,
  SettingsSectionId,
  SpeakingTranscriptReview,
} from './mock-data';
import type {
  BillingData,
  BillingChangePreview,
  BillingQuote,
  BillingProductType,
  Invoice,
} from './billing-types';
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
  ReviewRequest as ExpertReviewRequest,
  ScheduleException,
  SpeakingReviewDetail,
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
  GrammarExerciseResult,
  GrammarLessonDocument,
  GrammarLessonLearner,
  GrammarLessonProgress,
  GrammarLessonSummary,
  GrammarLessonUpsertPayload,
  GrammarOverview,
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
  if (!value) return '';
  return value
    .replace(/[_-]/g, ' ')
    .split(' ')
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

function minutesToLabel(minutes: number): string {
  if (minutes >= 180) return `${Math.round(minutes / 60)} hrs`;
  return `${minutes} mins`;
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
  return (value ?? '').replace(/\s*-\s*/g, ' to ');
}

function formatCurrency(amount: number | string | null | undefined, currency = 'AUD'): string {
  const numericAmount = Number(amount ?? 0);
  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
  }).format(Number.isFinite(numericAmount) ? numericAmount : 0);
}

function toBillingStatus(value: string | null | undefined): Invoice['status'] {
  const normalized = (value ?? '').toLowerCase();
  if (normalized === 'pending') return 'Pending';
  if (normalized === 'failed') return 'Failed';
  return 'Paid';
}

function normalizeWaveformPeaks(value: unknown): number[] {
  if (!Array.isArray(value)) return [];
  return value
    .map((entry) => Number(entry))
    .filter((entry) => Number.isFinite(entry))
    .map((entry) => Math.max(6, Math.min(100, Math.round(entry))));
}

function parseCriterionScore(scoreRange: string | null | undefined): number {
  if (!scoreRange) return 0;
  const match = scoreRange.match(/(\d+)(?:-(\d+))?/);
  if (!match) return 0;
  const first = Number(match[1]);
  const second = match[2] ? Number(match[2]) : first;
  return Math.round((first + second) / 2);
}

function scoreToGrade(score: number): string {
  if (score >= 5) return 'B+';
  if (score >= 4) return 'B';
  if (score >= 3) return 'C+';
  if (score >= 2) return 'C';
  return 'D';
}

function normalizeCriterionName(code: string | null | undefined): string {
  switch ((code ?? '').toLowerCase()) {
    case 'purpose':
      return 'Purpose';
    case 'content':
      return 'Content';
    case 'conciseness':
      return 'Conciseness & Clarity';
    case 'genre':
      return 'Genre & Style';
    case 'organization':
      return 'Organisation & Layout';
    case 'language':
      return 'Language';
    case 'intelligibility':
      return 'Intelligibility';
    case 'fluency':
      return 'Fluency';
    case 'appropriateness':
      return 'Appropriateness of Language';
    case 'grammar_expression':
      return 'Resources of Grammar and Expression';
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
    } else if (process.env.NODE_ENV !== 'development') {
      console.error('[API] No auth token available for production request to', path);
    }
  } catch (err) {
    if (process.env.NODE_ENV !== 'development') {
      console.error('[API] Failed to retrieve auth token:', err);
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
    case 'draft_version_conflict': return 'Your draft was updated in another tab. Please refresh and try again.';
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

async function apiRequest<T = any>(path: string, init?: RequestInit): Promise<T> {
  let lastError: Error | null = null;

  for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
    try {
      const response = await fetchWithTimeout(resolveApiUrl(path), {
        ...init,
        headers: await getHeaders(path, init?.headers),
      });

      if (!response.ok) {
        let code = 'unknown_error';
        let message = `Request failed: ${response.status}`;
        let retryable = false;
        let fieldErrors: Array<{ field: string; code: string; message: string }> = [];
        try {
          const error = await response.json();
          code = error.code ?? code;
          message = error.message ?? error.title ?? message;
          retryable = error.retryable ?? isRetryable(response.status);
          fieldErrors = Array.isArray(error.fieldErrors) ? error.fieldErrors : [];
        } catch (err) {
          console.error('[API] Failed to parse error response body:', err);
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

async function uploadBinary(pathOrUrl: string, blob: Blob): Promise<void> {
  const response = await fetchWithTimeout(resolveApiUrl(pathOrUrl), {
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

export async function lintWritingViaApi(input: WritingLintInput): Promise<WritingLintResponse> {
  return apiRequest<WritingLintResponse>('/v1/writing/lint', {
    method: 'POST',
    body: JSON.stringify({
      letterText: input.letterText,
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
  const response = await fetchWithTimeout(resolveApiUrl(pathOrUrl), {
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

function attemptCacheKey(subtest: string, contentId: string) {
  return `oet.${subtest}.attempt.${contentId}`;
}

function evaluationCacheKey(subtest: string, contentId: string) {
  return `oet.${subtest}.evaluation.${contentId}`;
}

async function ensureAttempt(subtest: 'writing' | 'speaking' | 'reading' | 'listening', contentId: string, mode: string) {
  const key = attemptCacheKey(subtest, contentId);
  const cached = cacheGet(key);

  if (cached) {
    try {
      const existing = await apiRequest<ApiRecord>(`/v1/${subtest}/attempts/${cached}`);
      if (existing.state !== 'completed') {
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

  const created = await apiRequest<ApiRecord>(`/v1/${subtest}/attempts`, {
    method: 'POST',
    body: JSON.stringify({ contentId, context: 'practice', mode, deviceType: 'web', parentAttemptId: null }),
  });
  cacheSet(key, created.attemptId);
  return created;
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

  return { attemptId: 'wa-001', subtest: 'writing' };
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
  };
}

function mapSpeakingTask(item: ApiRecord): SpeakingTask {
  return {
    id: item.contentId,
    title: item.title,
    scenarioType: titleCase(item.scenarioType),
    difficulty: titleCase(item.difficulty) as SpeakingTask['difficulty'],
    profession: titleCase(item.professionId),
    criteriaFocus: Array.isArray(item.criteriaFocus) ? item.criteriaFocus.map(normalizeCriterionName).join(', ') : '',
    duration: minutesToLabel(item.estimatedDurationMinutes),
  };
}

function mapCriterionFeedback(criterionScores: ApiRecord[], feedbackItems: ApiRecord[]): CriterionFeedback[] {
  return criterionScores.map((criterion) => {
    const score = parseCriterionScore(criterion.scoreRange);
    const criterionCode = criterion.criterionCode;
    const relatedFeedback = feedbackItems.filter((item) => item.criterionCode === criterionCode);

    return {
      name: normalizeCriterionName(criterionCode),
      score,
      maxScore: 6,
      grade: scoreToGrade(score),
      explanation: criterion.explanation ?? '',
      anchoredComments: relatedFeedback.map((item, index) => ({
        id: item.feedbackItemId ?? `${criterionCode}-${index}`,
        text: item.anchor?.snippet ?? item.anchor?.lineId ?? normalizeCriterionName(criterionCode),
        comment: item.message ?? '',
      })),
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
    return `/writing/library${search}${hash}`;
  }

  if (pathname.startsWith('/writing/tasks/')) {
    const taskId = pathname.slice('/writing/tasks/'.length);
    const params = new URLSearchParams(search);
    params.set('taskId', taskId);
    const nextSearch = params.toString();
    return `/writing/player${nextSearch ? `?${nextSearch}` : ''}${hash}`;
  }

  if (pathname.startsWith('/reading/task/')) {
    const taskOrEvaluationId = pathname.slice('/reading/task/'.length);
    if (taskOrEvaluationId.startsWith('rt-')) {
      return `/reading/player/${taskOrEvaluationId}${search}${hash}`;
    }
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

export async function fetchDiagnosticOverview(): Promise<ApiRecord> {
  return apiRequest<ApiRecord>('/v1/diagnostic/overview');
}

export async function fetchDashboardHome(): Promise<ApiRecord> {
  const data = await apiRequest<ApiRecord>('/v1/learner/dashboard');
  return normalizeRouteValues(data);
}

export async function fetchEngagement(): Promise<ApiRecord> {
  return apiRequest<ApiRecord>('/v1/learner/engagement');
}

export async function fetchWalletTransactions(limit = 20): Promise<ApiRecord> {
  return apiRequest<ApiRecord>(`/v1/billing/wallet/transactions?limit=${limit}`);
}

export async function createWalletTopUp(amount: number, gateway: 'stripe' | 'paypal'): Promise<ApiRecord> {
  return apiRequest<ApiRecord>('/v1/billing/wallet/top-up', {
    method: 'POST',
    body: JSON.stringify({ amount, gateway }),
  });
}

export async function fetchExamFamilies(): Promise<ApiRecord> {
  return apiRequest<ApiRecord>('/v1/reference/exam-families');
}

export async function fetchSettingsData(): Promise<ApiRecord> {
  return apiRequest<ApiRecord>('/v1/settings');
}

export async function fetchSettingsSection(section: SettingsSectionId): Promise<SettingsSectionData> {
  const data = await apiRequest<ApiRecord>(`/v1/settings/${section}`);
  return {
    section,
    values: normalizeRouteValues(data.values ?? {}),
  };
}

export async function updateSettingsSection(section: 'profile' | 'goals' | 'notifications' | 'privacy' | 'accessibility' | 'audio' | 'study', values: Record<string, unknown>): Promise<ApiRecord> {
  return apiRequest<ApiRecord>(`/v1/settings/${section}`, {
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

export async function fetchReadingHome(): Promise<ApiRecord> {
  const data = await apiRequest<ApiRecord>('/v1/reading/home');
  return normalizeRouteValues(data);
}

export async function fetchListeningHome(): Promise<ApiRecord> {
  const data = await apiRequest<ApiRecord>('/v1/listening/home');
  return normalizeRouteValues(data);
}

export async function fetchWritingHome(): Promise<ApiRecord> {
  const data = await apiRequest<ApiRecord>('/v1/writing/home');
  return normalizeRouteValues(data);
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
  };
}

export async function fetchMocksHome(): Promise<ApiRecord> {
  const data = await apiRequest<ApiRecord>('/v1/mocks');
  return normalizeRouteValues(data);
}

export async function postSpeakingDeviceCheck(payload: {
  microphoneGranted: boolean;
  networkStable: boolean;
  deviceType?: string;
  taskId?: string;
  noiseLevel?: number;
  noiseAcceptable?: boolean;
}): Promise<ApiRecord> {
  return apiRequest<ApiRecord>('/v1/speaking/device-checks', {
    method: 'POST',
    body: JSON.stringify(payload),
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
  }));
}

export async function updateStudyPlanTask(taskId: string, updates: Partial<StudyPlanTask>): Promise<StudyPlanTask> {
  let result: ApiRecord;
  if (updates.status === 'completed') {
    result = await apiRequest(`/v1/study-plan/items/${taskId}/complete`, { method: 'POST' });
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
  };
}

export async function fetchWritingTasks(): Promise<WritingTask[]> {
  const items = await apiRequest<ApiRecord[]>('/v1/writing/tasks');
  return items.map(mapWritingTask);
}

export async function fetchWritingTask(taskId: string): Promise<WritingTask> {
  const item = await apiRequest<ApiRecord>(`/v1/writing/tasks/${taskId}`);
  return mapWritingTask(item);
}

export async function fetchWritingChecklist(): Promise<ChecklistItem[]> {
  const criteria = await apiRequest<ApiRecord[]>('/v1/reference/criteria?subtest=writing');
  return criteria.map((criterion, index) => ({ id: index + 1, text: criterion.label, completed: false }));
}

export async function submitWritingDraft(taskId: string, content: string): Promise<{ saved: boolean }> {
  const attempt = await ensureAttempt('writing', taskId, 'timed');
  const saved = await apiRequest<ApiRecord>(`/v1/writing/attempts/${attempt.attemptId}/draft`, {
    method: 'PATCH',
    body: JSON.stringify({ content, scratchpad: null, checklist: null, draftVersion: attempt.draftVersion ?? 1 }),
  });
  return { saved: Boolean(saved.saved) };
}

export async function submitWritingTask(taskId: string, content: string): Promise<WritingSubmission> {
  const attempt = await ensureAttempt('writing', taskId, 'timed');
  await apiRequest(`/v1/writing/attempts/${attempt.attemptId}/draft`, {
    method: 'PATCH',
    body: JSON.stringify({ content, scratchpad: null, checklist: null, draftVersion: attempt.draftVersion ?? 1 }),
  });

  const submitted = await apiRequest<ApiRecord>(`/v1/writing/attempts/${attempt.attemptId}/submit`, {
    method: 'POST',
    body: JSON.stringify({ content, idempotencyKey: crypto.randomUUID?.() ?? String(Date.now()) }),
  });

  cacheRemove(attemptCacheKey('writing', taskId));
  cacheSet(evaluationCacheKey('writing', taskId), submitted.evaluationId);
  const task = await fetchWritingTask(taskId);

  return {
    id: submitted.evaluationId,
    taskId,
    taskTitle: task.title,
    content,
    wordCount: content.split(/\s+/).filter(Boolean).length,
    submittedAt: new Date().toISOString(),
    evalStatus: toEvalStatus(submitted.state),
    reviewStatus: 'not_requested',
  };
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
      wordCount: 0,
      submittedAt: item.attemptDate,
      evalStatus: item.evaluationId ? 'completed' : 'processing',
      scoreEstimate: scoreRangeDisplay(item.scoreEstimate),
      reviewStatus: toReviewStatus(item.reviewStatus),
    }));
}

export async function fetchCriteriaDeltas(): Promise<CriteriaDelta[]> {
  const writingSubmissions = await fetchWritingSubmissions();
  const latest = writingSubmissions[0]?.id ?? 'we-001';
  const result = await fetchWritingResult(latest);
  return result.criteria.map((criterion) => ({
    name: criterion.name,
    original: Math.max(criterion.score - 1, 0),
    revised: criterion.score,
    max: criterion.maxScore,
  }));
}

export async function fetchWritingRevisionData(referenceId: string): Promise<{
  originalText: string;
  revisedText: string;
  deltas: CriteriaDelta[];
  unresolvedIssues: string[];
  attemptId: string;
}> {
  let attemptId = referenceId;
  if (referenceId.startsWith('we-')) {
    const summary = await apiRequest<ApiRecord>(`/v1/writing/evaluations/${referenceId}/summary`);
    attemptId = summary.attemptId;
  }

  const revision = await apiRequest<ApiRecord>(`/v1/writing/revisions/${attemptId}`);
  return {
    originalText: revision.baseAttempt?.content ?? '',
    revisedText: revision.revisionDraft?.content ?? revision.baseAttempt?.content ?? '',
    deltas: (revision.deltaSummary ?? []).map((item: ApiRecord) => ({
      name: item.name,
      original: item.original,
      revised: item.revised,
      max: item.max,
    })),
    unresolvedIssues: revision.unresolvedIssues ?? [],
    attemptId: revision.baseAttempt?.attemptId ?? attemptId,
  };
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

export async function fetchRoleCard(taskId: string): Promise<RoleCard> {
  const item = await apiRequest<ApiRecord>(`/v1/speaking/tasks/${taskId}`);
  return {
    id: item.contentId,
    title: item.title,
    profession: item.profession ? titleCase(item.profession) : titleCase(item.professionId),
    setting: item.setting ?? 'Clinical setting',
    patient: item.patient ?? 'Patient',
    brief: item.brief ?? item.caseNotes ?? '',
    tasks: item.tasks ?? [],
    background: item.background ?? item.caseNotes ?? '',
  };
}

export async function fetchSpeakingResult(resultId: string): Promise<SpeakingResult> {
  const summary = await apiRequest<ApiRecord>(`/v1/speaking/evaluations/${resultId}/summary`);
  return {
    id: resultId,
    taskId: summary.taskId,
    taskTitle: summary.taskTitle,
    examFamilyCode: toExamFamilyCode(summary.examFamilyCode),
    examFamilyLabel: summary.examFamilyLabel ?? titleCase(summary.examFamilyCode ?? 'oet'),
    scoreRange: scoreRangeDisplay(summary.scoreRange),
    confidence: toConfidence(summary.confidenceBand),
    confidenceLabel: summary.confidenceLabel ?? `${toConfidence(summary.confidenceBand)} confidence practice estimate`,
    learnerDisclaimer: summary.learnerDisclaimer ?? `Practice estimate only. This is not an official ${summary.examFamilyLabel ?? 'exam'} score.`,
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
  };
}

export async function fetchPhrasingData(resultId: string): Promise<{ title: string; segments: PhrasingSegment[] }> {
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
  };
}

export async function submitSpeakingRecording(
  taskId: string,
  recording: Blob,
  durationSeconds = 120,
): Promise<{ uploadUrl: string; submissionId: string }> {
  const attempt = await ensureAttempt('speaking', taskId, 'self');
  const upload = await apiRequest<ApiRecord>(`/v1/speaking/attempts/${attempt.attemptId}/audio/upload-session`, { method: 'POST' });
  await uploadBinary(upload.uploadUrl, recording);
  await apiRequest(`/v1/speaking/attempts/${attempt.attemptId}/audio/complete`, {
    method: 'POST',
    body: JSON.stringify({
      uploadSessionId: upload.uploadSessionId,
      storageKey: upload.storageKey,
      fileName: `${taskId}.webm`,
      sizeBytes: recording.size,
      durationSeconds,
      captureMethod: 'browser-recording',
      contentType: recording.type || 'audio/webm',
    }),
  });
  const submitted = await apiRequest<ApiRecord>(`/v1/speaking/attempts/${attempt.attemptId}/submit`, { method: 'POST' });
  const evaluationId = typeof submitted.evaluationId === 'string' ? submitted.evaluationId : '';
  if (!evaluationId) {
    throw new Error('Speaking evaluation was not queued. Please try again.');
  }

  cacheRemove(attemptCacheKey('speaking', taskId));
  cacheSet(evaluationCacheKey('speaking', taskId), evaluationId);
  return { uploadUrl: upload.uploadUrl, submissionId: evaluationId };
}

export async function fetchReadingTask(taskId: string): Promise<ReadingTask> {
  const task = await apiRequest<ApiRecord>(`/v1/reading/tasks/${taskId}`);
  return {
    id: task.contentId,
    title: task.title,
    part: task.part ?? 'C',
    timeLimit: task.timeLimitSeconds ?? task.estimatedDurationMinutes * 60,
    texts: (task.texts ?? []).map((text: ApiRecord) => ({ id: text.id, title: text.title, content: text.content })),
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

export async function submitReadingAnswers(taskId: string, answers: Record<string, string>): Promise<ReadingResult> {
  const attempt = await ensureAttempt('reading', taskId, 'exam');
  await apiRequest(`/v1/reading/attempts/${attempt.attemptId}/answers`, { method: 'PATCH', body: JSON.stringify({ answers }) });
  const submitted = await apiRequest<ApiRecord>(`/v1/reading/attempts/${attempt.attemptId}/submit`, { method: 'POST' });
  cacheRemove(attemptCacheKey('reading', taskId));
  cacheSet(evaluationCacheKey('reading', taskId), submitted.evaluationId);
  return fetchReadingResult(taskId);
}

export async function fetchReadingResult(taskId: string): Promise<ReadingResult> {
  const evaluationId = await latestEvaluationIdForContent(taskId, 'reading');
  if (!evaluationId) {
    throw new Error('Reading result not found');
  }

  const [evaluation, task] = await Promise.all([
    apiRequest<ApiRecord>(`/v1/reading/evaluations/${evaluationId}`),
    fetchReadingTask(taskId),
  ]);
  const attempt = await apiRequest<ApiRecord>(`/v1/reading/attempts/${evaluation.attemptId}`);
  const answers = attempt.answers ?? {};

  const items = task.questions.map((question: ReadingTask['questions'][number], index) => {
    const userAnswer = answers[question.id] ?? '';
    const isCorrect = String(userAnswer).trim().toLowerCase() === String(question.correctAnswer).trim().toLowerCase();
    return {
      id: `ri-${index + 1}`,
      number: question.number,
      text: question.text,
      userAnswer,
      correctAnswer: question.correctAnswer,
      isCorrect,
      errorType: isCorrect ? '' : readingErrorType(question),
      explanation: (question as ReadingTask['questions'][number] & { explanation?: string }).explanation ?? (isCorrect ? 'Correct.' : 'Review the source text carefully and focus on exact detail.'),
    };
  });

  const totalQuestions = items.length;
  const score = items.filter((item) => item.isCorrect).length;
  const percentage = totalQuestions === 0 ? 0 : Math.round((score / totalQuestions) * 100);

  return {
    taskId,
    title: task.title,
    score,
    totalQuestions,
    percentage,
    grade: percentage >= 80 ? 'B+' : percentage >= 65 ? 'C+' : 'C',
    errorClusters: [
      { type: 'Inference', count: items.filter((item) => !item.isCorrect && item.errorType === 'Inference').length, total: Math.max(1, items.filter((item) => item.errorType === 'Inference').length), percentage: percentage },
      { type: 'Detail Extraction', count: items.filter((item) => !item.isCorrect && item.errorType === 'Detail Extraction').length, total: Math.max(1, items.filter((item) => item.errorType === 'Detail Extraction').length), percentage: percentage },
    ],
    items,
  };
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
  cacheRemove(attemptCacheKey('listening', taskId));
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

function mapMockReport(report: ApiRecord): MockReport {
  return {
    id: report.id,
    title: report.title,
    date: report.date,
    overallScore: report.overallScore,
    summary: report.summary,
    subTests: (report.subTests ?? []).map((subtest: ApiRecord) => ({
      id: subtest.id,
      name: subtest.name,
      score: subtest.score,
      rawScore: subtest.rawScore,
      ...mockSubtestColors(subtest.name),
    })),
    weakestCriterion: report.weakestCriterion,
    priorComparison: report.priorComparison,
  };
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
        ? 'Full OET Mock'
        : `${titleCase(config.subType ?? 'subtest')} Mock`,
      type: config.mockType === 'sub' ? 'sub' : 'full',
      subType: config.subType ? toSubTest(config.subType) : undefined,
      mode: config.mode === 'practice' ? 'practice' : 'exam',
      profession: titleCase(config.profession ?? 'medicine'),
      strictTimer: Boolean(config.strictTimer),
      includeReview: Boolean(config.includeReview),
      reviewSelection: config.reviewSelection ?? 'none',
    },
    sectionStates: (session.sectionStates ?? []).map((section: ApiRecord) => ({
      id: section.id,
      title: section.title,
      state: section.state,
      reviewAvailable: Boolean(section.reviewAvailable),
      reviewSelected: Boolean(section.reviewSelected),
      launchRoute: section.launchRoute,
    })),
  };
}

export async function createMockSession(config: {
  type: 'full' | 'sub';
  subType?: string;
  mode: 'practice' | 'exam';
  profession: string;
  strictTimer: boolean;
  reviewSelection: MockConfig['reviewSelection'];
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

export async function fetchReadiness(): Promise<ReadinessData> {
  const readiness = await apiRequest<ApiRecord>('/v1/readiness');
  return {
    targetDate: readiness.targetDate,
    weeksRemaining: readiness.weeksRemaining,
    overallRisk: titleCase(readiness.overallRisk) as ReadinessData['overallRisk'],
    recommendedStudyHours: readiness.recommendedStudyHours,
    weakestLink: readiness.weakestLink,
    subTests: (readiness.subTests ?? []).map((item: ApiRecord) => ({
      id: item.id,
      name: item.name,
      readiness: item.readiness,
      target: item.target,
      status: item.status,
      color: item.name === 'Writing' ? '#e11d48' : item.name === 'Speaking' ? '#7c3aed' : item.name === 'Reading' ? '#2563eb' : '#4f46e5',
      bg: item.name === 'Writing' ? '#fff1f2' : item.name === 'Speaking' ? '#f5f3ff' : item.name === 'Reading' ? '#eff6ff' : '#eef2ff',
      barColor: item.name === 'Writing' ? '#fb7185' : item.name === 'Speaking' ? '#a78bfa' : item.name === 'Reading' ? '#60a5fa' : '#818cf8',
      isWeakest: Boolean(item.isWeakest),
    })),
    blockers: readiness.blockers ?? [],
    evidence: readiness.evidence,
  };
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
  const response = await apiRequest<{ items: ApiRecord[] }>('/v1/submissions');
  return response.items.map((item) => ({
    id: item.submissionId,
    contentId: item.contentId,
    taskName: item.taskName,
    subTest: toSubTest(item.subtest),
    attemptDate: item.attemptDate,
    scoreEstimate: scoreRangeDisplay(item.scoreEstimate ?? ''),
    reviewStatus: toReviewStatus(item.reviewStatus),
    evaluationId: item.evaluationId ?? undefined,
    state: item.state ?? undefined,
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
    status: titleCase(summary.status),
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
}): Promise<BillingQuote> {
  const params = new URLSearchParams();
  params.set('productType', input.productType);
  params.set('quantity', String(input.quantity));
  if (input.priceId) params.set('priceId', input.priceId);
  if (input.couponCode) params.set('couponCode', input.couponCode);
  if (input.addOnCodes && input.addOnCodes.length > 0) params.set('addOnCodes', input.addOnCodes.join(','));
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
  quoteId?: string | null;
}): Promise<{ checkoutUrl: string; checkoutSessionId: string; quoteId?: string | null; totalAmount?: number; currency?: string }> {
  const response = await apiRequest<ApiRecord>('/v1/billing/checkout-sessions', {
    method: 'POST',
    body: JSON.stringify({
      productType: input.productType,
      quantity: input.quantity,
      priceId: input.priceId ?? null,
      couponCode: input.couponCode ?? null,
      addOnCodes: input.addOnCodes ?? null,
      quoteId: input.quoteId ?? null,
      idempotencyKey: crypto.randomUUID?.() ?? String(Date.now()),
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

export async function downloadInvoice(invoiceId: string): Promise<string> {
  return fetchAuthorizedObjectUrl(`/v1/billing/invoices/${encodeURIComponent(invoiceId)}/download`);
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
    })),
  };
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

export async function submitCalibrationCase(caseId: string, payload: { scores: Record<string, number>; notes?: string }): Promise<{ success: boolean }> {
  return apiRequest(`/v1/expert/calibration/cases/${encodeURIComponent(caseId)}/submit`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

// ─── Admin / CMS API ───

export async function fetchAdminContent(params?: { type?: string; profession?: string; status?: string; search?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.type) qs.set('type', params.type);
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

export async function createAdminContent(payload: { contentType: string; subtestCode: string; professionId: string; title: string; difficulty: string; estimatedDurationMinutes?: number; description?: string; caseNotes?: string; modelAnswer?: string; criteriaFocus?: string }) {
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

export async function inviteAdminUser(payload: { name: string; email: string; role: string; professionId?: string }) {
  return apiRequest('/v1/admin/users/invite', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminUserStatus(userId: string, payload: { status: string; reason?: string }) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/status`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function deleteAdminUser(userId: string, payload?: { reason?: string }) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/delete`, { method: 'POST', body: JSON.stringify(payload ?? {}) });
}

export async function restoreAdminUser(userId: string, payload?: { reason?: string }) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/restore`, { method: 'POST', body: JSON.stringify(payload ?? {}) });
}

export async function adjustAdminUserCredits(userId: string, payload: { amount: number; reason?: string }) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/credits`, { method: 'POST', body: JSON.stringify(payload) });
}

export async function triggerAdminUserPasswordReset(userId: string) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/password-reset`, { method: 'POST' });
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

function normalizeBillingCode(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 64) || 'custom-plan';
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
}) {
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
}) {
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
    }),
  });
}

export async function fetchAdminBillingAddOns(params?: { status?: string }) {
  const qs = params?.status ? `?status=${encodeURIComponent(params.status)}` : '';
  return apiRequest(`/v1/admin/billing/add-ons${qs}`);
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
}) {
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
}) {
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
    }),
  });
}

export async function fetchAdminBillingCoupons(params?: { status?: string }) {
  const qs = params?.status ? `?status=${encodeURIComponent(params.status)}` : '';
  return apiRequest(`/v1/admin/billing/coupons${qs}`);
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

export async function updateAdminFreezePolicy(payload: unknown) {
  return apiRequest('/v1/admin/freeze/policy', {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function createAdminManualFreeze(payload: unknown) {
  return apiRequest('/v1/admin/freeze/manual', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function approveAdminFreeze(freezeId: string, payload: unknown) {
  return apiRequest(`/v1/admin/freeze/${encodeURIComponent(freezeId)}/approve`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function rejectAdminFreeze(freezeId: string, payload: unknown) {
  return apiRequest(`/v1/admin/freeze/${encodeURIComponent(freezeId)}/reject`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function endAdminFreeze(freezeId: string, payload: unknown) {
  return apiRequest(`/v1/admin/freeze/${encodeURIComponent(freezeId)}/end`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function forceEndAdminFreeze(freezeId: string, payload: unknown) {
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

// ── Vocabulary ────────────────────────────────────────────────────────────────

export async function fetchVocabularyTerms(params?: { examTypeCode?: string; category?: string; profession?: string; search?: string; page?: number; pageSize?: number }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.category) p.set('category', params.category);
  if (params?.profession) p.set('profession', params.profession);
  if (params?.search) p.set('search', params.search);
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

export async function uploadMedia(file: File) {
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

export async function fetchAdminVocabularyItems(params?: {
  profession?: string; category?: string; status?: string; search?: string;
  page?: number; pageSize?: number;
}) {
  const p = new URLSearchParams();
  if (params?.profession) p.set('profession', params.profession);
  if (params?.category) p.set('category', params.category);
  if (params?.status) p.set('status', params.status);
  if (params?.search) p.set('search', params.search);
  if (params?.page) p.set('page', String(params.page));
  if (params?.pageSize) p.set('pageSize', String(params.pageSize));
  const qs = p.toString();
  return apiRequest(`/v1/admin/vocabulary/items${qs ? `?${qs}` : ''}`);
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

export async function fetchAdminVocabularyCategories(params?: { examTypeCode?: string; professionId?: string }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.professionId) p.set('professionId', params.professionId);
  const qs = p.toString();
  return apiRequest(`/v1/admin/vocabulary/categories${qs ? `?${qs}` : ''}`);
}

export async function previewAdminVocabularyImport(file: File) {
  const formData = new FormData();
  formData.append('file', file);
  const response = await fetchWithTimeout(resolveApiUrl('/v1/admin/vocabulary/import/preview'), {
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

export async function bulkImportAdminVocabulary(file: File, dryRun = false) {
  const formData = new FormData();
  formData.append('file', file);
  const response = await fetchWithTimeout(resolveApiUrl(`/v1/admin/vocabulary/import?dryRun=${dryRun}`), {
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

function makeGrammarExercise(type: GrammarExerciseAuthoring['type'], index: number, topicLabel: string): GrammarExerciseAuthoring {
  if (type === 'mcq') {
    return {
      id: `exercise-${index + 1}`,
      sortOrder: index + 1,
      type,
      promptMarkdown: `Which option is correct for the ${topicLabel.toLowerCase()} pattern?`,
      options: [
        { id: 'a', label: 'Incorrect option' },
        { id: 'b', label: 'Correct option' },
        { id: 'c', label: 'Distractor' },
      ],
      correctAnswer: 'b',
      acceptedAnswers: [],
      explanationMarkdown: 'The middle option follows the target structure.',
      difficulty: 'intermediate',
      points: 1,
    };
  }

  if (type === 'matching') {
    return {
      id: `exercise-${index + 1}`,
      sortOrder: index + 1,
      type,
      promptMarkdown: 'Match the sentence fragment to the best ending.',
      options: [
        { left: 'The patient was admitted', right: 'after the assessment' },
        { left: 'The results were discussed', right: 'with the consultant' },
      ],
      correctAnswer: [
        { left: 'The patient was admitted', right: 'after the assessment' },
        { left: 'The results were discussed', right: 'with the consultant' },
      ],
      acceptedAnswers: [],
      explanationMarkdown: 'Keep the time relationship and clinical reference in order.',
      difficulty: 'intermediate',
      points: 2,
    };
  }

  if (type === 'error_correction') {
    return {
      id: `exercise-${index + 1}`,
      sortOrder: index + 1,
      type,
      promptMarkdown: 'Correct the grammar error in the sentence: "The nurse explain the procedure."',
      options: [],
      correctAnswer: 'The nurse explains the procedure.',
      acceptedAnswers: ['The nurse explains the procedure'],
      explanationMarkdown: 'Subject-verb agreement requires explain -> explains.',
      difficulty: 'beginner',
      points: 1,
    };
  }

  if (type === 'sentence_transformation') {
    return {
      id: `exercise-${index + 1}`,
      sortOrder: index + 1,
      type,
      promptMarkdown: 'Rewrite the sentence using a passive structure.',
      options: [],
      correctAnswer: 'The medication was prescribed by the doctor.',
      acceptedAnswers: ['The medication was prescribed by the doctor'],
      explanationMarkdown: 'Use the passive voice to emphasise the action rather than the agent.',
      difficulty: 'intermediate',
      points: 2,
    };
  }

  return {
    id: `exercise-${index + 1}`,
    sortOrder: index + 1,
    type: 'fill_blank',
    promptMarkdown: `Fill the blank: "The ${topicLabel.toLowerCase()} approach is ___."`,
    options: [],
    correctAnswer: 'appropriate',
    acceptedAnswers: ['appropriate'],
    explanationMarkdown: 'Use a simple adjective that matches the context.',
    difficulty: 'beginner',
    points: 1,
  };
}

function buildGrammarDraftFromPrompt(params: { examTypeCode: string; topicSlug?: string; level: string; targetExerciseCount: number; prompt: string; }) {
  const topicSlug = normalizeGrammarTopicSlug(params.topicSlug ?? params.prompt.split(/\s+/).slice(0, 3).join('-'));
  const topicLabel = titleCase(topicSlug);
  const title = `${topicLabel} practice`;
  const contentBlocks: GrammarContentBlockLearner[] = [
    { id: 'intro', sortOrder: 1, type: 'callout', contentMarkdown: `Focus on **${topicLabel.toLowerCase()}** in a clinical context.` },
    { id: 'example', sortOrder: 2, type: 'example', contentMarkdown: 'Example: The patient **has been reviewed** by the team.' },
    { id: 'note', sortOrder: 3, type: 'note', contentMarkdown: 'Watch article use, agreement, and the level of formality.' },
  ];

  const exerciseTypes: GrammarExerciseAuthoring['type'][] = ['mcq', 'fill_blank', 'error_correction', 'sentence_transformation', 'matching'];
  const exercises: GrammarExerciseAuthoring[] = [];
  for (let index = 0; index < Math.max(3, params.targetExerciseCount); index += 1) {
    exercises.push(makeGrammarExercise(exerciseTypes[index % exerciseTypes.length], index, topicLabel));
  }

  return {
    lesson: {
      examTypeCode: params.examTypeCode,
      topicId: topicSlug,
      title,
      description: `Starter lesson created from the prompt: ${params.prompt}`,
      level: params.level,
      category: topicSlug,
      estimatedMinutes: Math.max(8, exercises.length * 2),
      sortOrder: 0,
      sourceProvenance: `Starter draft generated from: ${params.prompt}`,
      prerequisiteLessonIds: [],
      contentBlocks,
      exercises,
    } satisfies GrammarLessonUpsertPayload,
    warning: 'Grounded AI generation is not wired yet. A starter draft was created for editing.',
  };
}

export async function adminGenerateGrammarAiDraft(params: { examTypeCode: string; topicSlug?: string; prompt: string; level: string; targetExerciseCount: number; profession?: string; }) {
  // Grounded, platform-only. The backend builds the AI prompt via
  // IAiGatewayService and refuses ungrounded prompts. On AI-parse failure
  // the server returns a deterministic starter template + `warning`.
  try {
    const response = await apiRequest<{
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
    return response;
  } catch (err) {
    // Last-resort fallback: the backend is unreachable or rejected. Surface
    // the error up; also produce a local starter draft so the admin can
    // continue offline. Matches the "always produce a usable draft" decision.
    if (err instanceof Error && err.message.toLowerCase().includes('network')) {
      const draft = buildGrammarDraftFromPrompt(params);
      const created = await adminCreateGrammarLessonV2(draft.lesson);
      return {
        lessonId: created.id,
        title: draft.lesson.title,
        contentBlockCount: draft.lesson.contentBlocks.length,
        exerciseCount: draft.lesson.exercises.length,
        rulebookVersion: '1.0.0',
        appliedRuleIds: [] as string[],
        warning: `Backend unreachable — a local starter template was created. ${draft.warning ?? ''}`.trim(),
      };
    }
    throw err;
  }
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

export async function fetchGrammarEntitlement(): Promise<GrammarEntitlement> {
  return apiRequest<GrammarEntitlement>('/v1/grammar/entitlement');
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
  return apiRequest('/v1/marketplace/profile', {
    method: 'PUT',
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

export async function fetchAnnotationTemplates(params?: { subtestCode?: string; criterionCode?: string }) {
  const qs = new URLSearchParams();
  if (params?.subtestCode) qs.set('subtestCode', params.subtestCode);
  if (params?.criterionCode) qs.set('criterionCode', params.criterionCode);
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
  idempotencyKey: string;
}) {
  return apiRequest('/v1/private-speaking/bookings', {
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

export async function deleteExpertPrivateSpeakingAvailability(ruleId: string) {
  return apiRequest(`/v1/expert/private-speaking/availability/${encodeURIComponent(ruleId)}`, { method: 'DELETE' });
}

export async function cancelExpertPrivateSpeakingSession(bookingId: string, reason?: string) {
  return apiRequest(`/v1/expert/private-speaking/sessions/${encodeURIComponent(bookingId)}/cancel`, {
    method: 'POST',
    body: JSON.stringify({ reason: reason || null }),
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

export async function fetchAdminPrivateSpeakingAuditLogs(params?: { bookingId?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.bookingId) qs.set('bookingId', params.bookingId);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 50));
  return apiRequest(`/v1/admin/private-speaking/audit-logs?${qs}`);
}

// ── Orphan Endpoint Wiring ────────────────────────────

export async function fetchStudyPlanDrift() {
  return apiRequest('/v1/learner/study-plan/drift');
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
}

export interface SponsoredLearner {
  id: string;
  learnerEmail: string;
  learnerUserId: string | null;
  status: string;
  createdAt: string;
  revokedAt: string | null;
}

export interface SponsorBillingData {
  sponsorName: string;
  organizationName: string | null;
  totalSponsorships: number;
  totalSpend: number;
  currentMonthSpend: number;
  billingCycle: string;
  invoices: Array<Record<string, unknown>>;
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
