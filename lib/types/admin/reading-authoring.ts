// Stub types for Reading Authoring admin surface (implementation pending).
// These placeholders satisfy tsc until the full feature lands.

export interface ReadingDistractorsPayload {
  questionId: string;
  distractors: string[];
}

export interface ReadingExtractionDraft {
  id: string;
  paperId: string;
  status: 'pending' | 'processing' | 'ready' | 'approved' | 'rejected';
  parts: ReadingPartView[];
  createdAt: string;
}

export interface ReadingPartUpsertDto {
  partCode: string;
  title?: string;
  sortOrder?: number;
}

export interface ReadingPartView {
  id: string;
  partCode: string;
  title: string;
  sortOrder: number;
  texts: ReadingTextDto[];
  questions: ReadingQuestionDto[];
}

export interface ReadingQuestionDto {
  id: string;
  partId: string;
  questionNumber: number;
  questionType: string;
  questionText: string;
  correctAnswer: string;
  acceptedSynonyms?: string[];
  explanation?: string;
  sortOrder: number;
  reviewStatus?: string;
}

export interface ReadingQuestionReviewLogEntry {
  id: string;
  questionId: string;
  action: string;
  reviewerName: string;
  comment?: string;
  timestamp: string;
}

export interface ReadingQuestionUpsertDto {
  questionNumber?: number;
  questionType?: string;
  questionText?: string;
  correctAnswer?: string;
  acceptedSynonyms?: string[];
  explanation?: string;
  sortOrder?: number;
}

export interface ReadingReviewTransitionPayload {
  questionId: string;
  action: 'approve' | 'reject' | 'requestChanges';
  comment?: string;
}

export interface ReadingStructure {
  paperId: string;
  parts: ReadingPartView[];
  totalQuestions: number;
  isComplete: boolean;
}

export interface ReadingStructureImportResult {
  paperId: string;
  partsCreated: number;
  questionsCreated: number;
  textsCreated: number;
  errors: string[];
}

export interface ReadingStructureManifest {
  parts: Array<{
    partCode: string;
    title: string;
    questionCount: number;
    textCount: number;
  }>;
}

export interface ReadingStructureManifestImportPayload {
  manifest: ReadingStructureManifest;
  overwrite?: boolean;
}

export interface ReadingTextDto {
  id: string;
  partId: string;
  title: string;
  contentMarkdown: string;
  sortOrder: number;
}

export interface ReadingTextUpsertDto {
  title?: string;
  contentMarkdown?: string;
  sortOrder?: number;
}

export interface ReadingValidationReport {
  paperId: string;
  isValid: boolean;
  errors: string[];
  warnings: string[];
  questionCount: number;
  expectedCount: number;
}

export interface ReorderDto {
  items?: Array<{ id: string; sortOrder: number }>;
  orderedIds?: string[];
}
