/**
 * Listening authoring API client (admin-side).
 *
 * Mirrors lib/reading-authoring-api.ts for the Listening 42-item question map.
 * Structure and extract metadata are authored through JSON-compatible admin
 * endpoints, then projected into the relational Listening tables by backfill.
 * The learner runtime prefers relational rows when present and keeps JSON as a
 * migration fallback.
 */

import { apiClient } from './api';

export type ListeningPartCode = 'A1' | 'A2' | 'B' | 'C1' | 'C2';
export type ListeningQuestionType = 'short_answer' | 'fill_in_blank' | 'multiple_choice_3';
export type ListeningExtractKind = 'consultation' | 'workplace' | 'presentation';

/**
 * The 10 restructured Listening sub-sections. Part B was split into B1–B6, so a
 * paper now has its own audio + countdown timer + questions per sub-section.
 * These codes are accepted verbatim by the backend authoring service
 * (`NormalizePartCode` / `NormalizeExtractPartCode` in
 * `Services/Listening/ListeningAuthoringService.cs`).
 */
export type ListeningSubSectionCode =
  | 'A1' | 'A2'
  | 'B1' | 'B2' | 'B3' | 'B4' | 'B5' | 'B6'
  | 'C1' | 'C2';

/** All 10 sub-section codes in canonical display order. */
export const LISTENING_SUB_SECTION_CODES: readonly ListeningSubSectionCode[] = [
  'A1', 'A2', 'B1', 'B2', 'B3', 'B4', 'B5', 'B6', 'C1', 'C2',
] as const;

/** Per-sub-section canonical question counts (A1=12, A2=12, B1..B6=1, C1=6, C2=6 → 42). */
export const LISTENING_SUB_SECTION_QUESTION_TARGETS: Record<ListeningSubSectionCode, number> = {
  A1: 12, A2: 12,
  B1: 1, B2: 1, B3: 1, B4: 1, B5: 1, B6: 1,
  C1: 6, C2: 6,
};

/**
 * Admin-facing content types offered in every sub-section, mapped to the
 * backend wire `type`. There are three distinct stored types:
 * `multiple_choice_3` (MCQ), `fill_in_blank`, and `short_answer` (free text).
 * `FillInBlank` and `ShortAnswer` are grading-identical (stem + correct answer
 * + accepted variants, no options) and both render as a text box for the
 * learner, but they persist distinctly so the admin round-trips the author's
 * choice (the backend projects `fill_in_blank` → `ListeningQuestionType.FillInBlank`).
 */
export type ListeningContentType = 'MultipleChoice3' | 'FillInBlank' | 'ShortAnswer';

export const LISTENING_CONTENT_TYPE_LABELS: Record<ListeningContentType, string> = {
  MultipleChoice3: 'Multiple choice (3 options)',
  FillInBlank: 'Fill in the blank',
  ShortAnswer: 'Short answer',
};

/** Map an admin content type to the backend runtime wire `type`. */
export function contentTypeToWire(type: ListeningContentType): ListeningQuestionType {
  switch (type) {
    case 'MultipleChoice3': return 'multiple_choice_3';
    case 'FillInBlank': return 'fill_in_blank';
    default: return 'short_answer';
  }
}

/**
 * Map a stored runtime wire `type` back to an admin content type. All three
 * types persist distinctly; an unknown/legacy value resolves to `ShortAnswer`.
 */
export function wireToContentType(type: string | null | undefined): ListeningContentType {
  switch (type) {
    case 'multiple_choice_3': return 'MultipleChoice3';
    case 'fill_in_blank': return 'FillInBlank';
    default: return 'ShortAnswer';
  }
}

/** True when a sub-section code belongs to Part A (note-completion consultations). */
export function isPartASubSection(code: string): boolean {
  return code === 'A1' || code === 'A2';
}

/** Phase 4: per-option distractor category for Part B/C MCQ analysis. */
export type ListeningDistractorCategory =
  | 'too_strong'
  | 'too_weak'
  | 'wrong_speaker'
  | 'opposite_meaning'
  | 'reused_keyword'
  | 'out_of_scope';

/** Phase 4: speaker attitude tag for Part C extracts. */
export type ListeningSpeakerAttitude =
  | 'concerned'
  | 'optimistic'
  | 'doubtful'
  | 'critical'
  | 'neutral'
  | 'other';

export interface ListeningAuthoredQuestion {
  id: string;
  number: number;
  partCode: ListeningPartCode;
  type: ListeningQuestionType;
  stem: string;
  options: string[];
  correctAnswer: string;
  acceptedAnswers: string[];
  explanation: string | null;
  skillTag: string | null;
  transcriptExcerpt: string | null;
  distractorExplanation: string | null;
  points: number;
  // Phase 4: parallel arrays aligned with `options` (length matches options).
  // `null` means "not authored yet" for that option.
  optionDistractorWhy?: (string | null)[];
  optionDistractorCategory?: (ListeningDistractorCategory | null)[];
  // Phase 4: Part C only — the speaker's attitude in the extract.
  speakerAttitude?: ListeningSpeakerAttitude | null;
  // Phase 5: time-coded transcript evidence (start/end ms in section audio)
  // for jump-to-evidence in the post-attempt review player.
  transcriptEvidenceStartMs?: number | null;
  transcriptEvidenceEndMs?: number | null;
}

export interface ListeningAuthoredSpeaker {
  id: string;
  role: string;
  gender?: 'm' | 'f' | 'nb' | null;
  accent?: string | null;
}

export interface ListeningAuthoredExtract {
  partCode: ListeningPartCode | ListeningSubSectionCode;
  displayOrder: number;
  kind: ListeningExtractKind;
  title: string;
  accentCode: string | null;
  speakers: ListeningAuthoredSpeaker[];
  audioStartMs: number | null;
  audioEndMs: number | null;
  /**
   * Per-sub-section countdown shown to the learner before/while playing
   * (seconds). Optional for backward-compatibility with extract builders that
   * predate the per-sub-section timer; treated as "no timer" when absent.
   */
  timeLimitSeconds?: number | null;
  /**
   * Part A note-completion body (markdown-ish grammar defined in
   * `lib/listening-part-a-notes.ts`). Null / absent for Part B/C extracts or
   * when not yet authored. Round-trips through the manifest import/export.
   */
  notesBody?: string | null;
}

export interface ListeningAuthoredExtractList {
  extracts: ListeningAuthoredExtract[];
}

export interface ListeningValidationCounts {
  partACount: number;
  partBCount: number;
  partCCount: number;
  totalItems: number;
}

export interface ListeningAuthoredQuestionList {
  questions: ListeningAuthoredQuestion[];
  counts: ListeningValidationCounts;
}

export interface ListeningValidationIssue {
  code: string;
  severity: 'error' | 'warning';
  message: string;
}

export interface ListeningValidationReport {
  isPublishReady: boolean;
  issues: ListeningValidationIssue[];
  counts: ListeningValidationCounts;
}

// Delegates to the shared API client (lib/api.ts) so every authoring call
// inherits auth, CSRF, credentials, timeout, retry-on-5xx/408/429, and a
// normalized `ApiError` (status + code + retryable, detectable via
// `isApiError`). Call sites keep passing `JSON.stringify(...)` string bodies,
// which the shared client forwards verbatim with a JSON Content-Type.
async function api<T>(path: string, init?: RequestInit): Promise<T> {
  return apiClient.request<T>(path, init);
}

export const getListeningStructure = (paperId: string) =>
  api<ListeningAuthoredQuestionList>(`/v1/admin/papers/${paperId}/listening/structure`);

export const replaceListeningStructure = (
  paperId: string,
  questions: ListeningAuthoredQuestion[],
) =>
  api<ListeningAuthoredQuestionList>(`/v1/admin/papers/${paperId}/listening/structure`, {
    method: 'PUT',
    body: JSON.stringify({ questions }),
  });

export const validateListeningStructure = (paperId: string) =>
  api<ListeningValidationReport>(`/v1/admin/papers/${paperId}/listening/validate`);

/**
 * Per-question PATCH body. Every field is optional; absent (undefined) fields
 * are left untouched on the existing row. Maps onto
 * `ListeningQuestionPatch` in `Services/Listening/ListeningAuthoringService.cs`.
 */
export interface ListeningQuestionPatchBody {
  partCode?: ListeningPartCode;
  type?: ListeningQuestionType;
  stem?: string;
  options?: string[];
  correctAnswer?: string;
  acceptedAnswers?: string[];
  explanation?: string | null;
  skillTag?: string | null;
  transcriptExcerpt?: string | null;
  distractorExplanation?: string | null;
  points?: number;
  optionDistractorWhy?: (string | null)[];
  optionDistractorCategory?: (ListeningDistractorCategory | null)[];
  speakerAttitude?: ListeningSpeakerAttitude | null;
  transcriptEvidenceStartMs?: number | null;
  transcriptEvidenceEndMs?: number | null;
}

/**
 * PATCH one authored question. Server bumps `ListeningQuestion.Version` on any
 * meaningful change (stem / correct answer / options / accepted variants),
 * which in turn flows into the version-pin map snapshotted by in-flight
 * attempts so admin edits never silently invalidate a candidate's grading.
 * Returns the full re-tallied structure so the form can refresh in one call.
 */
export const patchListeningQuestion = (
  paperId: string,
  questionId: string,
  patch: ListeningQuestionPatchBody,
) =>
  api<ListeningAuthoredQuestionList>(
    `/v1/admin/papers/${paperId}/listening/structure/${encodeURIComponent(questionId)}`,
    {
      method: 'PATCH',
      body: JSON.stringify(patch),
    },
  );

export const getListeningExtracts = (paperId: string) =>
  api<ListeningAuthoredExtractList>(`/v1/admin/papers/${paperId}/listening/extracts`);

export const replaceListeningExtracts = (
  paperId: string,
  extracts: ListeningAuthoredExtract[],
) =>
  api<ListeningAuthoredExtractList>(`/v1/admin/papers/${paperId}/listening/extracts`, {
    method: 'PUT',
    body: JSON.stringify({ extracts }),
  });

export interface ListeningExtractPatchBody {
  displayOrder?: number;
  kind?: ListeningExtractKind;
  title?: string;
  accentCode?: string | null;
  speakers?: ListeningAuthoredSpeaker[];
  audioStartMs?: number | null;
  audioEndMs?: number | null;
  /** Per-sub-section countdown (seconds). `null` clears it. */
  timeLimitSeconds?: number | null;
  /** Part A note-completion body. `null` clears it; absent leaves it unchanged. */
  notesBody?: string | null;
}

export const patchListeningExtract = (
  paperId: string,
  extractCode: ListeningPartCode | ListeningSubSectionCode,
  patch: ListeningExtractPatchBody,
) =>
  api<ListeningAuthoredExtractList>(
    `/v1/admin/papers/${paperId}/listening/extracts/${encodeURIComponent(extractCode)}`,
    {
      method: 'PATCH',
      body: JSON.stringify(patch),
    },
  );

/**
 * Set (or clear) the countdown timer for a single sub-section.
 *
 * The backend stores the timer on the per-sub-section extract row, so this
 * upserts that extract: it PATCHes when the extract already exists, and falls
 * back to appending a minimal extract row via `replaceListeningExtracts` when
 * the sub-section has no extract yet (e.g. a freshly-created paper that has not
 * been through extract authoring). Pass `null` to clear the timer.
 *
 * Returns the re-read extract list so callers can refresh in one round-trip.
 */
export async function setListeningSubSectionTimer(
  paperId: string,
  code: ListeningSubSectionCode,
  timeLimitSeconds: number | null,
): Promise<ListeningAuthoredExtractList> {
  const current = await getListeningExtracts(paperId);
  const existing = current.extracts.find(
    (e) => String(e.partCode).toUpperCase() === code,
  );
  if (existing) {
    return patchListeningExtract(paperId, code, { timeLimitSeconds });
  }

  // No extract row yet for this sub-section — append a minimal one carrying the
  // timer. Kind is inferred from the part group so the backend keeps a sensible
  // default; title/audio windows can be filled in later via extract authoring.
  const kind: ListeningExtractKind = code.startsWith('B')
    ? 'workplace'
    : code.startsWith('C')
      ? 'presentation'
      : 'consultation';
  const next: ListeningAuthoredExtract[] = [
    ...current.extracts,
    {
      partCode: code,
      displayOrder: current.extracts.length,
      kind,
      title: `${code} extract`,
      accentCode: null,
      speakers: [],
      audioStartMs: null,
      audioEndMs: null,
      timeLimitSeconds,
    },
  ];
  return replaceListeningExtracts(paperId, next);
}

// ── Part A audio mode (single | per_subsection) ────────────────────────
//
// Mirrors GET/PATCH /v1/admin/papers/{paperId}/listening/part-a-audio-mode.
// "single" makes one uploaded Part-"A" (or A1) audio play across both
// consultations; "per_subsection" keeps independent A1/A2 audio. Stored on
// ContentPaper.ExtractedTextJson; resolved by ListeningLearnerService and
// surfaced to the learner session as `partAAudioMode`.

export type ListeningPartAAudioMode = 'single' | 'per_subsection';

export interface ListeningPartAAudioModeResult {
  mode: ListeningPartAAudioMode;
}

export const getPartAAudioMode = (paperId: string) =>
  api<ListeningPartAAudioModeResult>(`/v1/admin/papers/${paperId}/listening/part-a-audio-mode`);

export const setPartAAudioMode = (paperId: string, mode: ListeningPartAAudioMode) =>
  api<ListeningPartAAudioModeResult>(`/v1/admin/papers/${paperId}/listening/part-a-audio-mode`, {
    method: 'PATCH',
    body: JSON.stringify({ mode }),
  });

// ── Part A AI-assisted data entry (Mistral OCR + Claude) ────────────────
//
// Mirrors POST /extract, GET /extractions[/{id}], POST .../approve|reject.
// The pipeline OCRs the uploaded QuestionPaper + AnswerKey PDFs and stages a
// Pending ListeningExtractionDraft; approval imports it via the same validated
// manifest path as a manual edit. Drafts are never auto-published.

export interface ListeningExtractionRunResult {
  draftId: string;
  status: string;
  gapCountA1: number;
  gapCountA2: number;
  answerCountA1: number;
  answerCountA2: number;
  warnings: string[];
  summary: string;
}

export interface ListeningExtractionDraftSummary {
  id: string;
  status: string;
  proposedAt: string;
  proposedByUserId: string | null;
  summary: string;
  isStub: boolean;
  stubReason: string | null;
}

export interface ListeningExtractionAnswerPreview {
  number: number;
  correctAnswer: string | null;
  acceptedAnswers: string[];
}

export interface ListeningExtractionExtractPreview {
  partCode: string;
  extractNumber: number;
  gapCount: number;
  notesBody: string | null;
  answers: ListeningExtractionAnswerPreview[];
}

export interface ListeningExtractionDraftDetail {
  id: string;
  status: string;
  summary: string;
  isStub: boolean;
  stubReason: string | null;
  extracts: ListeningExtractionExtractPreview[];
}

export const runListeningExtraction = (paperId: string) =>
  api<ListeningExtractionRunResult>(`/v1/admin/papers/${paperId}/listening/extract`, { method: 'POST' });

export const listListeningExtractions = (paperId: string) =>
  api<{ drafts: ListeningExtractionDraftSummary[] }>(`/v1/admin/papers/${paperId}/listening/extractions`);

export const getListeningExtractionDraft = (paperId: string, draftId: string) =>
  api<ListeningExtractionDraftDetail>(
    `/v1/admin/papers/${paperId}/listening/extractions/${encodeURIComponent(draftId)}`,
  );

export const approveListeningExtraction = (paperId: string, draftId: string) =>
  api<{ draftId: string; structure: ListeningAuthoredQuestionList; report: ListeningValidationReport }>(
    `/v1/admin/papers/${paperId}/listening/extractions/${encodeURIComponent(draftId)}/approve`,
    { method: 'POST' },
  );

export const rejectListeningExtraction = (paperId: string, draftId: string, reason?: string) =>
  api<{ ok: boolean }>(
    `/v1/admin/papers/${paperId}/listening/extractions/${encodeURIComponent(draftId)}/reject`,
    { method: 'POST', body: JSON.stringify({ reason: reason ?? null }) },
  );

// ── Learner: course pathway snapshot ───────────────────────────────────
//
// Mirrors `ListeningPathwaySnapshot` from
// `backend/src/OetLearner.Api/Services/Listening/ListeningPathwayService.cs`.
// Surfaced at GET /v1/listening-papers/me/pathway (auth-required) and joins
// completed-attempt, best-scaled, and mock signals into a single readiness
// stage with one structured next-action.

export type ListeningPathwayStage =
  | 'not_started'
  | 'diagnostic'
  | 'drilling'
  | 'mini_tests'
  | 'mock_ready'
  | 'exam_ready';

export type ListeningPathwayActionKind =
  | 'start_diagnostic'
  | 'start_drill'
  | 'start_mini_test'
  | 'start_mock'
  | 'review_results'
  | 'book_exam';

export interface ListeningPathwayAction {
  kind: ListeningPathwayActionKind;
  label: string;
  drillId: string | null;
  paperId: string | null;
  route: string | null;
}

export interface ListeningPathwayMilestone {
  code: string;
  label: string;
  achieved: boolean;
  progress: number | null;
  target: number | null;
}

export interface ListeningPathwaySnapshot {
  stage: ListeningPathwayStage;
  headline: string;
  bestScaledScore: number | null;
  submittedAttempts: number;
  submittedListeningMockAttempts: number;
  nextAction: ListeningPathwayAction;
  milestones: ListeningPathwayMilestone[];
}

export const getListeningPathway = () =>
  api<ListeningPathwaySnapshot>('/v1/listening-papers/me/pathway');

// ── Phase 6: per-learner Listening analytics ───────────────────────────
//
// Mirrors `ListeningStudentAnalyticsDto` in
// `backend/src/OetLearner.Api/Services/Listening/ListeningAnalyticsService.cs`.
// Surfaced at GET /v1/listening-papers/me/analytics.

export interface ListeningPartBreakdown {
  partCode: string;        // "A" | "B" | "C"
  earned: number;
  max: number;
  accuracyPercent: number;
}

export interface ListeningTopWeakness {
  errorType: string;
  label: string;
  count: number;
}

export interface ListeningActionPlanItem {
  headline: string;
  detail: string;
  route: string | null;
}

export interface ListeningStudentAnalytics {
  completedAttempts: number;
  bestScaledScore: number | null;
  averageScaledScore: number | null;
  likelyPassing: boolean;
  partBreakdown: ListeningPartBreakdown[];
  weaknesses: ListeningTopWeakness[];
  actionPlan: ListeningActionPlanItem[];
}

export const getListeningStudentAnalytics = () =>
  api<ListeningStudentAnalytics>('/v1/listening-papers/me/analytics');

// ── Phase 7: admin-side Listening analytics ────────────────────────────

export interface ListeningHardestQuestion {
  paperId: string;
  paperTitle: string;
  questionNumber: number;
  partCode: string;
  attemptCount: number;
  accuracyPercent: number;
}

export interface ListeningDistractorHeat {
  paperId: string;
  questionNumber: number;
  correctAnswer: string;
  wrongAnswerHistogram: Record<string, number>;
}

export interface ListeningCommonMisspelling {
  correctAnswer: string;
  wrongSpelling: string;
  count: number;
}

export interface ListeningAdminAnalytics {
  days: number;
  completedAttempts: number;
  averageScaledScore: number | null;
  percentLikelyPassing: number;
  classPartAverages: ListeningPartBreakdown[];
  hardestQuestions: ListeningHardestQuestion[];
  distractorHeat: ListeningDistractorHeat[];
  commonMisspellings: ListeningCommonMisspelling[];
}

export const getListeningAdminAnalytics = (days: number = 30) =>
  api<ListeningAdminAnalytics>(`/v1/admin/listening/analytics?days=${days}`);

/**
 * Admin-only: export the full normalized + legacy JSON for a single Listening
 * attempt. The backend records this call as an `AuditEvent` of type
 * `ListeningAttemptExported`. Gated by `AdminContentRead`.
 *
 * Returns the parsed JSON payload — the shape is provider-defined and not
 * stable for clients, so it is intentionally typed as `unknown`.
 */
export const exportListeningAdminAttempt = (attemptId: string) =>
  api<unknown>(`/v1/admin/listening/attempts/${encodeURIComponent(attemptId)}/export`);

// ── Canonical scaffold ─────────────────────────────────────────────────────

export const LISTENING_CANONICAL_TOTAL = 42;
export const LISTENING_PART_A_COUNT = 24; // 12 + 12 across two consultations
export const LISTENING_PART_B_COUNT = 6;
export const LISTENING_PART_C_COUNT = 12; // 6 + 6 across two presentations

/**
 * Generates the 42-item canonical OET Listening skeleton with empty stems and
 * answers, ready for an admin to fill in. Numbering matches the printed
 * Question-Paper PDFs (1–42), part codes follow the runtime's expected scheme:
 *   A1 = consultation 1 (Q1–12), A2 = consultation 2 (Q13–24)
 *   B  = workplace extracts (Q25–30)
 *   C1 = presentation 1 (Q31–36), C2 = presentation 2 (Q37–42)
 */
export function buildCanonicalListeningSkeleton(): ListeningAuthoredQuestion[] {
  const items: ListeningAuthoredQuestion[] = [];
  const blank = (n: number, partCode: ListeningPartCode, type: ListeningQuestionType): ListeningAuthoredQuestion => ({
    id: `lq-${n}`,
    number: n,
    partCode,
    type,
    stem: '',
    options: type === 'multiple_choice_3' ? ['', '', ''] : [],
    correctAnswer: '',
    acceptedAnswers: [],
    explanation: null,
    skillTag: null,
    transcriptExcerpt: null,
    distractorExplanation: null,
    points: 1,
    optionDistractorWhy: type === 'multiple_choice_3' ? [null, null, null] : [],
    optionDistractorCategory: type === 'multiple_choice_3' ? [null, null, null] : [],
    speakerAttitude: null,
    transcriptEvidenceStartMs: null,
    transcriptEvidenceEndMs: null,
  });
  for (let i = 1; i <= 12; i++) items.push(blank(i, 'A1', 'short_answer'));
  for (let i = 13; i <= 24; i++) items.push(blank(i, 'A2', 'short_answer'));
  for (let i = 25; i <= 30; i++) items.push(blank(i, 'B', 'multiple_choice_3'));
  for (let i = 31; i <= 36; i++) items.push(blank(i, 'C1', 'multiple_choice_3'));
  for (let i = 37; i <= 42; i++) items.push(blank(i, 'C2', 'multiple_choice_3'));
  return items;
}

// ── WS4: Admin Sequence Builder ────────────────────────────────────────
//
// Optional explicit exam-sequence for a Listening paper. Mirrors
// `ListeningSequence` / `ListeningSequenceItem` in
// `Services/Listening/ListeningSequenceService.cs`. When a paper has no
// authored sequence the session FSM derives the canonical one from policy,
// so the builder UI fetches the derived shape via `deriveListeningSequence`.

/** One ordered FSM phase in a Listening exam-sequence. */
export type ListeningSequenceItemType =
  | 'instruction'
  | 'reading_time'
  | 'beep'
  | 'audio_extract'
  | 'local_check_time'
  | 'global_check_time'
  | 'section_transition'
  | 'auto_submit';

export interface ListeningSequenceItem {
  index: number;
  type: ListeningSequenceItemType;
  partCode: ListeningPartCode | null;
  extractDisplayOrder: number | null;
  durationMs: number | null;
  label: string | null;
}

export interface ListeningSequence {
  items: ListeningSequenceItem[];
  version: number;
}

/** Structured validation result; mirrors `ListeningSequenceValidationReport`. */
export interface ListeningSequenceValidationReport {
  isValid: boolean;
  issues: ListeningValidationIssue[];
  counts: ListeningValidationCounts;
}

/** GET shape: the authored sequence (null when none) plus an `isAuthored` flag. */
export interface ListeningSequenceState {
  sequence: ListeningSequence | null;
  isAuthored: boolean;
}

/** PUT response: the persisted sequence plus the validation report. */
export interface ListeningSequenceSaveResult {
  sequence: ListeningSequence | null;
  report: ListeningSequenceValidationReport;
}

/** POST /derive response: the canonical sequence for a mode (default Exam). */
export interface ListeningSequenceDeriveResult {
  sequence: ListeningSequence;
  mode: string;
}

export const getListeningSequence = (paperId: string) =>
  api<ListeningSequenceState>(`/v1/admin/papers/${paperId}/listening/sequence`);

export const replaceListeningSequence = (
  paperId: string,
  sequence: ListeningSequence,
) =>
  api<ListeningSequenceSaveResult>(`/v1/admin/papers/${paperId}/listening/sequence`, {
    method: 'PUT',
    body: JSON.stringify(sequence),
  });

export const deriveListeningSequence = (paperId: string, mode?: string) => {
  const qs = mode ? `?mode=${encodeURIComponent(mode)}` : '';
  return api<ListeningSequenceDeriveResult>(
    `/v1/admin/papers/${paperId}/listening/sequence/derive${qs}`,
    { method: 'POST', body: JSON.stringify({}) },
  );
};

export const validateListeningSequence = (
  paperId: string,
  sequence: ListeningSequence,
) =>
  api<ListeningSequenceValidationReport>(
    `/v1/admin/papers/${paperId}/listening/sequence/validate`,
    { method: 'POST', body: JSON.stringify(sequence) },
  );

// ── WS5: spec §19 full-test JSON manifest import / export ───────────────
//
// Mirrors `lib/reading-authoring-api.ts`'s `importReadingStructureManifest` /
// `exportReadingStructureManifest`. The manifest is the §19 Listening shape
// (`testTitle` + `partA` / `partB` / `partC`, each holding `extracts[]`). It is
// the round-trip contract with `ListeningStructureManifest` in
// `Services/Listening/ListeningAuthoringService.cs`. Part A extracts carry
// note-completion gap questions (`noteTextBeforeGap`); Part B/C extracts carry
// single MCQ-3 items (`questionStem` / `options.{A,B,C}`).

export interface ListeningManifestOptions {
  A?: string | null;
  B?: string | null;
  C?: string | null;
}

export interface ListeningManifestTranscriptSegment {
  startMs: number;
  endMs: number;
  speakerId?: string | null;
  text?: string | null;
}

export interface ListeningManifestQuestion {
  number: number;
  type?: string | null;                 // gap_fill | short_answer | multiple_choice_3
  noteTextBeforeGap?: string | null;     // Part A note lead-in
  stem?: string | null;                  // Part B/C question stem
  options?: ListeningManifestOptions | null;
  correctAnswer?: string | null;
  acceptedAnswers?: string[] | null;
  explanation?: string | null;
  distractorExplanation?: string | null;
  skillTag?: string | null;
  timestamp?: string | null;             // legacy "mm:ss"
  transcriptEvidenceStartMs?: number | null;
  transcriptEvidenceEndMs?: number | null;
  transcriptExcerpt?: string | null;
  optionDistractorWhy?: (string | null)[] | null;
  optionDistractorCategory?: (ListeningDistractorCategory | null)[] | null;
}

export interface ListeningManifestExtract {
  extractNumber: number;
  questionNumber?: string | null;        // Part B convenience
  questionRange?: string | null;         // Part C convenience
  patientName?: string | null;           // Part A
  professionalRole?: string | null;      // Part A
  context?: string | null;               // Part B
  topic?: string | null;                 // Part C
  format?: string | null;                // Part C — interview | presentation
  audioFile?: string | null;
  readingTimeSeconds?: number | null;
  transcript?: string | null;
  accentCode?: string | null;
  speakerAttitude?: ListeningSpeakerAttitude | null; // Part C
  transcriptSegments?: ListeningManifestTranscriptSegment[] | null;
  speakers?: ListeningAuthoredSpeaker[] | null;
  /** Part A note-completion body. Carries through manifest import/export. */
  notesBody?: string | null;
  questions: ListeningManifestQuestion[];
}

export interface ListeningManifestPart {
  extracts: ListeningManifestExtract[];
}

export interface ListeningStructureManifestDto {
  testTitle?: string | null;
  modeSupport?: string[] | null;
  strictMock?: boolean | null;
  partA?: ListeningManifestPart | null;
  partB?: ListeningManifestPart | null;
  partC?: ListeningManifestPart | null;
}

export interface ListeningStructureImportResultDto {
  structure: ListeningAuthoredQuestionList;
  report: ListeningValidationReport;
}

/** Export the current authored structure + extracts as a §19 manifest. */
export const exportListeningManifest = (paperId: string) =>
  api<ListeningStructureManifestDto>(`/v1/admin/papers/${paperId}/listening/manifest`);

/**
 * Import a complete Listening test from a §19 manifest. When
 * `replaceExisting` is false the server refuses to overwrite an already-authored
 * paper (409); it always refuses once learner attempts exist (409). Returns the
 * re-read structure plus the publish-gate validation report.
 */
export const importListeningManifest = (
  paperId: string,
  manifest: ListeningStructureManifestDto,
  replaceExisting: boolean,
) =>
  api<ListeningStructureImportResultDto>(`/v1/admin/papers/${paperId}/listening/manifest`, {
    method: 'POST',
    body: JSON.stringify({ replaceExisting, manifest }),
  });
