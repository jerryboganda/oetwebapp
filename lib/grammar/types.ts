/**
 * Grammar module — canonical TypeScript contracts.
 *
 * MISSION CRITICAL — keep in lock-step with
 * `backend/src/OetLearner.Api/Services/Grammar/GrammarDtos.cs`.
 * The learner DTOs here deliberately omit correct answers / explanations.
 */

export type GrammarExerciseType =
  | 'mcq'
  | 'fill_blank'
  | 'error_correction'
  | 'sentence_transformation'
  | 'matching';

export type GrammarLevel = 'beginner' | 'intermediate' | 'advanced';

export type GrammarProgressStatus = 'not_started' | 'in_progress' | 'completed';

export type GrammarPublishState = 'draft' | 'review' | 'published' | 'archived';

export interface GrammarTopicLearner {
  id: string;
  slug: string;
  examTypeCode: string;
  name: string;
  description: string | null;
  iconEmoji: string | null;
  levelHint: string;
  sortOrder: number;
  lessonCount: number;
  completedCount: number;
  masteredCount: number;
  avgMasteryScore: number;
}

export interface GrammarLessonSummary {
  id: string;
  examTypeCode: string;
  topicId: string | null;
  topicSlug: string | null;
  title: string;
  description: string | null;
  level: string;
  category: string;
  estimatedMinutes: number;
  sortOrder: number;
  progressStatus: GrammarProgressStatus;
  masteryScore: number;
  exerciseCount: number;
}

export interface GrammarMcqOption {
  id: string;
  label: string;
}

export interface GrammarMatchingOption {
  left: string;
  right: string;
}

export interface GrammarContentBlockLearner {
  id: string;
  sortOrder: number;
  type: 'prose' | 'callout' | 'example' | 'table' | 'note' | string;
  contentMarkdown: string;
  content: unknown | null;
}

/**
 * Learner-facing exercise — correct answers / explanations are omitted.
 * They only appear in {@link GrammarExerciseResult} returned by
 * {@link submitGrammarAttempt}.
 */
export interface GrammarExerciseLearner {
  id: string;
  sortOrder: number;
  type: GrammarExerciseType | string;
  promptMarkdown: string;
  options: GrammarMcqOption[] | GrammarMatchingOption[] | unknown[];
  difficulty: string;
  points: number;
}

export interface GrammarLessonProgress {
  status: GrammarProgressStatus;
  exerciseScore: number | null;
  masteryScore: number;
  attemptCount: number;
  startedAt: string | null;
  lastAttemptedAt: string | null;
  completedAt: string | null;
}

export interface GrammarLessonLearner {
  id: string;
  examTypeCode: string;
  topicId: string | null;
  topicSlug: string | null;
  title: string;
  description: string | null;
  level: string;
  category: string;
  estimatedMinutes: number;
  prerequisiteLessonId: string | null;
  contentBlocks: GrammarContentBlockLearner[];
  exercises: GrammarExerciseLearner[];
  progress: GrammarLessonProgress | null;
}

export interface GrammarRecommendation {
  id: string;
  lessonId: string;
  lessonTitle: string;
  source: string;
  sourceRefId: string | null;
  ruleId: string | null;
  relevance: number;
  createdAt: string;
  dismissedAt: string | null;
}

export interface GrammarOverview {
  topics: GrammarTopicLearner[];
  recommendations: GrammarRecommendation[];
  lessonsCompleted: number;
  lessonsMastered: number;
  lessonsTotal: number;
  overallMasteryScore: number;
}

export interface GrammarExerciseResult {
  exerciseId: string;
  type: string;
  isCorrect: boolean;
  pointsEarned: number;
  maxPoints: number;
  userAnswer: unknown | null;
  correctAnswer: unknown | null;
  explanationMarkdown: string | null;
}

export interface GrammarAttemptResult {
  lessonId: string;
  score: number;
  pointsEarned: number;
  maxPoints: number;
  correctCount: number;
  incorrectCount: number;
  masteryScore: number;
  mastered: boolean;
  xpAwarded: number;
  reviewItemsCreated: number;
  exercises: GrammarExerciseResult[];
}

export interface GrammarAttemptRequest {
  answers: Record<string, unknown>;
}

/** Admin-side (rich) lesson including authoring fields. */
export interface AdminGrammarLessonFull {
  id: string;
  examTypeCode: string;
  topicId: string | null;
  topicSlug: string | null;
  topicName: string | null;
  title: string;
  description: string;
  level: string;
  category: string;
  estimatedMinutes: number;
  sortOrder: number;
  prerequisiteLessonId: string | null;
  prerequisiteLessonIds: string;
  sourceProvenance: string;
  status: string;
  publishState: GrammarPublishState;
  version: number;
  publishedAt: string | null;
  createdAt: string;
  updatedAt: string;
  contentBlocks: Array<{
    id: string;
    sortOrder: number;
    type: string;
    contentMarkdown: string;
    content: string;
  }>;
  exercises: Array<{
    id: string;
    sortOrder: number;
    type: GrammarExerciseType | string;
    promptMarkdown: string;
    options: string;
    correctAnswer: string;
    acceptedAnswers: string;
    explanationMarkdown: string;
    difficulty: string;
    points: number;
  }>;
}

export interface AdminGrammarTopic {
  id: string;
  examTypeCode: string;
  slug: string;
  name: string;
  description: string | null;
  iconEmoji: string | null;
  levelHint: string;
  sortOrder: number;
  status: string;
  createdAt: string;
  updatedAt: string;
}

export const GRAMMAR_EXERCISE_TYPES: GrammarExerciseType[] = [
  'mcq',
  'fill_blank',
  'error_correction',
  'sentence_transformation',
  'matching',
];

export const GRAMMAR_LEVELS: GrammarLevel[] = ['beginner', 'intermediate', 'advanced'];
