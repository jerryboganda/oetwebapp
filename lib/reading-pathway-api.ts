/**
 * Reading Pathway API client.
 *
 * Covers WS7 onboarding/diagnostic, WS9 vocabulary (SM-2), and all other
 * pathway sub-features (lessons, strategies, analytics, community, AI).
 *
 * Backend base route: /v1/reading-pathway/
 * Pattern mirrors lib/reading-authoring-api.ts.
 */

import { env } from './env';
import { ensureFreshAccessToken } from './auth-client';
import { fetchWithTimeout } from './network/fetch-with-timeout';

// ── DTOs ─────────────────────────────────────────────────────────────────────

export interface LearnerReadingProfileDto {
  userId: string;
  currentStage: string;
  targetBand: number;
  examDate: string | null;
  hoursPerWeek: number;
  profession: string;
  diagnosticCompleted: boolean;
  createdAt: string;
}

export interface OnboardingRequest {
  targetBand: number;
  examDate: string | null;
  hoursPerWeek: number;
  profession: string;
  hasTakenBefore: boolean;
  previousScore: number | null;
  selfRatedSpeed: number;
  selfRatedVocab: number;
}

export interface DiagnosticResultDto {
  sessionId: string;
  estimatedScore: number;
  skillBreakdown: Record<string, number>;
  roadmapWeeks: number;
  timeAnalysis: {
    partA: number;
    partB: number;
    partC: number;
  };
  vocabFlagged: string[];
}

export interface PathwayDto {
  currentStage: string;
  totalWeeks: number;
  currentWeek: number;
  weeksRemaining: number;
  readinessScore: number;
}

export interface DailyPlanItemDto {
  id: string;
  itemType: string;
  skillCode: string | null;
  estimatedMinutes: number;
  status: string;
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
  questionIds: string[];
  timeLimit: number | null;
  mode: string;
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
  explanation: ExplanationDto | null;
  skillScoreDelta: number;
  newSkillScore: number;
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
  step: string;
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

// ── HTTP helper ───────────────────────────────────────────────────────────────

function resolveUrl(path: string): string {
  if (path.startsWith('http')) return path;
  const base = env.apiBaseUrl || '';
  return base ? `${base.replace(/\/$/, '')}${path}` : path;
}

const CSRF_SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const token = await ensureFreshAccessToken();
  const headers = new Headers(init?.headers);
  headers.set('Accept', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (init?.body && typeof init.body === 'string' && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  const method = (init?.method ?? 'GET').toUpperCase();
  if (!CSRF_SAFE_METHODS.has(method) && typeof document !== 'undefined' && !headers.has('x-csrf-token')) {
    const csrfMatch = document.cookie.match(/(?:^|;\s*)oet_csrf=([^;]+)/);
    if (csrfMatch) headers.set('x-csrf-token', csrfMatch[1]);
  }
  const res = await fetchWithTimeout(resolveUrl(path), { ...init, headers });
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

// ── Profile & Onboarding ──────────────────────────────────────────────────────

export const getReadingProfile = () =>
  api<LearnerReadingProfileDto>('/v1/reading-pathway/profile');

export const submitOnboarding = (req: OnboardingRequest) =>
  api<LearnerReadingProfileDto>('/v1/reading-pathway/onboarding', {
    method: 'POST',
    body: JSON.stringify(req),
  });

// ── Diagnostic ────────────────────────────────────────────────────────────────

export const startDiagnostic = () =>
  api<PracticeSessionDto>('/v1/reading-pathway/diagnostic/start', { method: 'POST' });

export const submitDiagnostic = (sessionId: string, answers: Record<string, string>) =>
  api<DiagnosticResultDto>('/v1/reading-pathway/diagnostic/submit', {
    method: 'POST',
    body: JSON.stringify({ sessionId, answers }),
  });

// ── Pathway & Daily Plan ──────────────────────────────────────────────────────

export const getPathway = () =>
  api<PathwayDto>('/v1/reading-pathway/pathway');

export const getTodayPlan = () =>
  api<DailyPlanDto>('/v1/reading-pathway/daily-plan');

export const markPlanItemComplete = (itemId: string) =>
  api<void>(`/v1/reading-pathway/daily-plan/${encodeURIComponent(itemId)}/complete`, { method: 'POST' });

export const skipPlanItem = (itemId: string, reason: string) =>
  api<void>(`/v1/reading-pathway/daily-plan/${encodeURIComponent(itemId)}/skip`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });

// ── Practice Sessions ─────────────────────────────────────────────────────────

export const startPracticeSession = (req: PracticeSessionStartRequest) =>
  api<PracticeSessionDto>('/v1/reading-pathway/sessions', {
    method: 'POST',
    body: JSON.stringify(req),
  });

export const submitAnswer = (sessionId: string, req: AnswerSubmitRequest) =>
  api<AnswerResultDto>(`/v1/reading-pathway/sessions/${encodeURIComponent(sessionId)}/answers`, {
    method: 'POST',
    body: JSON.stringify(req),
  });

export const endPracticeSession = (sessionId: string) =>
  api<void>(`/v1/reading-pathway/sessions/${encodeURIComponent(sessionId)}/end`, { method: 'POST' });

export const getExplanation = (questionId: string, wrongOption: string) =>
  api<ExplanationDto>(
    `/v1/reading-pathway/questions/${encodeURIComponent(questionId)}/explanation?wrongOption=${encodeURIComponent(wrongOption)}`,
  );

// ── Mock Tests ────────────────────────────────────────────────────────────────

export const startMock = (mockTemplateId: string) =>
  api<PracticeSessionDto>('/v1/reading-pathway/mocks/start', {
    method: 'POST',
    body: JSON.stringify({ mockTemplateId }),
  });

export const getMockResults = (sessionId: string) =>
  api<MockResultDto>(`/v1/reading-pathway/mocks/${encodeURIComponent(sessionId)}/results`);

// ── Vocabulary (SM-2 spaced repetition) ──────────────────────────────────────

/** Fetch all vocab items due for review today (SM-2 schedule). */
export const getVocabDue = () =>
  api<VocabItemDto[]>('/v1/reading-pathway/vocab/due');

/** Add a word to the learner's personal vocab deck. */
export const addVocabWord = (word: string, source: string) =>
  api<VocabItemDto>('/v1/reading-pathway/vocab/words', {
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
export const subscribeToVocabList = (slug: string) =>
  api<void>(`/v1/reading-pathway/vocab/lists/${encodeURIComponent(slug)}/subscribe`, { method: 'POST' });

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
  api<void>(`/v1/reading-pathway/strategies/${encodeURIComponent(slug)}/read`, { method: 'POST' });

// ── Analytics ─────────────────────────────────────────────────────────────────

export const getReadingDashboard = () =>
  api<ReadingDashboardDto>('/v1/reading-pathway/analytics/dashboard');

export const getSkillRadar = () =>
  api<SkillRadarDto>('/v1/reading-pathway/analytics/skill-radar');

export const getScoreHistory = () =>
  api<ScoreHistoryDto>('/v1/reading-pathway/analytics/score-history');

export const getReadinessScore = () =>
  api<{ score: number; label: string }>('/v1/reading-pathway/analytics/readiness');

export const getActivityCalendar = () =>
  api<ActivityCalendarDto>('/v1/reading-pathway/analytics/activity');

// ── Community ─────────────────────────────────────────────────────────────────

export const getQuestionComments = (questionId: string) =>
  api<CommentDto[]>(`/v1/reading-pathway/community/${encodeURIComponent(questionId)}/comments`);

export const postComment = (questionId: string, body: string) =>
  api<CommentDto>(`/v1/reading-pathway/community/${encodeURIComponent(questionId)}/comments`, {
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
