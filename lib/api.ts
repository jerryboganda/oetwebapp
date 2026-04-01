import { ensureFreshAccessToken } from './auth-client';
import { env } from './env';
import type {
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
  SpeakingReviewDetail,
  WritingReviewDetail,
} from './types/expert';

const API_BASE_URL = env.apiBaseUrl;
type ApiRecord = Record<string, any>;

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
      if (process.env.NODE_ENV === 'development') {
        console.warn('[API] Unknown subtest code:', code, '— defaulting to Writing');
      }
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
  return (value ?? '').replace(/-/g, '–');
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
      const response = await fetch(resolveApiUrl(path), {
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
        } catch {
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
  const response = await fetch(resolveApiUrl(pathOrUrl), {
    method: 'PUT',
    headers: await getHeaders(pathOrUrl, { 'Content-Type': blob.type || 'audio/webm' }, { json: false }),
    body: blob,
  });

  if (!response.ok) {
    let message = `Upload failed: ${response.status}`;
    try {
      const error = await response.json();
      message = error.message ?? error.title ?? message;
    } catch {
      // noop
    }
    throw new Error(message);
  }
}

export async function fetchAuthorizedObjectUrl(pathOrUrl: string): Promise<string> {
  const response = await fetch(resolveApiUrl(pathOrUrl), {
    headers: await getHeaders(pathOrUrl, undefined, { json: false }),
  });

  if (!response.ok) {
    let message = `Request failed: ${response.status}`;
    try {
      const error = await response.json();
      message = error.message ?? error.title ?? message;
    } catch {
      // noop
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
    } catch {
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
  const goals = bootstrap.goals;

  return {
    id: user.userId,
    email: user.email,
    displayName: user.displayName,
    profession: titleCase(goals.professionId ?? user.activeProfessionId),
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

export async function fetchSpeakingHome(): Promise<ApiRecord> {
  const data = await apiRequest<ApiRecord>('/v1/speaking/home');
  return normalizeRouteValues(data);
}

export async function fetchMocksHome(): Promise<ApiRecord> {
  const data = await apiRequest<ApiRecord>('/v1/mocks');
  return normalizeRouteValues(data);
}

export async function postSpeakingDeviceCheck(payload: { microphoneGranted: boolean; networkStable: boolean; deviceType?: string; }): Promise<ApiRecord> {
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
    estimatedScoreRange: scoreRangeDisplay(summary.scoreRange),
    estimatedGradeRange: scoreRangeDisplay(summary.gradeRange ?? 'Pending'),
    confidenceLabel: toConfidence(summary.confidenceBand),
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
    scoreRange: scoreRangeDisplay(summary.scoreRange),
    confidence: toConfidence(summary.confidenceBand),
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
  const attempt = await ensureAttempt('speaking', taskId, 'ai');
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
  cacheRemove(attemptCacheKey('speaking', taskId));
  cacheSet(evaluationCacheKey('speaking', taskId), submitted.evaluationId);
  return { uploadUrl: upload.uploadUrl, submissionId: submitted.evaluationId };
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
      explanation: (question as any).explanation ?? (isCorrect ? 'Correct.' : 'Review the source text carefully and focus on exact detail.'),
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
  const attempt = await apiRequest<ApiRecord>(`/v1/listening/attempts/${evaluation.attemptId}`);
  const answers = attempt.answers ?? {};

  const questions = (task.questions ?? []).map((question: ApiRecord, index: number) => {
    const userAnswer = answers[question.id] ?? '';
    const isCorrect = String(userAnswer).trim().toLowerCase() === String(question.correctAnswer).trim().toLowerCase();
    const itemReview = (evaluation.itemReview ?? []).find((item: ApiRecord) => item.questionId === question.id);
    const transcript = itemReview?.transcript ?? null;
    return {
      id: `lrq-${index + 1}`,
      number: question.number,
      text: question.text,
      userAnswer,
      correctAnswer: question.correctAnswer,
      isCorrect,
      explanation: itemReview?.explanation ?? question.explanation ?? (isCorrect ? 'Correct.' : 'Review the transcript clue and distractor pattern.'),
      allowTranscriptReveal: Boolean(transcript?.allowed),
      transcriptExcerpt: transcript?.excerpt ?? question.transcriptExcerpt ?? undefined,
      distractorExplanation: transcript?.distractorExplanation ?? question.distractorExplanation ?? undefined,
    };
  });

  const recommendedNextDrill = evaluation.recommendedNextDrill ?? {};

  return {
    id: taskId,
    title: task.title,
    score: questions.filter((question: ListeningResult['questions'][number]) => question.isCorrect).length,
    total: questions.length,
    questions,
    recommendedDrill: {
      id: recommendedNextDrill.id ?? 'listening-drill-detail_capture',
      title: recommendedNextDrill.title ?? 'Exact Detail Capture Drill',
      description: recommendedNextDrill.rationale ?? 'Practise the listening error type that appeared most often in this result.',
    },
  };
}

export async function fetchListeningDrill(drillId: string): Promise<ListeningDrill> {
  const drill = normalizeRouteValues(await apiRequest<ApiRecord>(`/v1/listening/drills/${drillId}`));
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
          id: evaluation.recommendedNextDrill.id,
          title: evaluation.recommendedNextDrill.title,
          description: evaluation.recommendedNextDrill.rationale ?? 'Continue with the recommended drill.',
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
    const existing = grouped.get(label) ?? { date: label };
    (existing as any)[String(point.subtest).toLowerCase()] = parseScoreValue(point.scoreRange);
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

export async function fetchFocusAreas(): Promise<FocusArea[]> {
  const criteria = await apiRequest<ApiRecord[]>('/v1/reference/criteria');
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
  const response = await fetch(resolveApiUrl(path), {
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
