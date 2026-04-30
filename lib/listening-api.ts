import { ensureFreshAccessToken } from './auth-client';
import { env } from './env';
import { fetchWithTimeout } from './network/fetch-with-timeout';

type HttpError = Error & { status?: number; detail?: unknown };

export interface ListeningHomePaperDto {
  id: string;
  title: string;
  slug: string;
  difficulty: string;
  estimatedDurationMinutes: number;
  publishedAt: string | null;
  route: string;
  sourceKind: 'content_paper';
  objectiveReady: boolean;
  questionCount: number;
  assetReadiness: {
    audio: boolean;
    questionPaper: boolean;
    answerKey: boolean;
    audioScript: boolean;
  };
  lastAttempt: {
    attemptId: string;
    status: string;
    startedAt: string;
    submittedAt: string | null;
    route: string;
  } | null;
}

export interface ListeningHomeTaskDto {
  id: string;
  contentId: string;
  title: string;
  difficulty: string;
  estimatedDurationMinutes: number;
  scenarioType?: string | null;
  route: string;
  sourceKind: 'legacy_content_item';
  objectiveReady: boolean;
  questionCount: number;
}

export interface ListeningHomeAttemptDto {
  attemptId: string;
  paperId: string;
  paperTitle: string;
  status: string;
  mode: 'practice' | 'exam';
  startedAt: string;
  lastClientSyncAt: string | null;
  answeredCount: number;
  route: string;
}

export interface ListeningHomeResultDto {
  attemptId: string;
  paperId: string;
  paperTitle: string;
  rawScore: number;
  maxRawScore: number;
  scaledScore: number;
  grade: string;
  passed: boolean;
  submittedAt: string | null;
  scoreDisplay: string;
  route: string;
}

export interface ListeningDrillDto {
  drillId: string;
  title: string;
  focusLabel: string;
  description: string;
  errorType: string;
  estimatedMinutes: number;
  highlights: string[];
  launchRoute: string;
  reviewRoute: string;
}

export interface ListeningHomeDto {
  intro: string;
  papers: ListeningHomePaperDto[];
  featuredTasks: ListeningHomeTaskDto[];
  activeAttempts: ListeningHomeAttemptDto[];
  recentResults: ListeningHomeResultDto[];
  partCollections: Array<{ id: string; title: string; description: string; available: boolean; route: string | null }>;
  transcriptBackedReview: {
    title: string;
    route: string | null;
    availableAfterAttempt: boolean;
    latestAttemptId: string | null;
    latestScoreDisplay: string | null;
  };
  distractorDrills: ListeningDrillDto[];
  drillGroups: ListeningDrillDto[];
  accessPolicyHints: {
    policy: string;
    state: 'deferred' | 'available' | 'partial' | 'restricted';
    rationale: string;
    availableAfterAttempt: boolean;
  };
  mockSets: Array<{ id: string; title: string; route?: string; mode: 'practice' | 'exam'; strictTimer: boolean }>;
  emptyStates: {
    papers: string | null;
    activeAttempts: string | null;
    recentResults: string | null;
  };
}

export interface ListeningSessionQuestionDto {
  id: string;
  number: number;
  partCode: string;
  text: string;
  type: string;
  options: string[];
  points: number;
}

/** Phase 5 tail — speaker descriptor inside an extract metadata row. */
export interface ListeningSpeakerDto {
  id: string;
  role: string;
  gender?: 'm' | 'f' | 'nb' | null;
  accent?: string | null;
}

/** Phase 5 tail — paper-level extract metadata. One row per extract. */
export interface ListeningExtractMetadataDto {
  partCode: 'A1' | 'A2' | 'B' | 'C1' | 'C2';
  displayOrder: number;
  kind: 'consultation' | 'workplace' | 'presentation';
  title: string;
  accentCode: string | null;
  speakers: ListeningSpeakerDto[];
  audioStartMs: number | null;
  audioEndMs: number | null;
}

export interface ListeningSessionDto {
  paper: {
    id: string;
    sourceKind: string;
    title: string;
    slug: string;
    difficulty: string;
    estimatedDurationMinutes: number;
    scenarioType: string;
    audioUrl: string | null;
    questionPaperUrl: string | null;
    audioAvailable: boolean;
    audioUnavailableReason: string | null;
    assetReadiness: {
      audio: boolean;
      questionPaper: boolean;
      answerKey: boolean;
      audioScript: boolean;
    };
    transcriptPolicy: string;
    /** Phase 5 tail — paper-level extract metadata. One row per extract:
     * A1, A2, B (one per workplace clip), C1, C2. Empty list when not
     * yet authored. */
    extracts?: ListeningExtractMetadataDto[];
  };
  attempt: ListeningAttemptDto | null;
  questions: ListeningSessionQuestionDto[];
  modePolicy: {
    mode: 'practice' | 'exam' | 'home' | 'paper';
    canPause: boolean;
    canScrub: boolean;
    onePlayOnly: boolean;
    autosave: boolean;
    transcriptPolicy: string;
    /** Phase 9 tail — UI hint. Server is the source of truth for integrity
     * invariants (onePlayOnly / canScrub / canPause). */
    presentationStyle?: 'practice' | 'exam_standard' | 'kiosk_fullscreen' | 'printable_booklet';
    /** OET@Home kiosk: full-screen + integrity prompt before audio plays. */
    integrityLockRequired?: boolean;
    /** Paper-simulation: render a printable booklet alongside the player. */
    printableBooklet?: boolean;
  };
  scoring: {
    maxRawScore: number;
    passRawScore: number;
    passScaledScore: number;
  };
  readiness: {
    objectiveReady: boolean;
    questionCount: number;
    audioAvailable: boolean;
    missingReason: string | null;
  };
}

export interface ListeningAttemptDto {
  attemptId: string;
  paperId: string;
  state: string;
  mode: 'practice' | 'exam';
  startedAt: string;
  submittedAt: string | null;
  completedAt: string | null;
  elapsedSeconds: number;
  lastClientSyncAt: string | null;
  answers: Record<string, string | null>;
}

export interface ListeningReviewItemDto {
  questionId: string;
  number: number;
  partCode: string;
  prompt: string;
  type: string;
  learnerAnswer: string;
  correctAnswer: string;
  isCorrect: boolean;
  pointsEarned: number;
  maxPoints: number;
  explanation: string;
  errorType: string | null;
  options: string[];
  transcript: {
    allowed: boolean;
    excerpt: string | null;
    distractorExplanation: string | null;
  } | null;
  distractorExplanation: string | null;
}

export interface ListeningReviewDto {
  evaluationId: string | null;
  attemptId: string;
  paper: ListeningSessionDto['paper'];
  rawScore: number;
  maxRawScore: number;
  scaledScore: number;
  grade: string;
  passed: boolean;
  scoreDisplay: string;
  correctCount: number;
  incorrectCount: number;
  unansweredCount: number;
  itemReview: ListeningReviewItemDto[];
  errorClusters: Array<{ errorType: string; label: string; count: number; affectedQuestionIds: string[] }>;
  recommendedNextDrill: ListeningDrillDto;
  transcriptAccess: {
    policy: string;
    state: 'restricted' | 'partial' | 'available';
    allowedQuestionIds: string[];
    reason: string;
  };
  strengths: string[];
  issues: string[];
  generatedAt: string | null;
}

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

  const response = await fetchWithTimeout(resolveUrl(path), { ...init, headers });
  if (!response.ok) {
    let detail: unknown = null;
    try {
      detail = await response.json();
    } catch {
      detail = null;
    }
    const message = typeof detail === 'object' && detail && 'message' in detail
      ? String((detail as { message?: unknown }).message)
      : `HTTP ${response.status}`;
    const error = new Error(message) as HttpError;
    error.status = response.status;
    error.detail = detail;
    throw error;
  }

  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

export const getListeningHome = () =>
  api<ListeningHomeDto>('/v1/listening/home');

export type ListeningSessionMode = 'practice' | 'exam' | 'home' | 'paper';

export function getListeningSession(
  paperId: string,
  options: { mode?: ListeningSessionMode; attemptId?: string | null } = {},
) {
  const params = new URLSearchParams();
  if (options.mode) params.set('mode', options.mode);
  if (options.attemptId) params.set('attemptId', options.attemptId);
  const suffix = params.toString() ? `?${params.toString()}` : '';
  return api<ListeningSessionDto>(`/v1/listening-papers/papers/${encodeURIComponent(paperId)}/session${suffix}`);
}

export const startListeningAttempt = (paperId: string, mode: ListeningSessionMode) =>
  api<ListeningAttemptDto>(`/v1/listening-papers/papers/${encodeURIComponent(paperId)}/attempts`, {
    method: 'POST',
    body: JSON.stringify({ mode }),
  });

export const saveListeningAnswer = (attemptId: string, questionId: string, userAnswer: string) =>
  api<void>(`/v1/listening-papers/attempts/${encodeURIComponent(attemptId)}/answers/${encodeURIComponent(questionId)}`, {
    method: 'PUT',
    body: JSON.stringify({ userAnswer }),
  });

export const heartbeatListeningAttempt = (attemptId: string, elapsedSeconds: number, deviceType = 'web') =>
  api<{ attemptId: string; elapsedSeconds: number; lastClientSyncAt: string }>(
    `/v1/listening-papers/attempts/${encodeURIComponent(attemptId)}/heartbeat`,
    {
      method: 'PATCH',
      body: JSON.stringify({ elapsedSeconds, deviceType }),
    },
  );

export const submitListeningAttempt = (attemptId: string) =>
  api<ListeningReviewDto>(`/v1/listening-papers/attempts/${encodeURIComponent(attemptId)}/submit`, {
    method: 'POST',
  });

export const getListeningResult = (attemptId: string) =>
  api<ListeningReviewDto>(`/v1/listening-papers/attempts/${encodeURIComponent(attemptId)}/review`);

export const getListeningReview = getListeningResult;

export function getListeningDrill(drillId: string, options: { paperId?: string; attemptId?: string } = {}) {
  const params = new URLSearchParams();
  if (options.paperId) params.set('paperId', options.paperId);
  if (options.attemptId) params.set('attemptId', options.attemptId);
  const suffix = params.toString() ? `?${params.toString()}` : '';
  return api<ListeningDrillDto>(`/v1/listening-papers/drills/${encodeURIComponent(drillId)}${suffix}`);
}
