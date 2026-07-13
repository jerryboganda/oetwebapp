// ── Vocabulary Types ───────────────────────────────────────────────────
// Source of truth: backend/src/OetLearner.Api/Contracts/VocabularyContracts.cs
// Kept in sync with the typed DTO response shapes produced by VocabularyService.

export interface VocabularyTerm {
  id: string;
  term: string;
  definition: string | null;
  exampleSentence: string;
  contextNotes: string | null;
  examTypeCode: string;
  professionId: string | null;
  category: string;
  ipaPronunciation: string | null;
  audioUrl: string | null;
  audioMediaAssetId: string | null;
  imageUrl: string | null;
  synonyms: string[];
  collocations: string[];
  relatedTerms: string[];
  sourceProvenance: string | null;
  status: 'active' | 'draft' | 'archived' | string;
  /**
   * Practice-collection dimension — multi-tag of recall-set codes (`old`,
   * `2023-2025`, `2026`, …). Empty array if the term has not been classified
   * into any practice collection label yet.
   */
  recallSetCodes?: string[];
  /**
   * How many times this term appeared across CSV imports.
   * Higher count = higher exam importance.
   */
  examFrequencyCount?: number;
  /**
   * Per-recall-set occurrence breakdown behind `examFrequencyCount`
   * (set code → count, e.g. `{ old: 2, '2026': 3 }`). Sum equals
   * `examFrequencyCount`. Powers the ×N badge breakdown tooltip.
   */
  recallSetOccurrences?: Record<string, number> | null;
  /**
   * Admin-curated flag marking this term as part of the free preview a
   * non-subscribed learner may access in the Recall Vocabulary Bank.
   */
  isFreePreview?: boolean;
  /**
   * Backend-authoritative paywall flag. When `true` the server has redacted
   * the term's content (definition, example, audio, IPA, synonyms, …) for a
   * non-subscribed learner — the UI must render a blurred/locked placeholder
   * and prompt the learner to subscribe rather than display partial data.
   */
  isLocked?: boolean;
}

export interface VocabularyTermSummary {
  id: string;
  term: string;
  definition: string;
  category: string;
  ipaPronunciation: string | null;
  audioUrl: string | null;
  exampleSentence: string | null;
}

export interface LearnerVocabulary {
  id: string;
  termId: string;
  term: string;
  definition: string;
  mastery: 'new' | 'learning' | 'reviewing' | 'mastered';
  easeFactor: number;
  intervalDays: number;
  reviewCount: number;
  correctCount: number;
  nextReviewDate: string | null;
  dueAt: string | null;
  lastReviewedAt: string | null;
  addedAt: string;
  sourceRef: string | null;
}

export interface MyVocabularyPageResponse {
  total: number;
  page: number;
  pageSize: number;
  items: LearnerVocabulary[];
}

export interface VocabularyQuizResult {
  id: string;
  format: string;
  termsQuizzed: number;
  correctCount: number;
  score: number;                  // Percentage 0-100 (NOT an OET scaled score)
  durationSeconds: number;
  xpAwarded: number;
  completedAt: string;
  newlyMasteredTermIds: string[];
}

export interface VocabularyQuizQuestion {
  termId: string;
  term: string;
  format: string;
  prompt: string;
  options: string[];
  correctIndex: number;           // -1 for text-entry formats
  correctAnswer: string;
  exampleSentence: string | null;
  audioUrl: string | null;
}

export interface VocabularyFlashcard {
  id: string;
  termId: string;
  term: string;
  definition: string;
  exampleSentence: string | null;
  contextNotes: string | null;
  ipaPronunciation: string | null;
  audioUrl: string | null;
  synonyms: string[];
  mastery: string;
  /**
   * How many times this term appeared across recall exams. Drives the "×N"
   * repeat tag; absent/≤1 means the word is not flagged as repeated.
   */
  examFrequencyCount?: number;
  /**
   * Per-recall-set occurrence breakdown behind `examFrequencyCount`
   * (set code → count). Sum equals `examFrequencyCount`. Powers the ×N
   * badge breakdown tooltip.
   */
  recallSetOccurrences?: Record<string, number> | null;
}

export interface VocabularyStats {
  totalInList: number;
  mastered: number;
  reviewing: number;
  learning: number;
  new: number;
  dueToday: number;
  dueThisWeek: number;
  streakDays: number;
  totalTermsInCatalog: number;
}

export interface VocabularyCategoryItem {
  category: string;
  termCount: number;
}

export interface VocabularyCategoriesResponse {
  examTypeCode: string;
  professionId: string | null;
  categories: VocabularyCategoryItem[];
}

export interface VocabularyDailySet {
  date: string;
  newCount: number;
  dueCount: number;
  cards: VocabularyFlashcard[];
}

export interface VocabularyLookupResult {
  found: boolean;
  term: VocabularyTerm | null;
  suggestions: VocabularyTermSummary[];
}

export interface VocabularyGlossResponse {
  term: string;
  ipaPronunciation: string | null;
  shortDefinition: string;
  exampleSentence: string;
  contextNotes: string | null;
  synonyms: string[];
  register: string;
  appliedRuleIds: string[];
  rulebookVersion: string;
  matchedExistingTerm: boolean;
  existingTermId: string | null;
}
