/**
 * Shared TS types for the Listening Authoring Workspace UI.
 *
 * Mirrors `OetLearner.Api.Services.Listening` records:
 *   - ListeningAuthoredQuestion
 *   - ListeningAuthoredQuestionList
 *   - ListeningAuthoredExtract / ListeningAuthoredSpeaker
 *   - ListeningQuestionPatch / ListeningExtractPatch
 *   - ListeningValidationReport / ListeningValidationIssue / ListeningValidationCounts
 *   - ListeningBackfillReport
 *   - ListeningExtractionDraft (domain entity surface)
 *
 * Canonical paper shape (publish-gate invariant):
 *   A1=12, A2=12, B=6, C1=6, C2=6 → 42 items, 5 extracts.
 */

export type ListeningPartCode = 'A1' | 'A2' | 'B' | 'C1' | 'C2';

export type ListeningQuestionType = 'short_answer' | 'multiple_choice_3';

export type ListeningExtractKind = 'consultation' | 'workplace' | 'presentation';

export type ListeningDistractorCategory =
  | 'too_strong'
  | 'too_weak'
  | 'wrong_speaker'
  | 'opposite_meaning'
  | 'reused_keyword'
  | 'out_of_scope';

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
  partCode: ListeningPartCode | string;
  type: ListeningQuestionType | string;
  stem: string;
  options: string[] | null;
  correctAnswer: string;
  acceptedAnswers: string[] | null;
  explanation: string | null;
  skillTag: string | null;
  transcriptExcerpt: string | null;
  distractorExplanation: string | null;
  points: number;
  optionDistractorWhy?: (string | null)[] | null;
  optionDistractorCategory?: (string | null)[] | null;
  speakerAttitude?: string | null;
  transcriptEvidenceStartMs?: number | null;
  transcriptEvidenceEndMs?: number | null;
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

export interface ListeningAuthoredSpeaker {
  id: string;
  role: string;
  gender: 'm' | 'f' | 'nb' | null;
  accent: string | null;
}

export interface ListeningAuthoredExtract {
  partCode: ListeningPartCode | string;
  displayOrder: number;
  kind: ListeningExtractKind | string;
  title: string;
  accentCode: string | null;
  speakers: ListeningAuthoredSpeaker[];
  audioStartMs: number | null;
  audioEndMs: number | null;
}

export interface ListeningExtractsResponse {
  extracts: ListeningAuthoredExtract[];
}

export interface ListeningQuestionPatch {
  partCode?: string;
  type?: string;
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
  optionDistractorCategory?: (string | null)[];
  speakerAttitude?: string | null;
  transcriptEvidenceStartMs?: number;
  transcriptEvidenceEndMs?: number;
}

export interface ListeningExtractPatch {
  displayOrder?: number;
  kind?: string;
  title?: string;
  accentCode?: string | null;
  speakers?: ListeningAuthoredSpeaker[];
  audioStartMs?: number;
  audioEndMs?: number;
}

export interface ListeningValidationIssue {
  code: string;
  severity: 'error' | 'warning' | string;
  message: string;
}

export interface ListeningValidationReport {
  isPublishReady: boolean;
  issues: ListeningValidationIssue[];
  counts: ListeningValidationCounts;
  source?: string;
}

export interface ListeningBackfillReport {
  paperId: string;
  success: boolean;
  partsCreated: number;
  extractsCreated: number;
  questionsCreated: number;
  optionsCreated: number;
  reason: string | null;
}

export interface ListeningBackfillAllResponse {
  count: number;
  successCount: number;
  reports: ListeningBackfillReport[];
}

export type ListeningExtractionDraftStatus = 'Pending' | 'Approved' | 'Rejected';

export interface ListeningExtractionDraft {
  id: string;
  paperId: string;
  status: ListeningExtractionDraftStatus;
  proposedAt: string;
  proposedByUserId: string | null;
  isStub: boolean;
  stubReason: string | null;
  summary: string;
  proposedQuestionsJson: string;
  rawAiResponseJson: string | null;
  decidedAt: string | null;
  decidedByUserId: string | null;
  decisionReason: string | null;
}

export interface ListeningExtractProposalResponse {
  draftId: string;
  status: ListeningExtractionDraftStatus;
  summary: string;
  isStub: boolean;
  stubReason: string | null;
  questions: ListeningAuthoredQuestion[];
}
