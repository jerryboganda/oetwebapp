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

export type ReadingSectionCode = 'B1' | 'B2' | 'B3' | 'B4' | 'B5' | 'B6' | 'C1' | 'C2';

export type ReadingQuestionType =
  | 'MatchingTextReference'
  | 'ShortAnswer'
  | 'SentenceCompletion'
  | 'MultipleChoice3'
  | 'MultipleChoice4'
  | 'FillInBlank'
  | 'ShortAnswerLabeled'
  | 'MultipleChoiceFlexible';

export type ReadingReviewState =
  | 'Draft'
  | 'AcademicReview'
  | 'MedicalReview'
  | 'LanguageReview'
  | 'Pilot'
  | 'Published'
  | 'Retired';

export type ReadingDistractorCategory =
  | 'Opposite'
  | 'TooBroad'
  | 'TooSpecific'
  | 'WrongSpeaker'
  | 'NotInText'
  | 'DistortedDetail'
  | 'OutOfScope';

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
  readingSectionId: string | null;
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
  optionDistractorsJson?: string | null;
  reviewState?: ReadingReviewState;
  latestReviewNote?: string | null;
  /** Authoring difficulty hint, 1 (easiest) – 5 (hardest). */
  difficulty?: number | null;
  /** Verbatim evidence sentence supporting the correct answer. */
  evidenceSentence?: string | null;
  /** Source paragraph order, drives the R07.6 paragraph-order lint. */
  paragraphIndex?: number | null;
  /** Per-option rationale map, serialised as JSON on the entity. */
  distractorRationaleJson?: string | null;
  /** Per-box explanation map for ShortAnswerLabeled; null for all other types. */
  boxExplanationsJson?: string | null;
}

export interface ReadingReviewLogEntryDto {
  id: string;
  fromState: ReadingReviewState;
  toState: ReadingReviewState;
  reviewerUserId: string;
  reviewerDisplayName: string | null;
  note: string | null;
  transitionedAt: string;
}

export interface ReadingReviewTransitionResultDto {
  questionId: string;
  fromState: ReadingReviewState;
  toState: ReadingReviewState;
  transitionedAt: string;
}

export interface ReadingQuestionLearnerDto {
  id: string;
  readingSectionId: string | null;
  readingTextId: string | null;
  displayOrder: number;
  points: number;
  questionType: ReadingQuestionType;
  stem: string;
  options: unknown; // already-parsed JSON from the backend
}

export interface ReadingSectionAdminDto {
  id: string;
  sectionCode: ReadingSectionCode;
  displayOrder: number;
  maxRawScore: number;
  contentPaperAssetId: string | null;
  questions: ReadingQuestionAdminDto[];
}

export interface ReadingSectionLearnerDto {
  id: string;
  sectionCode: ReadingSectionCode;
  displayOrder: number;
  maxRawScore: number;
  contentPaperAssetId: string | null;
  questions: ReadingQuestionLearnerDto[];
}

export interface ReadingPartAdminDto {
  id: string;
  partCode: ReadingPartCode;
  timeLimitMinutes: number;
  maxRawScore: number;
  instructions: string | null;
  texts: ReadingTextDto[];
  questions: ReadingQuestionAdminDto[];
  sections?: ReadingSectionAdminDto[];
}

export interface ReadingStructureAdminDto {
  paperId: string;
  parts: ReadingPartAdminDto[];
}

export interface ReadingStructureManifestDto {
  parts: Array<{
    partCode: ReadingPartCode;
    timeLimitMinutes: number | null;
    instructions: string | null;
    texts: Array<{
      displayOrder: number;
      title: string;
      source: string | null;
      bodyHtml: string;
      wordCount: number;
      topicTag: string | null;
    }>;
    questions: Array<{
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
      readingTextDisplayOrder: number | null;
      optionDistractorsJson?: string | null;
      reviewState?: ReadingReviewState;
    }>;
  }>;
}

export interface ReadingStructureImportResultDto {
  structure: ReadingStructureAdminDto;
  report: ReadingValidationReport;
}

export interface ReadingPaperCloneRequestDto {
  title?: string | null;
  slug?: string | null;
  resetReviewState?: boolean;
}

export interface ReadingPaperCloneResultDto {
  sourcePaperId: string;
  paperId: string;
  title: string;
  slug: string;
  adminRoute: string;
  structure: ReadingStructureAdminDto;
}

export type ReadingExtractionStatus = 'Pending' | 'Approved' | 'Rejected' | 'Failed';

export interface ReadingExtractionDraftDto {
  id: string;
  paperId: string;
  mediaAssetId: string | null;
  status: ReadingExtractionStatus;
  manifest: ReadingStructureManifestDto | null;
  rawAiResponseJson: string | null;
  isStub: boolean;
  notes: string | null;
  createdByAdminId: string;
  resolvedByAdminId: string | null;
  createdAt: string;
  resolvedAt: string | null;
}

export interface ReadingValidationReport {
  isPublishReady: boolean;
  issues: Array<{ code: string; severity: 'error' | 'warning'; message: string; targetId: string | null }>;
  counts: { partACount: number; partBCount: number; partCCount: number; totalPoints: number };
}

/**
 * Mirrors `ReadingResolvedPolicy` in
 * `backend/src/OetLearner.Api/Services/Reading/ReadingPolicyService.cs:31`.
 *
 * Surfaced 1:1 by the backend on `POST /v1/reading-papers/papers/{id}/attempts`
 * and on the resume endpoint so the player can render the timer, accessibility
 * panel, and rate-limit hints from the same snapshot the server graded with.
 * Adding this type closes API-contract drift P0-L (May 2026 audit closure).
 */
export interface ReadingResolvedPolicy {
  attemptsPerPaperPerUser: number;
  attemptCooldownMinutes: number;
  partATimerStrictness: string;
  partATimerMinutes: number;
  partBCTimerMinutes: number;
  gracePeriodSeconds: number;
  onExpirySubmitPolicy: string;
  countdownWarnings: number[];
  enabledQuestionTypes: string[];
  shortAnswerNormalisation: string;
  shortAnswerAcceptSynonyms: boolean;
  matchingAllowPartialCredit: boolean;
  unknownTypeFallbackPolicy: string;
  showExplanationsAfterSubmit: boolean;
  showExplanationsOnlyIfWrong: boolean;
  showCorrectAnswerOnReview: boolean;
  submitRateLimitPerMinute: number;
  autosaveRateLimitPerMinute: number;
  extraTimeEntitlementPct: number;
  allowMultipleConcurrentAttempts: boolean;
  allowPausingAttempt: boolean;
  allowResumeAfterExpiry: boolean;
  allowPaperReadingMode: boolean;
  fontScaleUserControl: boolean;
  highContrastMode: boolean;
  screenReaderOptimised: boolean;
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
  partABreakAvailable: boolean;
  partABreakResumed: boolean;
  partBCTimerPausedAt: string | null;
  partBCPausedSeconds: number;
  partABreakMaxSeconds: number;
  /** Policy snapshot the backend captured at attempt-start. Drift fix
   *  P0-L 2026-05: previously omitted from this DTO, so the player ignored
   *  the per-user accessibility / rate-limit hints the server already
   *  resolved. Always present in fresh attempts. */
  policy?: ReadingResolvedPolicy;
}

export interface ReadingAttemptBreakState {
  attemptId: string;
  deadlineAt: string;
  partADeadlineAt: string;
  partBCDeadlineAt: string;
  partABreakAvailable: boolean;
  partABreakResumed: boolean;
  partBCTimerPausedAt: string | null;
  partBCPausedSeconds: number;
  partABreakMaxSeconds: number;
}

export interface ReadingAttemptGraded {
  attemptId?: string;
  rawScore: number;
  maxRawScore: number;
  scaledScore: number | null;
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
  entitlement?: {
    allowed: boolean;
    reason: string;
    currentTier: string | null;
    requiredScope: string | null;
  };
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
  partABreakAvailable?: boolean;
  partABreakResumed?: boolean;
  partBCTimerPausedAt?: string | null;
  partBCPausedSeconds?: number;
  partABreakMaxSeconds?: number;
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
  scaledScore: number | null;
  gradeLetter: string;
  submittedAt: string | null;
  route: string;
}

export interface ReadingHomeSafeDrillDto {
  id: string;
  title: string;
  description: string;
  focusLabel: string;
  estimatedMinutes: number;
  launchRoute: string;
  highlights: string[];
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
    allowPaperReadingMode: boolean;
  };
  safeDrills: ReadingHomeSafeDrillDto[];
}

export interface ReadingLearnerStructureDto {
  paper: {
    id: string;
    title: string;
    slug: string;
    subtestCode: string;
    allowPaperReadingMode?: boolean;
    questionPaperAssets?: Array<{
      id: string;
      part: string | null;
      title: string;
      downloadPath: string;
    }>;
    /**
     * Phase 5 closure — accessibility opt-ins from the resolved Reading
     * policy. Each flag gates whether the player exposes the matching
     * toggle in its settings panel.
     */
    policy?: {
      fontScaleUserControl: boolean;
      highContrastMode: boolean;
      screenReaderOptimised: boolean;
    };
  };
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
    sections?: ReadingSectionLearnerDto[];
  }>;
}

export type ReadingPaperAnnotationKind = 'Text' | 'Rectangle' | 'Freehand';

export interface ReadingPaperAnnotationDto {
  id: string;
  paperId: string;
  contentPaperAssetId: string;
  pageNumber: number;
  kind: ReadingPaperAnnotationKind;
  geometry: unknown;
  createdAt: string;
  updatedAt: string;
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
  normalizeSmartQuotes: boolean;
  normalizeHyphenSpacing: boolean;
  normalizeUnitSpacing: boolean;
  partACaseInsensitive: boolean;
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
    mode: 'Exam' | 'Learning' | 'Drill' | 'MiniTest' | 'ErrorBank';
    scopeQuestionIds: string[] | null;
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
  }>;
  clusters: Array<{
    label: string;
    incorrectCount: number;
    questionIds: string[];
    questions: Array<{
      partCode: ReadingPartCode;
      displayOrder: number;
      label: string;
    }>;
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

export interface ReadingAdminAnalyticsDto {
  generatedAt: string;
  windowDays: number;
  summary: {
    totalPapers: number;
    publishedPapers: number;
    examReadyPapers: number;
    authoredQuestions: number;
    totalAttempts: number;
    submittedAttempts: number;
    activeAttempts: number;
    averageRawScore: number | null;
    averageScaledScore: number | null;
    passRatePercent: number | null;
    unansweredRatePercent: number | null;
  };
  papers: Array<{
    paperId: string;
    title: string;
    status: string;
    difficulty: string;
    questionCount: number;
    totalPoints: number;
    partACount: number;
    partBCount: number;
    partCCount: number;
    isCanonicalShapeComplete: boolean;
    isExamReady: boolean;
    attemptCount: number;
    submittedCount: number;
    averageRawScore: number | null;
    averageScaledScore: number | null;
    passRatePercent: number | null;
    averageCompletionSeconds: number | null;
  }>;
  partBreakdown: Array<{
    partCode: ReadingPartCode;
    questionCount: number;
    opportunities: number;
    correctCount: number;
    unansweredCount: number;
    accuracyPercent: number | null;
  }>;
  skillBreakdown: Array<{
    label: string;
    questionCount: number;
    opportunities: number;
    correctCount: number;
    unansweredCount: number;
    accuracyPercent: number | null;
  }>;
  hardestQuestions: Array<{
    paperId: string;
    paperTitle: string;
    questionId: string;
    partCode: ReadingPartCode | '?';
    label: string;
    displayOrder: number;
    questionType: string;
    skillTag: string;
    stem: string;
    opportunities: number;
    answeredCount: number;
    correctCount: number;
    unansweredCount: number;
    accuracyPercent: number | null;
  }>;
  modeBreakdown: Array<{
    mode: string;
    attemptCount: number;
    submittedCount: number;
    averageRawScore: number | null;
    averageScaledScore: number | null;
    passRatePercent: number | null;
  }>;
  actionInsights: Array<{
    id: string;
    title: string;
    description: string;
    tone: 'success' | 'warning' | 'danger' | string;
  }>;
  /**
   * Phase 2 closure — distractor traps. One row per (question, option,
   * category) where learners picked a wrong answer carrying authored
   * distractor metadata. Top 50 by selection count. Drives the
   * "Distractor Traps" panel on /admin/analytics/reading.
   */
  distractorTraps: Array<{
    questionId: string;
    paperId: string;
    paperTitle: string;
    partCode: ReadingPartCode | '?';
    stem: string;
    optionKey: string;
    category: string;
    selectedCount: number;
    opportunities: number;
    selectionRatePercent: number | null;
  }>;
}

// ── HTTP helper ─────────────────────────────────────────────────────────

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

// ── Admin: structure ────────────────────────────────────────────────────

export const ensureCanonicalParts = (paperId: string) =>
  api<void>(`/v1/admin/papers/${paperId}/reading/ensure-canonical`, { method: 'POST' });

export const getReadingStructureAdmin = (paperId: string) =>
  api<ReadingStructureAdminDto>(`/v1/admin/papers/${paperId}/reading/structure`);

export const getReadingStructureAdminPreview = (paperId: string) =>
  api<ReadingLearnerStructureDto>(`/v1/admin/papers/${paperId}/reading/preview-structure`);

export const exportReadingStructureManifest = (paperId: string) =>
  api<ReadingStructureManifestDto>(`/v1/admin/papers/${paperId}/reading/manifest`);

export const importReadingStructureManifest = (
  paperId: string,
  body: { replaceExisting: boolean; manifest: ReadingStructureManifestDto },
) => api<ReadingStructureImportResultDto>(`/v1/admin/papers/${paperId}/reading/manifest`, {
  method: 'POST', body: JSON.stringify(body),
});

export const cloneReadingPaper = (
  paperId: string,
  body: ReadingPaperCloneRequestDto = {},
) => api<ReadingPaperCloneResultDto>(`/v1/admin/papers/${paperId}/reading/clone`, {
  method: 'POST', body: JSON.stringify(body),
});

type ReadingExtractionDraftWire = Partial<ReadingExtractionDraftDto> & {
  id: string;
  paperId: string;
  status: ReadingExtractionStatus;
  extractedManifestJson?: string | null;
  manifest?: ReadingStructureManifestDto | null;
  isStub: boolean;
  notes?: string | null;
  createdByAdminId: string;
  resolvedByAdminId?: string | null;
  createdAt: string;
  resolvedAt?: string | null;
};

function parseReadingManifest(json: string | null | undefined): ReadingStructureManifestDto | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json);
    return parsed && typeof parsed === 'object' ? (parsed as ReadingStructureManifestDto) : null;
  } catch {
    return null;
  }
}

function normaliseReadingExtractionDraft(wire: ReadingExtractionDraftWire): ReadingExtractionDraftDto {
  return {
    id: wire.id,
    paperId: wire.paperId,
    mediaAssetId: wire.mediaAssetId ?? null,
    status: wire.status,
    manifest: wire.manifest ?? parseReadingManifest(wire.extractedManifestJson),
    rawAiResponseJson: wire.rawAiResponseJson ?? null,
    isStub: wire.isStub,
    notes: wire.notes ?? null,
    createdByAdminId: wire.createdByAdminId,
    resolvedByAdminId: wire.resolvedByAdminId ?? null,
    createdAt: wire.createdAt,
    resolvedAt: wire.resolvedAt ?? null,
  };
}

export async function proposeReadingStructure(
  paperId: string,
  mediaAssetId?: string | null,
): Promise<ReadingExtractionDraftDto> {
  const wire = await api<ReadingExtractionDraftWire>(`/v1/admin/papers/${paperId}/reading/extractions`, {
    method: 'POST',
    body: JSON.stringify({ mediaAssetId: mediaAssetId ?? null }),
  });
  return normaliseReadingExtractionDraft(wire);
}

export async function listReadingExtractionDrafts(
  paperId: string,
  status?: ReadingExtractionStatus,
): Promise<ReadingExtractionDraftDto[]> {
  const wire = await api<ReadingExtractionDraftWire[]>(`/v1/admin/papers/${paperId}/reading/extractions`);
  const drafts = wire.map(normaliseReadingExtractionDraft);
  return status ? drafts.filter((draft) => draft.status === status) : drafts;
}

export async function approveReadingExtractionDraft(
  paperId: string,
  draftId: string,
): Promise<ReadingExtractionDraftDto> {
  const wire = await api<ReadingExtractionDraftWire>(
    `/v1/admin/papers/${paperId}/reading/extractions/${draftId}/approve`,
    { method: 'POST' },
  );
  return normaliseReadingExtractionDraft(wire);
}

export async function rejectReadingExtractionDraft(
  paperId: string,
  draftId: string,
  reason: string,
): Promise<ReadingExtractionDraftDto> {
  const wire = await api<ReadingExtractionDraftWire>(
    `/v1/admin/papers/${paperId}/reading/extractions/${draftId}/reject`,
    { method: 'POST', body: JSON.stringify({ reason }) },
  );
  return normaliseReadingExtractionDraft(wire);
}

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
  readingSectionId?: string | null;
  displayOrder: number; points: number;
  questionType: ReadingQuestionType;
  stem: string; optionsJson: string; correctAnswerJson: string;
  acceptedSynonymsJson?: string | null; caseSensitive: boolean;
  explanationMarkdown?: string | null; skillTag?: string | null;
  difficulty?: number | null;
  evidenceSentence?: string | null;
  paragraphIndex?: number | null;
  /** Per-option rationale map; serialised to the `distractorRationaleJson`
   *  string column the backend stores. */
  distractorRationale?: Record<string, string> | null;
  /** Per-box explanation map for ShortAnswerLabeled; null for all other types. */
  boxExplanationsJson?: string | null;
}) => {
  const { distractorRationale, ...rest } = body;
  const payload = {
    ...rest,
    ...(distractorRationale !== undefined
      ? {
          distractorRationaleJson:
            distractorRationale === null ? null : JSON.stringify(distractorRationale),
        }
      : {}),
  };
  return api<ReadingQuestionAdminDto>(`/v1/admin/papers/${paperId}/reading/questions`, {
    method: 'POST', body: JSON.stringify(payload),
  });
};

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

export const setReadingQuestionDistractors = (
  paperId: string,
  questionId: string,
  distractors: Partial<Record<string, ReadingDistractorCategory>>,
) => api<{ id: string; optionDistractorsJson: string | null }>(
  `/v1/admin/papers/${paperId}/reading/questions/${questionId}/distractors`,
  { method: 'PUT', body: JSON.stringify({ distractors }) },
);

export const getReadingQuestionReviewHistory = (paperId: string, questionId: string) =>
  api<ReadingReviewLogEntryDto[]>(`/v1/admin/papers/${paperId}/reading/questions/${questionId}/review-history`);

export const transitionReadingQuestionReviewState = (
  paperId: string,
  questionId: string,
  body: { toState: ReadingReviewState; note?: string | null; isAdminOverride?: boolean },
) => api<ReadingReviewTransitionResultDto>(
  `/v1/admin/papers/${paperId}/reading/questions/${questionId}/review-transition`,
  { method: 'POST', body: JSON.stringify({ note: null, isAdminOverride: false, ...body }) },
);

export const validateReadingPaper = (paperId: string) =>
  api<ReadingValidationReport>(`/v1/admin/papers/${paperId}/reading/validate`);

export const getReadingAdminAnalytics = (days = 30) =>
  api<ReadingAdminAnalyticsDto>(`/v1/admin/reading/analytics?days=${encodeURIComponent(days)}`);

// ── Admin: policy ──────────────────────────────────────────────────────

export const getReadingPolicy = () => api<ReadingPolicyDto>('/v1/admin/reading-policy');
export const updateReadingPolicy = (body: ReadingPolicyDto) =>
  api<ReadingPolicyDto>('/v1/admin/reading-policy', { method: 'PUT', body: JSON.stringify(body) });

/**
 * Phase 3 closure — per-user Reading policy override. Lets operations
 * grant a single learner extra time (e.g. an accessibility entitlement)
 * or block a learner from starting new attempts, without editing the
 * global policy. Backed by `ReadingUserPolicyOverride` row keyed on
 * userId.
 */
export interface ReadingUserPolicyOverrideDto {
  userId: string;
  extraTimeEntitlementPct: number;
  blockAttempts: boolean;
  reason: string | null;
  grantedByAdminId: string | null;
  createdAt: string;
  updatedAt: string;
  expiresAt: string | null;
}

export const getReadingUserOverride = (userId: string) =>
  api<ReadingUserPolicyOverrideDto | null>(
    `/v1/admin/reading-policy/users/${encodeURIComponent(userId)}`,
  );

export const upsertReadingUserOverride = (
  userId: string,
  body: Omit<ReadingUserPolicyOverrideDto, 'grantedByAdminId' | 'createdAt' | 'updatedAt'>,
) =>
  api<ReadingUserPolicyOverrideDto>(
    `/v1/admin/reading-policy/users/${encodeURIComponent(userId)}`,
    { method: 'PUT', body: JSON.stringify(body) },
  );

// ── Learner ─────────────────────────────────────────────────────────────

export const getReadingHome = () => api<ReadingHomeDto>('/v1/reading-papers/home');

// ── Learner: course pathway snapshot ───────────────────────────────────
//
// Mirrors `ReadingPathwaySnapshot` from
// `backend/src/OetLearner.Api/Services/Reading/ReadingPathwayService.cs`.
// Surfaced at GET /v1/reading-papers/me/pathway (auth-required) and joins
// diagnostic + drilling + mock signals into a single readiness stage.

export type ReadingPathwayStage =
  | 'not_started'
  | 'diagnostic'
  | 'drilling'
  | 'mini_tests'
  | 'mock_ready'
  | 'exam_ready';

export type ReadingPathwayActionKind =
  | 'start_diagnostic'
  | 'start_drill'
  | 'start_mini_test'
  | 'start_mock'
  | 'review_results'
  | 'book_exam';

export interface ReadingPathwayAction {
  kind: ReadingPathwayActionKind;
  label: string;
  drillCode: string | null;
  paperId: string | null;
  route: string | null;
}

export interface ReadingPathwayMilestone {
  code: string;
  label: string;
  achieved: boolean;
  progress: number | null;
  target: number | null;
}

export interface ReadingPathwaySnapshot {
  stage: ReadingPathwayStage;
  headline: string;
  bestScaledScore: number | null;
  openErrorBankCount: number;
  submittedExamAttempts: number;
  submittedPracticeAttempts: number;
  submittedReadingMockAttempts: number;
  weakestSkillTag: string | null;
  nextAction: ReadingPathwayAction;
  milestones: ReadingPathwayMilestone[];
}

export const getReadingPathway = () =>
  api<ReadingPathwaySnapshot>('/v1/reading-papers/me/pathway');

export const getReadingStructureLearner = (paperId: string) =>
  api<ReadingLearnerStructureDto>(`/v1/reading-papers/papers/${paperId}/structure`);

export const getReadingPaperAnnotations = (paperId: string) =>
  api<ReadingPaperAnnotationDto[]>(`/v1/reading-papers/papers/${paperId}/annotations`);

export const createReadingPaperAnnotation = (
  paperId: string,
  body: {
    contentPaperAssetId: string;
    pageNumber: number;
    kind: ReadingPaperAnnotationKind;
    geometryJson: unknown;
  },
) => api<ReadingPaperAnnotationDto>(`/v1/reading-papers/papers/${paperId}/annotations`, {
  method: 'POST',
  body: JSON.stringify(body),
});

export const updateReadingPaperAnnotation = (
  paperId: string,
  annotationId: string,
  body: {
    contentPaperAssetId: string;
    pageNumber: number;
    kind: ReadingPaperAnnotationKind;
    geometryJson: unknown;
  },
) => api<ReadingPaperAnnotationDto>(`/v1/reading-papers/papers/${paperId}/annotations/${annotationId}`, {
  method: 'PUT',
  body: JSON.stringify(body),
});

export const deleteReadingPaperAnnotation = (paperId: string, annotationId: string) =>
  api<void>(`/v1/reading-papers/papers/${paperId}/annotations/${annotationId}`, { method: 'DELETE' });

export const clearReadingPaperAnnotations = (
  paperId: string,
  options: { scope: 'asset'; assetId: string } | { scope: 'paper' },
) => {
  const params = new URLSearchParams({ scope: options.scope });
  if (options.scope === 'asset') params.set('assetId', options.assetId);
  return api<void>(`/v1/reading-papers/papers/${paperId}/annotations?${params.toString()}`, { method: 'DELETE' });
};

export const startReadingAttempt = (
  paperId: string,
  options: { mockAttemptId?: string | null; mockSectionId?: string | null } = {},
) => {
  const params = new URLSearchParams();
  if (options.mockAttemptId) params.set('mockAttemptId', options.mockAttemptId);
  if (options.mockSectionId) params.set('mockSectionId', options.mockSectionId);
  const suffix = params.toString() ? `?${params.toString()}` : '';
  return api<ReadingAttemptStarted>(`/v1/reading-papers/papers/${paperId}/attempts${suffix}`, { method: 'POST' });
};

/**
 * Phase 1 closure — autosave one Reading answer.
 *
 * @param elapsedMs Optional milliseconds the learner spent on this
 *   question between focus and save. Server caps at 14_400_000 ms (4 h)
 *   and silently discards non-positive values. Pass `null` / omit when
 *   timing is unavailable.
 */
export const saveReadingAnswer = (
  attemptId: string,
  questionId: string,
  userAnswerJson: string,
  elapsedMs?: number | null,
) =>
  api<void>(`/v1/reading-papers/attempts/${attemptId}/answers/${questionId}`, {
    method: 'PUT',
    body: JSON.stringify(
      elapsedMs != null && elapsedMs > 0
        ? { userAnswerJson, elapsedMs: Math.floor(elapsedMs) }
        : { userAnswerJson },
    ),
  });

export const resumeReadingBreak = (attemptId: string) =>
  api<ReadingAttemptBreakState>(`/v1/reading-papers/attempts/${attemptId}/break/resume`, { method: 'POST' });

/**
 * Phase 1 closure — submit a Reading attempt for grading.
 *
 * The deterministic `Idempotency-Key` header lets a retried POST (network
 * blip, double-click, second tab) collide with the original on the server
 * and return the cached grading result instead of re-grading. The key
 * shape is `reading-submit:{attemptId}` — derived without user input so
 * any client retry of the same logical submission matches.
 */
export const submitReadingAttempt = (attemptId: string) =>
  api<ReadingAttemptGraded>(`/v1/reading-papers/attempts/${attemptId}/submit`, {
    method: 'POST',
    headers: { 'Idempotency-Key': `reading-submit:${attemptId}` },
  });

export const getReadingAttempt = (attemptId: string) =>
  api<{
    id: string; paperId: string; status: ReadingAttemptStatus;
    /** Phase 3: practice mode tag — Exam | Learning | Drill | MiniTest | ErrorBank. */
    mode: 'Exam' | 'Learning' | 'Drill' | 'MiniTest' | 'ErrorBank';
    /** Subset modes only — question IDs the player should expose. */
    scopeQuestionIds: string[] | null;
    startedAt: string; deadlineAt: string | null; submittedAt: string | null;
    rawScore: number | null; scaledScore: number | null; maxRawScore: number;
    partADeadlineAt: string; partBCDeadlineAt: string;
    partABreakAvailable: boolean; partABreakResumed: boolean; partBCTimerPausedAt: string | null;
    partBCPausedSeconds: number; partABreakMaxSeconds: number;
    answeredCount: number; totalQuestions: number; canResume: boolean;
    answers: Array<{
      readingQuestionId: string; userAnswerJson: string;
      isCorrect: boolean | null; pointsEarned: number; answeredAt: string;
    }>;
    showExplanations: boolean;
  }>(`/v1/reading-papers/attempts/${attemptId}`);

export const getReadingAttemptReview = (attemptId: string) =>
  api<ReadingAttemptReviewDto>(`/v1/reading-papers/attempts/${attemptId}/review`);

// ── Phase 3: Practice Mode + Error Bank ────────────────────────────────

export interface ReadingPracticeStartedDto {
  mode: 'Learning' | 'Drill' | 'MiniTest' | 'ErrorBank';
  attemptId: string;
  startedAt: string;
  deadlineAt: string;
  /** Only present for Learning mode (full-paper attempts). */
  partADeadlineAt?: string;
  /** Only present for Learning mode (full-paper attempts). */
  partBCDeadlineAt?: string;
  paperTitle: string;
  /** Only present for Learning mode (full-paper attempts). */
  partATimerMinutes?: number;
  /** Only present for Learning mode (full-paper attempts). */
  partBCTimerMinutes?: number;
  /** Subset modes only. */
  minutes?: number;
  /** Subset modes only — number of in-scope questions. */
  questionCount?: number;
  /** Drill mode only. */
  drill?: { code: string; title: string; partCode: 'A' | 'B' | 'C' };
  /** Part practice mode only. */
  partPractice?: { partCode: ReadingPartCode; title: string };
  playerRoute: string;
}

export interface ReadingDrillCatalogueDto {
  drills: Array<{
    code: string;
    title: string;
    description: string;
    partCode: 'A' | 'B' | 'C';
    skillTag: string | null;
    questionCount: number;
    minutes: number;
  }>;
  miniTests: Array<{ minutes: 5 | 10 | 15; label: string; questionCount: number }>;
}

export interface ReadingErrorBankEntryDto {
  id: string;
  readingQuestionId: string;
  partCode: ReadingPartCode;
  timesWrong: number;
  lastSeenWrongAt: string;
  lastWrongAttemptId: string;
  questionStem: string | null;
  questionType: ReadingQuestionType | null;
  skillTag: string | null;
  paper: { id: string; title: string; slug: string } | null;
}

export interface ReadingErrorBankDto {
  totals: {
    open: number;
    resolved: number;
    byPart: Partial<Record<ReadingPartCode, number>>;
  };
  entries: ReadingErrorBankEntryDto[];
}

export const startReadingLearningAttempt = (paperId: string) =>
  api<ReadingPracticeStartedDto>(
    `/v1/reading-papers/papers/${paperId}/practice/learning`,
    { method: 'POST' },
  );

export const startReadingPartPracticeAttempt = (paperId: string, partCode: ReadingPartCode) =>
  api<ReadingPracticeStartedDto>(
    `/v1/reading-papers/papers/${paperId}/practice/parts/${partCode}`,
    { method: 'POST' },
  );

export const getReadingErrorBank = (opts?: { partCode?: ReadingPartCode; limit?: number }) => {
  const params = new URLSearchParams();
  if (opts?.partCode) params.set('partCode', opts.partCode);
  if (opts?.limit) params.set('limit', String(opts.limit));
  const qs = params.toString();
  return api<ReadingErrorBankDto>(
    `/v1/reading-papers/practice/error-bank${qs ? `?${qs}` : ''}`,
  );
};

export const clearReadingErrorBankEntry = (entryId: string) =>
  api<void>(`/v1/reading-papers/practice/error-bank/${entryId}`, { method: 'DELETE' });

// ── Phase 3b: Drills, Mini-Tests, Error-Bank Retest ─────────────────────

export const getReadingDrillCatalogue = () =>
  api<ReadingDrillCatalogueDto>(`/v1/reading-papers/practice/drills`);

export const startReadingDrill = (paperId: string, drillCode: string) =>
  api<ReadingPracticeStartedDto>(
    `/v1/reading-papers/papers/${paperId}/practice/drills/${drillCode}`,
    { method: 'POST' },
  );

export const startReadingMiniTest = (paperId: string, minutes: 5 | 10 | 15) =>
  api<ReadingPracticeStartedDto>(
    `/v1/reading-papers/papers/${paperId}/practice/mini-test`,
    { method: 'POST', body: JSON.stringify({ minutes }) },
  );

export const startReadingErrorBankRetest = (opts?: {
  partCode?: ReadingPartCode;
  limit?: number;
}) =>
  api<ReadingPracticeStartedDto>(
    `/v1/reading-papers/practice/error-bank/retest`,
    { method: 'POST', body: JSON.stringify(opts ?? {}) },
  );
