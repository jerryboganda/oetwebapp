// Stub types for Listening Authoring admin surface (implementation pending).
// These placeholders satisfy tsc until the full feature lands.

export interface ListeningAuthoredExtract {
  id: string;
  paperId: string;
  partCode: string;
  title: string;
  audioUrl?: string;
  transcriptMarkdown?: string;
  durationSeconds?: number;
  sortOrder: number;
}

export interface ListeningAuthoredQuestion {
  id: string;
  extractId: string;
  questionNumber: number;
  questionType: string;
  questionText: string;
  correctAnswer: string;
  options?: string[];
  explanation?: string;
}

export interface ListeningAuthoredQuestionList {
  questions: ListeningAuthoredQuestion[];
  totalCount: number;
}

export interface ListeningBackfillAllResponse {
  processed: number;
  errors: string[];
}

export interface ListeningBackfillReport {
  paperId: string;
  extractsBackfilled: number;
  questionsBackfilled: number;
  errors: string[];
}

export interface ListeningExtractionDraft {
  id: string;
  paperId: string;
  status: ListeningExtractionDraftStatus;
  extracts: ListeningAuthoredExtract[];
  createdAt: string;
}

export type ListeningExtractionDraftStatus =
  | 'pending'
  | 'processing'
  | 'ready'
  | 'approved'
  | 'rejected';

export interface ListeningExtractPatch {
  title?: string;
  transcriptMarkdown?: string;
  sortOrder?: number;
}

export interface ListeningExtractProposalResponse {
  draftId: string;
  extracts: ListeningAuthoredExtract[];
}

export interface ListeningExtractsResponse {
  extracts: ListeningAuthoredExtract[];
  totalCount: number;
}

export interface ListeningQuestionPatch {
  questionText?: string;
  correctAnswer?: string;
  options?: string[];
  explanation?: string;
}

export interface ListeningValidationReport {
  paperId: string;
  isValid: boolean;
  errors: string[];
  warnings: string[];
  questionCount: number;
  expectedCount: number;
}
