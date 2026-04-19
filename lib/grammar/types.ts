export type GrammarLevel = 'beginner' | 'intermediate' | 'advanced';

export type GrammarExerciseType =
  | 'mcq'
  | 'fill_blank'
  | 'error_correction'
  | 'sentence_transformation'
  | 'matching';

export interface GrammarProgress {
  status: 'not_started' | 'in_progress' | 'completed' | string;
  exerciseScore?: number | null;
  startedAt?: string | null;
  completedAt?: string | null;
}

export interface GrammarLessonSummary {
  id: string;
  title: string;
  description: string;
  category: string;
  level: GrammarLevel | string;
  estimatedMinutes: number;
  sortOrder: number;
  progress?: GrammarProgress | null;
}

export interface GrammarTopicLearner {
  id: string;
  slug: string;
  name: string;
  description: string | null;
  iconEmoji: string | null;
  levelHint: string;
  lessonCount: number;
  masteredCount: number;
}

export interface GrammarRecommendation {
  id: string;
  title: string;
  reason: string;
  lessonId?: string | null;
  topicSlug?: string | null;
}

export interface GrammarOverview {
  topics: GrammarTopicLearner[];
  recommendations: GrammarRecommendation[];
  lessonsTotal: number;
  lessonsCompleted: number;
  lessonsMastered: number;
  overallMasteryScore: number;
}

export interface GrammarContentBlockLearner {
  id: string;
  sortOrder: number;
  type: 'prose' | 'callout' | 'example' | 'note' | string;
  contentMarkdown: string;
}

export interface GrammarExerciseLearner {
  id: string;
  sortOrder: number;
  type: GrammarExerciseType | string;
  promptMarkdown: string;
  options: unknown;
  acceptedAnswers?: string[];
  difficulty?: GrammarLevel | string;
  points?: number;
}

export interface GrammarLessonLearner extends GrammarLessonSummary {
  contentBlocks: GrammarContentBlockLearner[];
  exercises: GrammarExerciseLearner[];
  prerequisiteLessonId?: string | null;
}

export interface GrammarExerciseResult {
  exerciseId: string;
  correct: boolean;
  userAnswer: unknown;
  explanationMarkdown?: string | null;
}

export interface GrammarAttemptResult {
  score: number;
  correctCount: number;
  incorrectCount: number;
  masteryScore: number;
  mastered: boolean;
  xpAwarded: number;
  reviewItemsCreated: number;
  exercises: GrammarExerciseResult[];
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
}

export interface AdminGrammarLessonFull {
  id: string;
  examTypeCode: string;
  topicId: string | null;
  title: string;
  description: string;
  level: string;
  category: string;
  estimatedMinutes: number;
  sortOrder: number;
  sourceProvenance: string;
  prerequisiteLessonIds: string | null;
  publishState: string;
  status: string;
  version: number;
  contentBlocks: Array<{
    id: string;
    sortOrder: number;
    type: string;
    contentMarkdown: string;
  }>;
  exercises: Array<{
    id: string;
    sortOrder: number;
    type: string;
    promptMarkdown: string;
    options: string | null;
    correctAnswer: string | null;
    acceptedAnswers: string | null;
    explanationMarkdown: string;
    difficulty: string;
    points: number;
  }>;
}
