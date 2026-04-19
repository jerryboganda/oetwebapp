export type GrammarExerciseType = 'mcq' | 'fill_blank' | 'error_correction' | 'sentence_transformation' | 'matching';

export type GrammarDifficulty = 'beginner' | 'intermediate' | 'advanced';

export type GrammarPublishState = 'draft' | 'review' | 'published' | 'archived';

export interface GrammarContentBlockLearner {
  id?: string | null;
  sortOrder: number;
  type: 'prose' | 'callout' | 'example' | 'note' | string;
  contentMarkdown: string;
  content?: string | null;
}

export interface GrammarExerciseChoiceOption {
  id: string;
  label: string;
}

export interface GrammarMatchingPair {
  left: string;
  right: string;
}

export interface GrammarExerciseLearner {
  id: string;
  sortOrder: number;
  type: GrammarExerciseType;
  promptMarkdown: string;
  options: Array<GrammarExerciseChoiceOption | GrammarMatchingPair>;
  difficulty: GrammarDifficulty | string;
  points: number;
  correctAnswer?: unknown;
  acceptedAnswers?: string[];
  explanationMarkdown?: string;
}

export interface GrammarExerciseAuthoring {
  id: string | null;
  sortOrder: number;
  type: GrammarExerciseType | string;
  promptMarkdown: string;
  options: unknown;
  difficulty: GrammarDifficulty | string;
  points: number;
  correctAnswer: unknown;
  acceptedAnswers: string[];
  explanationMarkdown: string;
}

export interface GrammarLessonProgress {
  status: 'not_started' | 'in_progress' | 'completed';
  score: number | null;
  masteryScore: number | null;
  startedAt: string | null;
  completedAt: string | null;
}

export interface GrammarLessonSummary {
  id: string;
  examTypeCode: string;
  topicId: string | null;
  topicSlug: string | null;
  topicName: string | null;
  category: string;
  title: string;
  description: string | null;
  level: GrammarDifficulty | string;
  estimatedMinutes: number;
  sortOrder: number;
  exerciseCount: number;
  progress?: GrammarLessonProgress | null;
  mastered?: boolean;
  statusLabel?: string;
}

export interface GrammarLessonLearner extends GrammarLessonSummary {
  contentBlocks: GrammarContentBlockLearner[];
  exercises: GrammarExerciseLearner[];
  sourceProvenance?: string | null;
}

export interface GrammarExerciseResult {
  exerciseId: string;
  isCorrect: boolean;
  pointsAwarded: number;
  pointsPossible: number;
  userAnswer: unknown;
  correctAnswer: unknown;
  explanationMarkdown: string | null;
  reviewItemCreated: boolean;
}

export interface GrammarAttemptResult {
  lessonId: string;
  score: number;
  masteryScore: number;
  correctCount: number;
  incorrectCount: number;
  mastered: boolean;
  xpAwarded: number;
  reviewItemsCreated: number;
  exercises: GrammarExerciseResult[];
  progress?: GrammarLessonProgress | null;
}

export interface GrammarTopicLearner {
  id: string;
  slug: string;
  name: string;
  description: string | null;
  iconEmoji: string | null;
  levelHint: string;
  lessonCount: number;
  masteredLessonCount: number;
  completedLessonCount: number;
}

export interface GrammarRecommendation {
  id: string;
  lessonId: string;
  title: string;
  reason: string;
  topicSlug: string | null;
  topicName: string | null;
  level: GrammarDifficulty | string;
  estimatedMinutes: number;
  actionLabel?: string;
}

export interface GrammarOverview {
  examTypeCode: string;
  topics: GrammarTopicLearner[];
  lessonsMastered: number;
  lessonsCompleted: number;
  lessonsTotal: number;
  overallMasteryScore: number;
  recommendations: GrammarRecommendation[];
}

export interface AdminGrammarTopic extends GrammarTopicLearner {
  examTypeCode: string;
  status: GrammarPublishState | string;
  sortOrder: number;
  createdAt: string | null;
  updatedAt: string | null;
}

export interface AdminGrammarLessonFull extends Omit<GrammarLessonLearner, 'exercises'> {
  exercises: GrammarExerciseAuthoring[];
  publishState: GrammarPublishState | string;
  version: number;
  sourceProvenance: string;
  prerequisiteLessonIds: string[];
  status: string;
  createdAt: string | null;
  updatedAt: string | null;
  publishedAt: string | null;
}

export interface GrammarLessonDocument {
  topicId: string | null;
  category: string;
  sourceProvenance: string;
  prerequisiteLessonIds: string[];
  contentBlocks: GrammarContentBlockLearner[];
  exercises: GrammarExerciseAuthoring[];
  version: number;
  updatedAt: string;
}

export interface GrammarLessonUpsertPayload {
  examTypeCode: string;
  topicId: string | null;
  title: string;
  description: string | null;
  level: GrammarDifficulty | string;
  category: string;
  estimatedMinutes: number;
  sortOrder: number;
  sourceProvenance: string;
  prerequisiteLessonIds: string[];
  contentBlocks: GrammarContentBlockLearner[];
  exercises: GrammarExerciseAuthoring[];
}

export interface GrammarTopicUpsertPayload {
  examTypeCode: string;
  slug: string;
  name: string;
  description: string | null;
  iconEmoji: string | null;
  levelHint: string;
  sortOrder: number;
  status?: GrammarPublishState | string;
}

export interface AdminGrammarLessonRow extends GrammarLessonSummary {
  status: string;
  publishState: string;
  updatedAt: string;
}
