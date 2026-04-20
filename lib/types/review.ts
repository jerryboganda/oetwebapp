// ─────────────────────────────────────────────────────────────────────────────
// Review module — shared types (docs/REVIEW-MODULE.md)
// Mirrors the API DTOs from /v1/review/*.
// ─────────────────────────────────────────────────────────────────────────────

export type ReviewSourceType =
  | 'grammar_error'
  | 'reading_miss'
  | 'listening_miss'
  | 'writing_issue'
  | 'speaking_issue'
  | 'pronunciation_finding'
  | 'mock_miss'
  | 'vocabulary';

export type ReviewPromptKind =
  | 'grammar'
  | 'vocabulary'
  | 'pronunciation'
  | 'writing_issue'
  | 'speaking_issue'
  | 'reading_miss'
  | 'listening_miss'
  | 'mock_miss'
  | 'generic';

export type ReviewItemStatus = 'active' | 'mastered' | 'suspended';

export interface ReviewItem {
  id: string;
  examTypeCode: string;
  sourceType: ReviewSourceType;
  sourceId: string | null;
  subtestCode: string;
  criterionCode: string | null;
  title: string | null;
  promptKind: ReviewPromptKind;
  questionJson: string;
  answerJson: string;
  richContentJson?: string | null;
  easeFactor: number;
  intervalDays: number;
  reviewCount: number;
  consecutiveCorrect: number;
  dueDate: string;           // ISO date (YYYY-MM-DD)
  lastReviewedAt?: string | null;
  lastQuality?: number | null;
  status: ReviewItemStatus;
  suspendedAt?: string | null;
  suspendedReason?: string | null;
}

export interface ReviewBreakdownBucket {
  sourceType: ReviewSourceType;
  active: number;
  due: number;
}

export interface ReviewSummary {
  total: number;
  due: number;
  dueToday: number;
  mastered: number;
  upcoming: number;
  suspended: number;
  native?: {
    total: number;
    due: number;
    dueToday: number;
    mastered: number;
    upcoming: number;
  };
  vocabulary?: {
    total: number;
    due: number;
    dueToday: number;
    mastered: number;
    upcoming: number;
  };
  bySource?: ReviewBreakdownBucket[];
  byPromptKind?: { promptKind: ReviewPromptKind; count: number }[];
}

export interface ReviewRetentionPoint {
  date: string;        // YYYY-MM-DD
  reviewed: number;
  correct: number;
  accuracy: number;    // 0..100
}

export interface ReviewRetentionResponse {
  days: number;
  series: ReviewRetentionPoint[];
}

export interface ReviewHeatmapCell {
  sourceType: ReviewSourceType;
  subtest: string;
  criterion: string | null;
  active: number;
  mastered: number;
  suspended: number;
  due: number;
}

export interface ReviewHeatmapResponse {
  cells: ReviewHeatmapCell[];
}

export interface ReviewConfig {
  newCardsPerDay: number;
  reviewsPerDay: number;
}

export interface ReviewSubmissionResponse {
  itemId: string;
  dueDate: string;
  intervalDays: number;
  easeFactor: number;
  status?: ReviewItemStatus;
  masteredJustNow?: boolean;
  routed?: 'vocabulary';
  mastery?: string;
}

// ── Parsed content (UI consumers) ───────────────────────────────────────────

export interface ReviewQuestionPayload {
  text?: string;
  [key: string]: unknown;
}

export interface ReviewAnswerPayload {
  text?: string;
  explanation?: string;
  severity?: string;
  drillPrompt?: string;
  transcriptSnippet?: string;
  score?: number;
  [key: string]: unknown;
}

export interface ReviewRichContent {
  // Pronunciation
  phoneme?: string;
  ruleId?: string;
  score?: number;
  tip?: string;
  audioUrl?: string;
  // Writing/Speaking
  criterionCode?: string;
  severity?: string;
  anchorSnippet?: string;
  suggestedFix?: string;
  transcriptLineId?: string;
  drillPrompt?: string;
  evaluationId?: string;
  feedbackItemId?: string;
  // Reading
  paperId?: string;
  partCode?: string;
  questionId?: string;
  // Listening
  attemptId?: string;
  transcriptSnippet?: string;
  // Grammar
  lessonId?: string;
  exerciseId?: string;
  exerciseType?: string;
  // Mock
  mockReportId?: string;
  sectionCode?: string;
  // Vocabulary (projection)
  termId?: string;
  term?: string;
  definition?: string;
  exampleSentence?: string;
  ipa?: string;
  category?: string;
  [key: string]: unknown;
}

export function safeParseJson<T>(value: string | null | undefined, fallback: T): T {
  if (!value) return fallback;
  try {
    return JSON.parse(value) as T;
  } catch {
    return fallback;
  }
}

export const SOURCE_TYPE_LABELS: Record<ReviewSourceType, string> = {
  grammar_error: 'Grammar',
  reading_miss: 'Reading',
  listening_miss: 'Listening',
  writing_issue: 'Writing',
  speaking_issue: 'Speaking',
  pronunciation_finding: 'Pronunciation',
  mock_miss: 'Mock',
  vocabulary: 'Vocabulary',
};

export const PROMPT_KIND_ICON_KEY: Record<ReviewPromptKind, 'grammar' | 'vocabulary' | 'pronunciation' | 'writing' | 'speaking' | 'reading' | 'listening' | 'mock' | 'generic'> = {
  grammar: 'grammar',
  vocabulary: 'vocabulary',
  pronunciation: 'pronunciation',
  writing_issue: 'writing',
  speaking_issue: 'speaking',
  reading_miss: 'reading',
  listening_miss: 'listening',
  mock_miss: 'mock',
  generic: 'generic',
};

// Canonical 4-button quality scale (matches /vocabulary/flashcards).
export interface QualityOption {
  quality: number;      // SM-2 0–5
  key: string;          // keyboard shortcut (1/2/3/4)
  label: string;        // display
  tone: 'danger' | 'warning' | 'info' | 'success';
}

export const QUALITY_OPTIONS: QualityOption[] = [
  { quality: 0, key: '1', label: 'Forgot', tone: 'danger' },
  { quality: 2, key: '2', label: 'Hard', tone: 'warning' },
  { quality: 3, key: '3', label: 'Good', tone: 'info' },
  { quality: 5, key: '4', label: 'Easy', tone: 'success' },
];
