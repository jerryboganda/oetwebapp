import { apiClient } from './api';

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
  /**
   * True when the current learner's subscription does not grant access to this
   * paper. Surfaced by the home endpoint so the UI can render a Premium lock
   * badge instead of letting the learner click through to a 402.
   */
  requiresSubscription?: boolean;
  /**
   * Backend B4 work — coarse access tier projected by
   * `IContentEntitlementService` so the home grid can render a tier-aware
   * affordance (Free / Preview / Premium) without re-querying entitlement.
   * `requiresSubscription` remains the authoritative gate; `accessTier` only
   * influences the lock badge visual.
   */
  accessTier?: 'free' | 'preview' | 'premium';
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
    mode?: ListeningSessionMode;
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
  mode: ListeningSessionMode;
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

/**
 * Per-sub-section part code emitted by the backend session DTO. The exam
 * player treats every Part B extract as a distinct navigable sub-section, so
 * this is the full ordered set A1, A2, B1..B6, C1, C2 (not the legacy rolled-up
 * "B"). The bare "B" alias is retained for forward-compat with any not-yet-split
 * paper the backend still floors to "B1".
 */
export type ListeningExtractPartCode =
  | 'A1' | 'A2'
  | 'B' | 'B1' | 'B2' | 'B3' | 'B4' | 'B5' | 'B6'
  | 'C1' | 'C2';

/** Phase 5 tail — paper-level extract metadata. One row per extract. */
export interface ListeningExtractMetadataDto {
  partCode: ListeningExtractPartCode;
  displayOrder: number;
  kind: 'consultation' | 'workplace' | 'presentation';
  title: string;
  accentCode: string | null;
  speakers: ListeningSpeakerDto[];
  audioStartMs: number | null;
  audioEndMs: number | null;
  /**
   * Per-sub-section audio URL the player loads for this section. The backend
   * resolves it uploaded-asset-first (an authenticated `/v1/media/{id}/content`
   * URL) with a TTS fallback (an anonymous `/v1/listening/audio/{sha}.wav`
   * URL); null when neither exists. The exam player blob-fetches `/v1/media`
   * URLs (Bearer auth) and uses a plain `<audio src>` for the TTS WAV.
   */
  audioUrl?: string | null;
  /** Per-sub-section countdown (seconds). Null → the player applies a default. */
  timeLimitSeconds?: number | null;
  /**
   * Part A note-completion body (markdown-ish grammar defined in
   * `lib/listening-part-a-notes.ts`). Null / absent for Part B/C extracts or
   * when the body has not yet been authored. The player and printable booklet
   * render this via `parseNotesDocument` when present.
   */
  notesBody?: string | null;
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
    /** Per-part learner-facing QuestionPaper PDF URLs, keyed by uppercased
     * part/section code (A | B | C and overrides A1/A2 | B1..B6 | C1/C2). The
     * player resolves the current section to a URL (exact code → parent part
     * fallback). Empty object when no per-part QuestionPaper assets exist. */
    questionPaperUrlByPart?: Record<string, string>;
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
    /** R07/R06 policy hint: paper/diagnostic modes may navigate across sections. */
    freeNavigation?: boolean;
    /** R06.11 policy hint: show exact unanswered numbers before lock/submit. */
    unansweredWarningRequired?: boolean;
    /** Paper mode final all-parts review window in seconds, when configured. */
    finalReviewAllPartsSeconds?: number | null;
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
  mode: ListeningSessionMode;
  startedAt: string;
  submittedAt: string | null;
  completedAt: string | null;
  elapsedSeconds: number;
  lastClientSyncAt: string | null;
  answers: Record<string, string | null>;
  /**
   * Server-authoritative deadline for this attempt (ISO-8601). Drives the
   * 40-minute whole-attempt countdown in the player chrome and the
   * exam/home auto-submit on expiry. Optional for back-compat with legacy
   * sessions that have not yet been re-projected.
   */
  expiresAt?: string | null;
}

/**
 * Listening Part A miss classification surfaced on the review page. Matches
 * the backend `ListeningMissReason` enum populated by the grader when an
 * answer fails to match canonical + accepted variants. Null for MCQ items
 * and for legacy attempts graded before the column existed.
 */
export type ListeningMissReason =
  | 'Match'
  | 'Empty'
  | 'SpellingError'
  | 'WrongNumber'
  | 'ExtraInfo'
  | 'WrongSection'
  | 'Paraphrase'
  | 'Other';

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
  /**
   * Structured miss classification from the relational grader. Drives the
   * "Missed because…" chip on the review page. Coexists with the legacy
   * free-form `errorType` (which is computed from the JSON-pathway DTOs).
   */
  missReason?: ListeningMissReason | null;
  options: string[];
  transcript: {
    allowed: boolean;
    excerpt: string | null;
    distractorExplanation: string | null;
  } | null;
  distractorExplanation: string | null;
  optionAnalysis?: Array<{
    optionLabel: string;
    optionText: string;
    isCorrect: boolean;
    distractorCategory: string | null;
    whyMarkdown: string | null;
  }> | null;
  speakerAttitude?: string | null;
  transcriptEvidenceStartMs?: number | null;
  transcriptEvidenceEndMs?: number | null;
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
  transcriptSegments: Array<{
    startMs: number;
    endMs: number;
    partCode: string | null;
    speakerId: string | null;
    text: string;
  }>;
  strengths: string[];
  issues: string[];
  generatedAt: string | null;
}

// Delegates to the shared API client (lib/api.ts) so every listening call
// inherits auth (Bearer), the CSRF header (required by the /api/backend proxy
// on non-safe methods — listening autosave PUTs and section-transition
// heartbeats 403 without it), credentials, timeout, retry-on-5xx/408/429, and a
// normalized `ApiError` (carries status + code + retryable, and surfaces the
// backend's own prose as the message instead of a bare `HTTP <status>`).
// Call sites keep passing `JSON.stringify(...)` string bodies, forwarded
// verbatim with a JSON Content-Type.
async function api<T>(path: string, init?: RequestInit): Promise<T> {
  return apiClient.request<T>(path, init);
}

export const getListeningHome = () =>
  api<ListeningHomeDto>('/v1/listening/home');

// Public OET Listening test-rules constants. Anonymous-allowed on the backend
// — the /listening/test-rules page sources its 42-q / 40-min / 30-pass / 350-
// scaled-pass numbers from here so future spec changes don't require a code
// deploy. Page falls back to its static copy if this fetch fails.
export interface ListeningTestRulesPolicyDto {
  questionCount: number;
  durationMinutes: number;
  partA: { items: number; extracts: number; itemType: string };
  partB: { items: number; extracts: number; itemType: string };
  partC: { items: number; extracts: number; itemType: string };
  passRawAnchor: number;
  passScaledAnchor: number;
  scaledMax: number;
}

export const getListeningTestRulesPolicy = () =>
  api<ListeningTestRulesPolicyDto>('/v1/listening-papers/policy/test-rules');

export type ListeningSessionMode = 'practice' | 'exam' | 'home' | 'paper' | 'diagnostic';

export function getListeningSession(
  paperId: string,
  options: { mode?: ListeningSessionMode; attemptId?: string | null; pathwayStage?: string | null } = {},
) {
  const params = new URLSearchParams();
  if (options.mode) params.set('mode', options.mode);
  if (options.attemptId) params.set('attemptId', options.attemptId);
  if (options.pathwayStage) params.set('pathwayStage', options.pathwayStage);
  const suffix = params.toString() ? `?${params.toString()}` : '';
  return api<ListeningSessionDto>(`/v1/listening-papers/papers/${encodeURIComponent(paperId)}/session${suffix}`);
}

export const startListeningAttempt = (paperId: string, mode: ListeningSessionMode, options: {
  pathwayStage?: string | null;
  mockAttemptId?: string | null;
  mockSectionId?: string | null;
} = {}) =>
  api<ListeningAttemptDto>(`/v1/listening-papers/papers/${encodeURIComponent(paperId)}/attempts`, {
    method: 'POST',
    body: JSON.stringify({
      mode,
      pathwayStage: options.pathwayStage ?? undefined,
      mockAttemptId: options.mockAttemptId ?? undefined,
      mockSectionId: options.mockSectionId ?? undefined,
    }),
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

export interface ListeningAdvanceSectionResult {
  attemptId: string;
  sectionCursor: number;
  lastClientSyncAt: string;
}

/**
 * One-way section navigation. Stores a monotonically non-decreasing
 * `sectionCursor` on the relational attempt; the server rejects any request
 * that would move it backwards (`listening_section_one_way`). The exam player
 * calls this on every forward advance (Next or timer auto-advance). `toIndex`
 * is the zero-based index of the sub-section being entered in the ordered
 * A1→A2→B1..B6→C1→C2 sequence.
 */
export const advanceListeningSection = (attemptId: string, toIndex: number) =>
  api<ListeningAdvanceSectionResult>(
    `/v1/listening-papers/attempts/${encodeURIComponent(attemptId)}/advance-section`,
    {
      method: 'POST',
      body: JSON.stringify({ sectionCursor: toIndex }),
    },
  );

/**
 * Listening attempt / integrity event-type union (spec §17.11). The first
 * group are the OET@Home integrity-lock events recorded only when
 * `modePolicy.integrityLockRequired` is set (window focus/blur, fullscreen,
 * blocked audio gestures). The second group are the §17.11 attempt-event
 * stream, recorded for any graded attempt — audio lifecycle, reading-time
 * windows, answer changes, annotations, and the timer auto-submit.
 *
 * The string is left open (`| (string & {})`) so callers can still pass an
 * ad-hoc event type during incremental rollout without a type error; the
 * server clamps unknown types to a length-limited passthrough.
 */
export type ListeningIntegrityEventType =
  // OET@Home integrity-lock events (existing semantics — unchanged).
  | 'fullscreen_enter'
  | 'fullscreen_exit'
  | 'fullscreen_request_failed'
  | 'page_hidden'
  | 'page_visible'
  | 'window_blur'
  | 'window_focus'
  | 'audio_seek_blocked'
  | 'audio_pause_blocked'
  | 'audio_replay_blocked'
  // §17.11 attempt-event stream (recorded for any graded attempt).
  | 'audio_started'
  | 'audio_ended'
  | 'audio_error'
  | 'reading_time_started'
  | 'reading_time_ended'
  | 'answer_changed'
  | 'highlight'
  | 'strikethrough'
  | 'auto_submit'
  | (string & {});

export const recordListeningIntegrityEvent = (
  attemptId: string,
  eventType: ListeningIntegrityEventType,
  details?: string,
  occurredAt = new Date().toISOString(),
) =>
  api<void>(`/v1/listening-papers/attempts/${encodeURIComponent(attemptId)}/integrity-events`, {
    method: 'POST',
    body: JSON.stringify({ eventType, details, occurredAt }),
  });

export const submitListeningAttempt = (attemptId: string, answers?: Record<string, string | null>) =>
  api<ListeningReviewDto>(`/v1/listening-papers/attempts/${encodeURIComponent(attemptId)}/submit`, {
    method: 'POST',
    body: JSON.stringify({ answers: answers ?? {} }),
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
