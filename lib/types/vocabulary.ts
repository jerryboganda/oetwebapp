// ── Vocabulary Types ───────────────────────────────────────────────────
// Derived from VocabularyEntities.cs and VocabularyService.cs return shapes

export interface VocabularyTerm {
  id: string;
  term: string;
  word: string;             // Alias for term (backend returns both)
  definition: string;
  exampleSentence: string;
  contextNotes: string | null;
  examTypeCode: string;
  professionId: string | null;
  category: 'medical' | 'academic' | 'general' | 'clinical_communication' | string;
  difficulty: 'easy' | 'medium' | 'hard';
  difficultyLevel: string;  // Alias for difficulty
  pronunciation: string | null;
  audioUrl: string | null;
  imageUrl: string | null;
  synonymsJson: string;
  collocationsJson: string;
  relatedTermsJson: string;
}

export interface LearnerVocabulary {
  id: string;
  termId: string;
  term: string;
  word: string;
  definition: string;
  mastery: 'new' | 'learning' | 'reviewing' | 'mastered';
  easeFactor: number;
  intervalDays: number;
  reviewCount: number;
  correctCount: number;
  nextReviewDate: string | null;   // ISO date string (DateOnly)
  dueAt: string | null;           // Formatted date string
  lastReviewedAt: string | null;  // ISO datetime
  addedAt: string;                // ISO datetime
}

export interface VocabularyQuizResult {
  id: string;
  termsQuizzed: number;
  correctCount: number;
  score: number;                  // Percentage 0-100
  durationSeconds: number;
  completedAt: string;            // ISO datetime
}

export interface VocabularyQuizQuestion {
  termId: string;
  term: string;
  word: string;
  definition: string;
  exampleSentence: string;
  options: string[];
  correctIndex: number;
}

export interface VocabularyFlashcard {
  id: string;
  termId: string;
  term: string;
  word: string;
  definition: string;
  exampleSentence: string;
  contextNotes: string | null;
  pronunciation: string | null;
  audioUrl: string | null;
  synonymsJson: string;
  mastery: string;
}

export interface VocabularyStats {
  totalTerms: number;
  mastered: number;
  learning: number;
  reviewing: number;
  newTerms: number;
  dueToday: number;
  streakDays: number;
}

export interface DailyVocabSet {
  date: string;               // ISO date string
  examTypeCode: string;
  terms: VocabularyTerm[];
  quizAvailable: boolean;
}

export interface VocabularyTermsPage {
  total: number;
  page: number;
  pageSize: number;
  terms: VocabularyTerm[];
  items: VocabularyTerm[];
}
