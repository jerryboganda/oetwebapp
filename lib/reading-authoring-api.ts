/**
 * Reading Authoring API client. Matches the .NET endpoints in
 * `ReadingAuthoringAdminEndpoints.cs`, `ReadingLearnerEndpoints.cs`,
 * and `ReadingPolicyAdminEndpoints.cs`.
 *
 * See `docs/READING-AUTHORING-PLAN.md` and `docs/READING-AUTHORING-POLICY.md`.
 */

import { env } from './env';
import { ensureFreshAccessToken } from './auth-client';
import { fetchWithTimeout } from './network/fetch-with-timeout';

// ── Types mirror the .NET contracts 1:1 ────────────────────────────────

export type ReadingPartCode = 'A' | 'B' | 'C';

export type ReadingQuestionType =
  | 'MatchingTextReference'
  | 'ShortAnswer'
  | 'SentenceCompletion'
  | 'MultipleChoice3'
  | 'MultipleChoice4';

export type ReadingAttemptStatus = 'InProgress' | 'Submitted' | 'Expired' | 'Abandoned';

export interface ReadingTextDto {
  id: string;
  readingPartId: string;
  displayOrder: number;
  title: string;
  source: string | null;
  bodyHtml: string;
  wordCount: number;
  topicTag: string | null;
}

export interface ReadingQuestionAdminDto {
  id: string;
  readingPartId: string;
  readingTextId: string | null;
  displayOrder: number;
  points: number;
  questionType: ReadingQuestionType;
  stem: string;
  optionsJson: string;
  correctAnswerJson: string;
  acceptedSynonymsJson: string | null;
  caseSensitive: boolean;
  explanationMarkdown: string | null;
  skillTag: string | null;
}

export interface ReadingQuestionLearnerDto {
  id: string;
  readingTextId: string | null;
  displayOrder: number;
  points: number;
  questionType: ReadingQuestionType;
  stem: string;
  options: unknown; // already-parsed JSON from the backend
}

export interface ReadingPartAdminDto {
  id: string;
  partCode: ReadingPartCode;
  timeLimitMinutes: number;
  maxRawScore: number;
  instructions: string | null;
  texts: ReadingTextDto[];
  questions: ReadingQuestionAdminDto[];
}

export interface ReadingStructureAdminDto {
  paperId: string;
  parts: ReadingPartAdminDto[];
}

export interface ReadingValidationReport {
  isPublishReady: boolean;
  issues: Array<{ code: string; severity: 'error' | 'warning'; message: string; targetId: string | null }>;
  counts: { partACount: number; partBCount: number; partCCount: number; totalPoints: number };
}

export interface ReadingAttemptStarted {
  attemptId: string;
  startedAt: string;
  deadlineAt: string;
  partADeadlineAt: string;
  partBCDeadlineAt: string;
  answeredCount: number;
  canResume: boolean;
  paperTitle: string;
  partATimerMinutes: number;
  partBCTimerMinutes: number;
}

export interface ReadingAttemptGraded {
  attemptId?: string;
  rawScore: number;
  maxRawScore: number;
  scaledScore: number;
  gradeLetter: string;
  correctCount: number;
  incorrectCount: number;
  unansweredCount: number;
  answers: Array<{
    questionId: string;
    questionType: string;
    isCorrect: boolean;
    pointsEarned: number;
    maxPoints: number;
  }>;
  reviewRoute?: string | null;
}

export interface ReadingHomePaperDto {
  id: string;
  title: string;
  slug: string;
  difficulty: string;
  estimatedDurationMinutes: number;
  publishedAt: string | null;
  route: string;
  partACount: number;
  partBCount: number;
  partCCount: number;
  totalPoints: number;
  partATimerMinutes: number;
  partBCTimerMinutes: number;
  lastAttempt: {
    attemptId: string;
    status: ReadingAttemptStatus;
    startedAt: string;
    submittedAt: string | null;
    rawScore: number | null;
    scaledScore: number | null;
    route: string;
  } | null;
}

export interface ReadingHomeAttemptDto {
  attemptId: string;
  paperId: string;
  paperTitle: string;
  status: ReadingAttemptStatus;
  startedAt: string;
  deadlineAt: string | null;
  partADeadlineAt: string;
  partBCDeadlineAt: string;
  answeredCount: number;
  totalQuestions: number;
  canResume: boolean;
  route: string;
}

export interface ReadingHomeResultDto {
  attemptId: string;
  paperId: string;
  paperTitle: string;
  rawScore: number;
  maxRawScore: number;
  scaledScore: number;
  gradeLetter: string;
  submittedAt: string | null;
  route: string;
}

export interface ReadingHomeDto {
  intro: string;
  papers: ReadingHomePaperDto[];
  activeAttempts: ReadingHomeAttemptDto[];
  recentResults: ReadingHomeResultDto[];
  policy: {
    partATimerMinutes: number;
    partBCTimerMinutes: number;
    allowPausingAttempt: boolean;
    allowResumeAfterExpiry: boolean;
    showCorrectAnswerOnReview: boolean;
    showExplanationsAfterSubmit: boolean;
  };
  safeDrills: unknown[];
}

export interface ReadingLearnerStructureDto {
  paper: { id: string; title: string; slug: string; subtestCode: string };
  parts: Array<{
    id: string;
    partCode: ReadingPartCode;
    timeLimitMinutes: number;
    maxRawScore: number;
    instructions: string | null;
    texts: Array<{
      id: string; displayOrder: number; title: string; source: string | null;
      bodyHtml: string; wordCount: number; topicTag: string | null;
    }>;
    questions: ReadingQuestionLearnerDto[];
  }>;
}

export interface ReadingPolicyDto {
  id: string;
  attemptsPerPaperPerUser: number;
  attemptCooldownMinutes: number;
  bestScoreDisplay: string;
  showPastAttempts: boolean;
  allowAttemptOnArchivedPaper: boolean;
  partATimerStrictness: string;
  partATimerMinutes: number;
  partBCTimerMinutes: number;
  gracePeriodSeconds: number;
  onExpirySubmitPolicy: string;
  countdownWarningsJson: string;
  enabledQuestionTypesJson: string;
  shortAnswerNormalisation: string;
  shortAnswerAcceptSynonyms: boolean;
  matchingAllowPartialCredit: boolean;
  sentenceCompletionStrictness: string;
  unknownTypeFallbackPolicy: string;
  showExplanationsAfterSubmit: boolean;
  showExplanationsOnlyIfWrong: boolean;
  showCorrectAnswerOnReview: boolean;
  allowResultDownload: boolean;
  allowResultSharing: boolean;
  aiExtractionEnabled: boolean;
  aiExtractionRequireHumanApproval: boolean;
  aiExtractionMaxRetriesPerPaper: number;
  aiExtractionModelOverride: string | null;
  aiExtractionStrictSchemaMode: string;
  questionBankEnabled: boolean;
  assemblyStrategy: string;
  allowLearnerRandomisation: boolean;
  fontScaleUserControl: boolean;
  highContrastMode: boolean;
  screenReaderOptimised: boolean;
  allowPaperReadingMode: boolean;
  extraTimeApprovalWorkflow: boolean;
  requireFreshAuthForSubmit: boolean;
  allowMultipleConcurrentAttempts: boolean;
  attemptIpPinning: string;
  submitRateLimitPerMinute: number;
  autosaveRateLimitPerMinute: number;
  preventMultipleTabs: boolean;
  retainAnswerRowsDays: number;
  retainAttemptHeadersDays: number;
  anonymiseOnAccountDelete: boolean;
  shareAnonymousAnalytics: boolean;
  allowPausingAttempt: boolean;
  autoExpireWorkerEnabled: boolean;
  autoExpireAfterMinutes: number;
  allowResumeAfterExpiry: boolean;
  rowVersion: number;
  updatedAt: string;
  updatedByAdminId: string | null;
}

export interface ReadingAttemptReviewDto {
  attempt: {
    id: string;
    paperId: string;
    status: ReadingAttemptStatus;
    startedAt: string;
    submittedAt: string | null;
    rawScore: number | null;
    maxRawScore: number;
    scaledScore: number | null;
    gradeLetter: string;
    partADeadlineAt: string;
    partBCDeadlineAt: string;
  };
  paper: { id: string; title: string; slug: string; subtestCode: string };
  policy: {
    showCorrectAnswerOnReview: boolean;
    showExplanationsAfterSubmit: boolean;
    showExplanationsOnlyIfWrong: boolean;
  };
  items: Array<{
    questionId: string;
    partCode: ReadingPartCode;
    displayOrder: number;
    questionType: ReadingQuestionType;
    stem: string;
    skillTag: string | null;
    userAnswer: unknown;
    isCorrect: boolean;
    pointsEarned: number;
    maxPoints: number;
    correctAnswer: unknown | null;
    explanationMarkdown: string | null;
  }>;
  clusters: Array<{
    label: string;
    incorrectCount: number;
    questionIds: string[];
  }>;
  partBreakdown: Array<{
    partCode: ReadingPartCode;
    rawScore: number;
    maxRawScore: number;
    correctCount: number;
    incorrectCount: number;
    unansweredCount: number;
  }>;
  skillBreakdown: Array<{
    label: string;
    correctCount: number;
    incorrectCount: number;
    unansweredCount: number;
    totalCount: number;
  }>;
}

// ── HTTP helper ─────────────────────────────────────────────────────────

function resolveUrl(path: string): string {
  if (path.startsWith('http')) return path;
  const base = env.apiBaseUrl || '';
  return base ? `${base.replace(/\/$/, '')}${path}` : path;
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const token = await ensureFreshAccessToken();
  const headers = new Headers(init?.headers);
  headers.set('Accept', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (init?.body && typeof init.body === 'string' && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
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

// ── Admin: structure ────────────────────────────────────────────────────

export const ensureCanonicalParts = (paperId: string) =>
  api<void>(`/v1/admin/papers/${paperId}/reading/ensure-canonical`, { method: 'POST' });

export const getReadingStructureAdmin = (paperId: string) =>
  api<ReadingStructureAdminDto>(`/v1/admin/papers/${paperId}/reading/structure`);

export const upsertReadingPart = (paperId: string, partCode: ReadingPartCode, body: {
  timeLimitMinutes?: number | null; instructions?: string | null;
}) => api<unknown>(`/v1/admin/papers/${paperId}/reading/parts/${partCode}`, {
  method: 'PUT', body: JSON.stringify(body),
});

export const upsertReadingText = (paperId: string, body: {
  id?: string | null; readingPartId: string; displayOrder: number;
  title: string; source?: string | null; bodyHtml: string;
  wordCount: number; topicTag?: string | null;
}) => api<ReadingTextDto>(`/v1/admin/papers/${paperId}/reading/texts`, {
  method: 'POST', body: JSON.stringify(body),
});

export const removeReadingText = (paperId: string, textId: string) =>
  api<void>(`/v1/admin/papers/${paperId}/reading/texts/${textId}`, { method: 'DELETE' });

export const upsertReadingQuestion = (paperId: string, body: {
  id?: string | null; readingPartId: string; readingTextId?: string | null;
  displayOrder: number; points: number;
  questionType: ReadingQuestionType;
  stem: string; optionsJson: string; correctAnswerJson: string;
  acceptedSynonymsJson?: string | null; caseSensitive: boolean;
  explanationMarkdown?: string | null; skillTag?: string | null;
}) => api<ReadingQuestionAdminDto>(`/v1/admin/papers/${paperId}/reading/questions`, {
  method: 'POST', body: JSON.stringify(body),
});

export const removeReadingQuestion = (paperId: string, questionId: string) =>
  api<void>(`/v1/admin/papers/${paperId}/reading/questions/${questionId}`, { method: 'DELETE' });

export const reorderReadingTexts = (paperId: string, partId: string, orderedIds: string[]) =>
  api<void>(`/v1/admin/papers/${paperId}/reading/parts/${partId}/reorder-texts`, {
    method: 'POST', body: JSON.stringify({ orderedIds }),
  });

export const reorderReadingQuestions = (paperId: string, partId: string, orderedIds: string[]) =>
  api<void>(`/v1/admin/papers/${paperId}/reading/parts/${partId}/reorder-questions`, {
    method: 'POST', body: JSON.stringify({ orderedIds }),
  });

export const validateReadingPaper = (paperId: string) =>
  api<ReadingValidationReport>(`/v1/admin/papers/${paperId}/reading/validate`);

// ── Admin: policy ──────────────────────────────────────────────────────

export const getReadingPolicy = () => api<ReadingPolicyDto>('/v1/admin/reading-policy');
export const updateReadingPolicy = (body: ReadingPolicyDto) =>
  api<ReadingPolicyDto>('/v1/admin/reading-policy', { method: 'PUT', body: JSON.stringify(body) });

// ── Learner ─────────────────────────────────────────────────────────────

export const getReadingHome = () => api<ReadingHomeDto>('/v1/reading/home');

export const getReadingStructureLearner = (paperId: string) =>
  api<ReadingLearnerStructureDto>(`/v1/reading-papers/papers/${paperId}/structure`);

export const startReadingAttempt = (paperId: string) =>
  api<ReadingAttemptStarted>(`/v1/reading-papers/papers/${paperId}/attempts`, { method: 'POST' });

export const saveReadingAnswer = (attemptId: string, questionId: string, userAnswerJson: string) =>
  api<void>(`/v1/reading-papers/attempts/${attemptId}/answers/${questionId}`, {
    method: 'PUT', body: JSON.stringify({ userAnswerJson }),
  });

export const submitReadingAttempt = (attemptId: string) =>
  api<ReadingAttemptGraded>(`/v1/reading-papers/attempts/${attemptId}/submit`, { method: 'POST' });

export const getReadingAttempt = (attemptId: string) =>
  api<{
    id: string; paperId: string; status: ReadingAttemptStatus;
    startedAt: string; deadlineAt: string | null; submittedAt: string | null;
    rawScore: number | null; scaledScore: number | null; maxRawScore: number;
    partADeadlineAt: string; partBCDeadlineAt: string;
    answeredCount: number; totalQuestions: number; canResume: boolean;
    answers: Array<{
      readingQuestionId: string; userAnswerJson: string;
      isCorrect: boolean | null; pointsEarned: number; answeredAt: string;
    }>;
    showExplanations: boolean;
  }>(`/v1/reading-papers/attempts/${attemptId}`);

export const getReadingAttemptReview = (attemptId: string) =>
  api<ReadingAttemptReviewDto>(`/v1/reading-papers/attempts/${attemptId}/review`);
