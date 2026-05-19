/**
 * Shared TS types for the Reading Authoring Workspace UI.
 *
 * Mirrors `OetLearner.Api.Services.Reading` records and entities at
 * the JSON-wire level:
 *   - ReadingStructure / ReadingPartView
 *   - ReadingStructureManifest / ReadingPartManifest / ReadingTextManifest / ReadingQuestionManifest
 *   - ReadingValidationReport / ReadingValidationIssue / ReadingValidationCounts
 *   - ReadingPartUpsertDto / ReadingTextUpsertDto / ReadingQuestionUpsertDto
 *   - ReadingExtractionDraft (domain entity surface)
 *   - ReadingQuestionReviewLog (history projection)
 *
 * Canonical paper shape (publish-gate invariant):
 *   Part A = 20 items, Part B = 6 items, Part C = 16 items → 42 total.
 */

export type ReadingPartCode = 'A' | 'B' | 'C';

export type ReadingQuestionType =
  | 'MatchingTextReference'
  | 'ShortAnswer'
  | 'SentenceCompletion'
  | 'MultipleChoice3'
  | 'MultipleChoice4';

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

export type ReadingExtractionStatus =
  | 'Pending'
  | 'Approved'
  | 'Rejected'
  | 'Failed';

// ── Wire shapes (admin includes correct answers) ──────────────────────────

export interface ReadingTextDto {
  id: string;
  readingPartId: string;
  displayOrder: number;
  title: string;
  source: string | null;
  bodyHtml: string;
  wordCount: number;
  topicTag: string | null;
  createdAt?: string;
  updatedAt?: string;
}

export interface ReadingQuestionDto {
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
  optionDistractorsJson: string | null;
  reviewState: ReadingReviewState;
  latestReviewNote: string | null;
  createdAt?: string;
  updatedAt?: string;
}

export interface ReadingPartView {
  id: string;
  partCode: ReadingPartCode;
  timeLimitMinutes: number;
  maxRawScore: number;
  instructions: string | null;
  texts: ReadingTextDto[];
  questions: ReadingQuestionDto[];
}

export interface ReadingStructure {
  paperId: string;
  parts: ReadingPartView[];
}

export interface ReadingValidationIssue {
  code: string;
  severity: 'error' | 'warning' | string;
  message: string;
  targetId: string | null;
}

export interface ReadingValidationCounts {
  partACount: number;
  partBCount: number;
  partCCount: number;
  totalPoints: number;
}

export interface ReadingValidationReport {
  isPublishReady: boolean;
  issues: ReadingValidationIssue[];
  counts: ReadingValidationCounts;
}

// ── Manifest (import / export round-trip) ─────────────────────────────────

export interface ReadingTextManifest {
  displayOrder: number;
  title: string;
  source: string | null;
  bodyHtml: string;
  wordCount: number;
  topicTag: string | null;
}

export interface ReadingQuestionManifest {
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
}

export interface ReadingPartManifest {
  partCode: ReadingPartCode;
  timeLimitMinutes: number | null;
  instructions: string | null;
  texts: ReadingTextManifest[];
  questions: ReadingQuestionManifest[];
}

export interface ReadingStructureManifest {
  parts: ReadingPartManifest[];
}

export interface ReadingStructureManifestImportPayload {
  replaceExisting: boolean;
  manifest: ReadingStructureManifest;
}

export interface ReadingStructureImportResult {
  structure: ReadingStructure;
  report: ReadingValidationReport;
}

// ── Upsert DTOs (per backend ReadingPartUpsertDto etc.) ───────────────────

export interface ReadingPartUpsertDto {
  timeLimitMinutes?: number | null;
  instructions?: string | null;
}

export interface ReadingTextUpsertDto {
  id?: string | null;
  readingPartId: string;
  displayOrder: number;
  title: string;
  source: string | null;
  bodyHtml: string;
  wordCount: number;
  topicTag: string | null;
}

export interface ReadingQuestionUpsertDto {
  id?: string | null;
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

export interface ReorderDto {
  orderedIds: string[];
}

// ── Distractors + review ──────────────────────────────────────────────────

export interface ReadingDistractorsPayload {
  /** Map keyed by option key (e.g. "A","B","C","D") → distractor category. */
  distractors: Record<string, ReadingDistractorCategory>;
}

export interface ReadingReviewTransitionPayload {
  toState: ReadingReviewState;
  note?: string | null;
  isAdminOverride: boolean;
}

export interface ReadingQuestionReviewLogEntry {
  id: string;
  readingQuestionId: string;
  fromState: ReadingReviewState;
  toState: ReadingReviewState;
  reviewerUserId: string;
  reviewerDisplayName: string | null;
  note: string | null;
  transitionedAt: string;
}

// ── Extractions ───────────────────────────────────────────────────────────

export interface ReadingExtractionDraft {
  id: string;
  paperId: string;
  mediaAssetId: string | null;
  status: ReadingExtractionStatus;
  extractedManifestJson: string | null;
  rawAiResponseJson: string | null;
  notes: string | null;
  isStub: boolean;
  createdByAdminId: string;
  resolvedByAdminId: string | null;
  createdAt: string;
  resolvedAt: string | null;
}
