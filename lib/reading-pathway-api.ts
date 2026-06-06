/**
 * Reading Pathway API client.
 *
 * Covers WS7 diagnostic, WS9 vocabulary (SM-2), and all other
 * pathway sub-features (lessons, strategies, analytics, community, AI).
 *
 * Backend base route: /v1/reading-pathway/
 * Pattern mirrors lib/reading-authoring-api.ts.
 */

import { apiClient } from './api';
import { env } from './env';
import { ensureFreshAccessToken } from './auth-client';
import { fetchWithTimeout } from './network/fetch-with-timeout';
import { oetGradeFromScaled } from './scoring';

// ── DTOs ─────────────────────────────────────────────────────────────────────

export interface LearnerReadingProfileDto {
  userId: string;
  currentStage: string;
  targetBand: string | null;
  examDate: string | null;
  hoursPerWeek: number | null;
  profession: string | null;
  hasTakenBefore: boolean;
  previousScore: number | null;
  selfRatedSpeed: number | null;
  selfRatedVocabulary: number | null;
  readinessScore: number | null;
  predictedScore: number | null;
  onboardingCompletedAt: string | null;
  pathwayGeneratedAt: string | null;
  weeksRemaining: number | null;
  diagnosticCompleted: boolean;
}

export interface DiagnosticStartDto {
  sessionId: string;
  questionIds: string[];
  timeLimitMinutes: number;
}

export interface DiagnosticQuestionDto {
  id: string;
  partCode: string;
  questionType: string;
  displayOrder: number;
  stem: string;
  options: unknown;
  textTitle: string | null;
  textHtml: string | null;
  skillCode: string | null;
}

export interface DiagnosticResultDto {
  sessionId: string;
  score: number;
  totalQuestions: number;
  skillScores: Record<string, number>;
  estimatedOetBand: string;
  estimatedScaledScore: number;
  durationSeconds: number | null;
  roadmapWeeks: number;
  completedAt: string | null;
}

export interface PathwayWeekDto {
  weekNumber: number;
  phase: string;
  focusSkills: string[];
  theme: string;
  mockScheduled: boolean;
  isCompleted: boolean;
}

export interface PathwayDto {
  currentStage: string;
  totalWeeks: number;
  currentWeek: number;
  weeksRemaining: number;
  readinessScore: number;
  predictedScore: number | null;
  generatedAt: string | null;
  weeks: PathwayWeekDto[];
}

export interface DailyPlanItemDto {
  id: string;
  itemType: string;
  focusSkill: string | null;
  skillCode: string | null;
  estimatedMinutes: number;
  status: string;
  payloadJson: string;
  title: string;
  description: string;
}

export interface DailyPlanDto {
  items: DailyPlanItemDto[];
  totalMinutes: number;
  completedCount: number;
  streak: number;
}

export interface PracticeSessionStartRequest {
  focusSkill: string | null;
  targetMinutes: number;
  sessionType: string;
}

export interface PracticeSessionDto {
  sessionId: string;
  questionIds?: string[];
  questionCount?: number;
  timeLimitMinutes?: number | null;
  sessionType?: string;
}

export interface PracticeSessionPassageDto {
  id: string;
  title: string;
  bodyHtml: string;
  partCode: number;
}

export interface PracticeSessionQuestionDto {
  id: string;
  passageId: string;
  stem: string;
  options: { key: string; text: string }[];
  questionType: string;
  partCode: number;
  skillCode?: string;
}

interface RawPracticeSessionQuestionDto extends Omit<PracticeSessionQuestionDto, 'options' | 'skillCode'> {
  options: unknown;
  skillCode?: string | null;
}

interface RawPracticeSessionQuestionsDto {
  sessionId: string;
  mode: 'drill' | 'review' | 'wrong_review';
  focusSkill: string | null;
  timeLimitSeconds: number | null;
  questions: RawPracticeSessionQuestionDto[];
  passages: PracticeSessionPassageDto[];
}

export interface PracticeSessionQuestionsDto extends Omit<RawPracticeSessionQuestionsDto, 'questions'> {
  questions: PracticeSessionQuestionDto[];
}

export interface AnswerSubmitRequest {
  questionId: string;
  selectedOption: string;
  timeSpentSeconds: number;
}

export interface ExplanationDto {
  whyCorrect: string;
  whyWrong: string;
  trapName: string;
  avoidTip: string;
}

export interface AnswerResultDto {
  isCorrect: boolean;
  explanation: string | null;
  skillScoreDelta?: number;
  newSkillScore?: number;
}

export interface MockResultDto {
  sessionId: string;
  rawScore: number;
  scaledScore: number;
  grade: string;
  sectionBreakdown: Record<string, number>;
  skillBreakdown: Record<string, number>;
  timeMap: Record<string, number>;
}

// ── Vocabulary DTOs ───────────────────────────────────────────────────────────

export interface VocabItemDto {
  id: string;
  word: string;
  definitionEn: string;
  definitionAr: string;
  pronunciationIpa: string;
  healthcareContext: string;
  exampleEn: string;
  exampleAr: string;
  nextReviewAt: string;
  intervalDays: number;
  easiness: number;
  repetitions: number;
  retentionScore: number;
}

export interface VocabStatsDto {
  total: number;
  mastered: number;
  learning: number;
  struggling: number;
  dueToday: number;
  averageRetention: number;
}

export interface VocabularyListDto {
  id: string;
  slug: string;
  name: string;
  description: string;
  wordCount: number;
  isSubscribed: boolean;
  previewWords: string[];
}

// ── Lesson DTOs ───────────────────────────────────────────────────────────────

export interface ReadingLessonDto {
  id: string;
  slug: string;
  title: string;
  skillCode: string;
  orderIndex: number;
  estimatedMinutes: number;
  videoUrl: string | null;
  bodyMarkdown: string;
  isLocked: boolean;
}

export interface LessonProgressDto {
  lessonId: string;
  videoWatched: boolean;
  bodyRead: boolean;
  drill1: boolean;
  drill2: boolean;
  drill3: boolean;
  quizScore: number | null;
  completedAt: string | null;
}

export interface ReadingLessonWithProgressDto {
  lesson: ReadingLessonDto;
  progress: LessonProgressDto | null;
}

export interface LessonProgressRequest {
  videoWatched?: boolean;
  bodyRead?: boolean;
  drill1Completed?: boolean;
  drill2Completed?: boolean;
  drill3Completed?: boolean;
  quizScore?: number;
}

// ── Strategy DTOs ─────────────────────────────────────────────────────────────

export interface ReadingStrategyDto {
  id: string;
  slug: string;
  title: string;
  category: string;
  difficulty: string;
  estimatedReadMinutes: number;
  unlockStage: string;
  isRead: boolean;
}

export interface ReadingStrategyWithProgressDto {
  strategy: ReadingStrategyDto & { bodyMarkdown: string; relatedSlugs: string[] };
  readAt: string | null;
}

// ── Analytics DTOs ────────────────────────────────────────────────────────────

export interface SkillRadarDto {
  skills: Array<{
    code: string;
    name: string;
    current: number;
    baseline: number;
    target: number;
  }>;
}

export interface ScoreHistoryDto {
  history: Array<{
    date: string;
    score: number;
    sessionType: string;
  }>;
}

export interface ActivityCalendarDto {
  days: Array<{
    date: string;
    minutesPracticed: number;
    questionsAnswered: number;
  }>;
}

export interface ReadingDashboardDto {
  readinessScore: number;
  predictedScore: number;
  streak: number;
  longestStreak: number;
  totalXp: number;
  level: number;
  todayPlan: DailyPlanDto | null;
  skillRadar: SkillRadarDto;
}

// ── Community DTOs ────────────────────────────────────────────────────────────

export interface CommentDto {
  id: string;
  userId: string;
  userDisplayName: string;
  body: string;
  upvotes: number;
  isExpert: boolean;
  createdAt: string;
}

export interface ChatMessage {
  role: string;
  content: string;
}

interface RawDailyPlanItemDto {
  id: string;
  itemType: string;
  focusSkill: string | null;
  estimatedMinutes: number;
  payloadJson: string;
  status: string;
}

interface RawSkillStatsDto {
  current?: Record<string, number>;
  baseline?: Record<string, number>;
}

interface RawDashboardStatsDto {
  skillScores?: Record<string, number>;
  streakStatus?: {
    currentStreak?: number;
    longestStreak?: number;
  };
  predictedScore?: number | null;
  readinessScore?: number | null;
}

interface RawScoreHistoryItemDto {
  id?: string;
  score?: number | null;
  totalQuestions?: number | null;
  completedAt?: string | null;
}

interface RawActivityItemDto {
  date: string;
  questionsAnsweredToday?: number;
  hasActivity?: boolean;
}

interface RawMockResultDto {
  score?: number | null;
  totalQuestions?: number | null;
  durationSeconds?: number | null;
  scaledScore?: number | null;
}

// ── HTTP helper ───────────────────────────────────────────────────────────────
//
// The common case delegates to the shared API client (lib/api.ts) so calls
// inherit auth (Bearer), CSRF header, credentials, timeout,
// retry-on-5xx/408/429, and a normalized `ApiError` (carries status + code +
// retryable, detectable via `isApiError`). Call sites keep passing
// `JSON.stringify(...)` string bodies, forwarded verbatim with a JSON
// Content-Type.
//
// The diagnostic submit/results endpoints need a *bespoke per-call timeout*
// (120s for the slow grading submit; a short ~5s budget for the fast
// poll-with-fallback that recovers a just-submitted result). The shared client
// hard-caps every request at its own 30s default and exposes no timeout knob,
// so those calls — and only those — stay on the raw `fetchWithTimeout` path
// that honors `timeoutMs` exactly. They keep their existing `{ status, detail }`
// error shape, which the diagnostic page's recovery loop already inspects.

function resolveUrl(path: string): string {
  if (path.startsWith('http')) return path;
  const base = env.apiBaseUrl || '';
  return base ? `${base.replace(/\/$/, '')}${path}` : path;
}

const CSRF_SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);

interface RequestInitWithTimeout extends RequestInit {
  timeoutMs?: number;
}

async function apiWithTimeout<T>(path: string, init: RequestInitWithTimeout): Promise<T> {
  const token = await ensureFreshAccessToken();
  const { timeoutMs, ...requestInit } = init;
  const headers = new Headers(requestInit.headers);
  headers.set('Accept', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (requestInit.body && typeof requestInit.body === 'string' && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  const method = (requestInit.method ?? 'GET').toUpperCase();
  if (!CSRF_SAFE_METHODS.has(method) && typeof document !== 'undefined' && !headers.has('x-csrf-token')) {
    const csrfMatch = document.cookie.match(/(?:^|;\s*)oet_csrf=([^;]+)/);
    if (csrfMatch) headers.set('x-csrf-token', csrfMatch[1]);
  }
  const res = await fetchWithTimeout(resolveUrl(path), { ...requestInit, headers }, timeoutMs);
  if (!res.ok) {
    let detail: unknown = null;
    try { detail = await res.json(); } catch { /* ignore */ }
    const err = new Error(`HTTP ${res.status}`) as Error & { status?: number; detail?: unknown };
    err.status = res.status;
    err.detail = detail;
    throw err;
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

async function api<T>(path: string, init?: RequestInitWithTimeout): Promise<T> {
  // A bespoke per-call timeout can't go through the shared client (it caps at
  // its own default and offers no override), so keep those on the raw path.
  if (init?.timeoutMs !== undefined) {
    return apiWithTimeout<T>(path, init);
  }
  return apiClient.request<T>(path, init);
}

// ── Profile ──────────────────────────────────────────────────────

export const getReadingProfile = () =>
  api<LearnerReadingProfileDto>('/v1/reading-pathway/profile');

// ── Diagnostic ────────────────────────────────────────────────────────────────

export const startDiagnostic = () =>
  api<DiagnosticStartDto>('/v1/reading-pathway/diagnostic/start', { method: 'POST' });

export const getDiagnosticQuestions = (sessionId: string) =>
  api<DiagnosticQuestionDto[]>(`/v1/reading-pathway/diagnostic/sessions/${encodeURIComponent(sessionId)}/questions`);

export const submitDiagnostic = (
  sessionId: string,
  answers: Record<string, string>,
  options?: { timeoutMs?: number },
) =>
  api<DiagnosticResultDto>('/v1/reading-pathway/diagnostic/submit', {
    method: 'POST',
    body: JSON.stringify({ sessionId, answers }),
    timeoutMs: options?.timeoutMs ?? 120_000,
  });

export const getDiagnosticResult = (sessionId: string, options?: { timeoutMs?: number }) =>
  api<DiagnosticResultDto>(`/v1/reading-pathway/diagnostic/sessions/${encodeURIComponent(sessionId)}/results`, {
    timeoutMs: options?.timeoutMs,
  });

// ── Pathway & Daily Plan ──────────────────────────────────────────────────────

export const getPathway = () =>
  api<PathwayDto>('/v1/reading-pathway/pathway');

export const getTodayPlan = () =>
  api<RawDailyPlanItemDto[]>('/v1/reading-pathway/plan/today').then(mapDailyPlan);

export const markPlanItemComplete = (itemId: string) =>
  api<void>(`/v1/reading-pathway/plan/items/${encodeURIComponent(itemId)}/complete`, { method: 'POST' });

export const skipPlanItem = (itemId: string, reason: string) =>
  api<void>(`/v1/reading-pathway/plan/items/${encodeURIComponent(itemId)}/skip`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });

// ── Practice Sessions ─────────────────────────────────────────────────────────

export const startPracticeSession = (req: PracticeSessionStartRequest) =>
  api<PracticeSessionDto>('/v1/reading-pathway/practice/sessions', {
    method: 'POST',
    body: JSON.stringify(req),
  });

export const submitAnswer = (sessionId: string, req: AnswerSubmitRequest) =>
  api<AnswerResultDto>(`/v1/reading-pathway/practice/sessions/${encodeURIComponent(sessionId)}/answers`, {
    method: 'POST',
    body: JSON.stringify(req),
  });

export const getPracticeSessionQuestions = (sessionId: string) =>
  api<RawPracticeSessionQuestionsDto>(`/v1/reading-pathway/practice/sessions/${encodeURIComponent(sessionId)}/questions`)
    .then((session) => ({
      ...session,
      questions: session.questions.map((question) => ({
        ...question,
        options: normalizeAnswerOptions(question.options),
        skillCode: question.skillCode ?? undefined,
      })),
    }));

export const endPracticeSession = (sessionId: string) =>
  api<void>(`/v1/reading-pathway/practice/sessions/${encodeURIComponent(sessionId)}/submit`, { method: 'POST' });

export const getExplanation = (questionId: string, wrongOption: string) =>
  api<ExplanationDto>(
    `/v1/reading-pathway/questions/${encodeURIComponent(questionId)}/explanation?wrongOption=${encodeURIComponent(wrongOption)}`,
  );

// ── Mock Tests ────────────────────────────────────────────────────────────────

export const startMock = (mockTemplateId: string) =>
  api<PracticeSessionDto>('/v1/reading-pathway/mocks/start', {
    method: 'POST',
    body: JSON.stringify({ sessionType: 'mock', targetMinutes: 60, mockTemplateId }),
  });

export const getMockResults = (sessionId: string) =>
  api<RawMockResultDto>(`/v1/reading-pathway/mocks/sessions/${encodeURIComponent(sessionId)}/results`).then((raw) => {
    const scaledScore = raw.scaledScore ?? 0;
    const timeMap: Record<string, number> = raw.durationSeconds === null || raw.durationSeconds === undefined
      ? {}
      : { total: raw.durationSeconds };

    return {
      sessionId,
      rawScore: raw.score ?? 0,
      scaledScore,
      grade: gradeFromScaled(scaledScore),
      sectionBreakdown: {},
      skillBreakdown: {},
      timeMap,
    };
  });

// ── Vocabulary (SM-2 spaced repetition) ──────────────────────────────────────

/** Fetch all vocab items due for review today (SM-2 schedule). */
export const getVocabDue = () =>
  api<VocabItemDto[]>('/v1/reading-pathway/vocab/due');

/** Add a word to the learner's personal vocab deck. */
export const addVocabWord = (word: string, source: string) =>
  api<VocabItemDto>('/v1/reading-pathway/vocab', {
    method: 'POST',
    body: JSON.stringify({ word, source }),
  });

/**
 * Submit a SM-2 review rating for a vocab item.
 * @param quality 0 = Forgot | 3 = Hard | 4 = Good | 5 = Easy
 */
export const submitVocabReview = (itemId: string, quality: number) =>
  api<VocabItemDto>(`/v1/reading-pathway/vocab/${encodeURIComponent(itemId)}/review`, {
    method: 'POST',
    body: JSON.stringify({ quality }),
  });

/** Fetch the learner's vocab stats (mastered / learning / due counts). */
export const getVocabStats = () =>
  api<VocabStatsDto>('/v1/reading-pathway/vocab/stats');

/** Fetch the curated vocabulary lists available to the learner. */
export const getVocabLists = () =>
  api<VocabularyListDto[]>('/v1/reading-pathway/vocab/lists');

/** Subscribe the learner to a curated vocabulary list. */
export const subscribeToVocabList = (listId: string) =>
  api<void>(`/v1/reading-pathway/vocab/lists/${encodeURIComponent(listId)}/subscribe`, { method: 'POST' });

// ── Lessons ───────────────────────────────────────────────────────────────────

export const getLessons = () =>
  api<ReadingLessonWithProgressDto[]>('/v1/reading-pathway/lessons');

export const getLesson = (slug: string) =>
  api<ReadingLessonWithProgressDto>(`/v1/reading-pathway/lessons/${encodeURIComponent(slug)}`);

export const updateLessonProgress = (slug: string, req: LessonProgressRequest) =>
  api<LessonProgressDto>(`/v1/reading-pathway/lessons/${encodeURIComponent(slug)}/progress`, {
    method: 'POST',
    body: JSON.stringify(req),
  });

// ── Strategies ────────────────────────────────────────────────────────────────

export interface StrategiesParams {
  category?: string;
  difficulty?: string;
  stage?: string;
}

export const getStrategies = (params?: StrategiesParams) => {
  const qs = new URLSearchParams();
  if (params?.category) qs.set('category', params.category);
  if (params?.difficulty) qs.set('difficulty', params.difficulty);
  if (params?.stage) qs.set('stage', params.stage);
  const suffix = qs.toString() ? `?${qs.toString()}` : '';
  return api<ReadingStrategyDto[]>(`/v1/reading-pathway/strategies${suffix}`);
};

export const getStrategy = (slug: string) =>
  api<ReadingStrategyWithProgressDto>(`/v1/reading-pathway/strategies/${encodeURIComponent(slug)}`);

export const markStrategyRead = (slug: string) =>
  api<void>(`/v1/reading-pathway/strategies/${encodeURIComponent(slug)}/mark-read`, { method: 'POST' });

// ── Analytics ─────────────────────────────────────────────────────────────────

export const getReadingDashboard = () =>
  api<RawDashboardStatsDto>('/v1/reading-pathway/stats/dashboard').then((raw) => ({
    readinessScore: raw.readinessScore ?? 0,
    predictedScore: raw.predictedScore ?? 0,
    streak: raw.streakStatus?.currentStreak ?? 0,
    longestStreak: raw.streakStatus?.longestStreak ?? 0,
    totalXp: 0,
    level: 1,
    todayPlan: null,
    skillRadar: mapSkillRadar({ current: raw.skillScores, baseline: {} }),
  }));

export const getSkillRadar = () =>
  api<RawSkillStatsDto>('/v1/reading-pathway/stats/skills').then(mapSkillRadar);

export const getScoreHistory = () =>
  api<RawScoreHistoryItemDto[]>('/v1/reading-pathway/stats/history').then((history) => ({
    history: history.map((item) => ({
      date: item.completedAt ?? new Date().toISOString(),
      score: item.score ?? 0,
      sessionType: 'mock',
    })),
  }));

export const getReadinessScore = () =>
  api<number>('/v1/reading-pathway/stats/readiness');

export const getActivityCalendar = () =>
  api<RawActivityItemDto[]>('/v1/reading-pathway/stats/calendar').then((days) => ({
    days: days.map((day) => ({
      date: day.date,
      minutesPracticed: day.hasActivity ? 15 : 0,
      questionsAnswered: day.questionsAnsweredToday ?? 0,
    })),
  }));

// ── Community ─────────────────────────────────────────────────────────────────

export const getQuestionComments = (questionId: string) =>
  api<CommentDto[]>(`/v1/reading-pathway/questions/${encodeURIComponent(questionId)}/comments`);

export const postComment = (questionId: string, body: string) =>
  api<CommentDto>(`/v1/reading-pathway/questions/${encodeURIComponent(questionId)}/comments`, {
    method: 'POST',
    body: JSON.stringify({ body }),
  });

// ── AI ────────────────────────────────────────────────────────────────────────

export const askAiAboutPassage = (
  passageId: string,
  message: string,
  history: ChatMessage[],
) =>
  api<{ reply: string; history: ChatMessage[] }>('/v1/reading-pathway/ai/passage-qna', {
    method: 'POST',
    body: JSON.stringify({ passageId, message, history }),
  });

function mapDailyPlan(items: RawDailyPlanItemDto[]): DailyPlanDto {
  const mapped = items.map((item) => ({
    ...item,
    skillCode: item.focusSkill,
    title: dailyPlanTitle(item),
    description: dailyPlanDescription(item),
  }));

  return {
    items: mapped,
    totalMinutes: mapped.reduce((total, item) => total + item.estimatedMinutes, 0),
    completedCount: mapped.filter((item) => item.status === 'completed').length,
    streak: 0,
  };
}

function mapSkillRadar(raw: RawSkillStatsDto): SkillRadarDto {
  const current = raw.current ?? {};
  const baseline = raw.baseline ?? {};
  const codes = ['S1', 'S2', 'S3', 'S4', 'S5', 'S6', 'S7', 'S8'];
  return {
    skills: codes.map((code) => ({
      code,
      name: skillName(code),
      current: current[code] ?? 5,
      baseline: baseline[code] ?? current[code] ?? 5,
      target: 8,
    })),
  };
}

function skillName(code: string): string {
  switch (code) {
    case 'S1': return 'Scanning';
    case 'S2': return 'Skimming';
    case 'S3': return 'Paraphrase';
    case 'S4': return 'Distractors';
    case 'S5': return 'Inference';
    case 'S6': return 'Reference';
    case 'S7': return 'Vocabulary';
    case 'S8': return 'Timing';
    default: return code;
  }
}

function gradeFromScaled(score: number): string {
  return oetGradeFromScaled(score);
}

function dailyPlanTitle(item: RawDailyPlanItemDto): string {
  switch (item.itemType) {
    case 'drill': return item.focusSkill ? `${item.focusSkill} drill` : 'Reading drill';
    case 'vocab_review': return 'Vocabulary review';
    case 'wrong_review': return 'Wrong-answer review';
    case 'strategy_read': return 'Strategy read';
    case 'lesson': return 'Foundation lesson';
    case 'mock': return 'Full mock test';
    default: return item.itemType.replace(/_/g, ' ');
  }
}

function dailyPlanDescription(item: RawDailyPlanItemDto): string {
  const minutes = `${item.estimatedMinutes} min`;
  switch (item.itemType) {
    case 'drill': return `${minutes} targeted practice for your current reading focus.`;
    case 'vocab_review': return `${minutes} spaced repetition vocabulary review.`;
    case 'wrong_review': return `${minutes} revisiting missed questions.`;
    case 'strategy_read': return `${minutes} short reading strategy.`;
    case 'lesson': return `${minutes} guided sub-skill foundation work.`;
    case 'mock': return `${minutes} timed exam simulation.`;
    default: return `${minutes} reading pathway task.`;
  }
}

function normalizeAnswerOptions(options: unknown): { key: string; text: string }[] {
  if (Array.isArray(options)) {
    return options.map((option, index) => ({
      key: optionValue(option, String.fromCharCode(65 + index)),
      text: optionText(option),
    }));
  }

  if (options && typeof options === 'object') {
    return Object.entries(options as Record<string, unknown>).map(([key, value]) => ({
      key,
      text: optionText(value),
    }));
  }

  return [];
}

function optionValue(option: unknown, fallback: string): string {
  if (option && typeof option === 'object') {
    const record = option as Record<string, unknown>;
    return String(record.value ?? record.key ?? record.id ?? fallback);
  }

  return fallback;
}

function optionText(option: unknown): string {
  if (option && typeof option === 'object') {
    const record = option as Record<string, unknown>;
    return String(record.text ?? record.label ?? record.title ?? record.value ?? record.key ?? '');
  }

  return String(option);
}

