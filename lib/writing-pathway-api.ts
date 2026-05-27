import { apiClient } from './api';

export interface LearnerWritingProfileDto {
  userId: string;
  currentStage: string;
  profession: string;
  targetBand: string;
  examDate: string | null;
  daysPerWeek: number;
  minutesPerDay: number;
  targetCountry: string;
  letterTypeFocus: string[];
  readinessScore: number | null;
  predictedScore: number | null;
  lastDiagnosticEvaluationId: string | null;
  onboardingCompletedAt: string | null;
  pathwayGeneratedAt: string | null;
  weeksRemaining: number | null;
  diagnosticCompleted: boolean;
}

export interface WritingOnboardingRequest {
  profession: string;
  targetBand: string;
  examDate: string | null;
  daysPerWeek: number;
  minutesPerDay: number;
  targetCountry: string;
  letterTypeFocus: string[];
}

export interface WritingPathwayWeekDto {
  weekNumber: number;
  phase: string;
  focusSkills: string[];
  focusLetterTypes: string[];
  theme: string;
  mockScheduled: boolean;
  isCompleted: boolean;
}

export interface WritingPathwayDto {
  currentStage: string;
  totalWeeks: number;
  currentWeek: number;
  weeksRemaining: number;
  readinessScore: number;
  predictedScore: number | null;
  generatedAt: string | null;
  weeks: WritingPathwayWeekDto[];
}

export interface WritingTodayPlanItemDto {
  id: string;
  ordinal: number;
  itemType: string;
  focusSkill: string | null;
  focusCriterion: string | null;
  estimatedMinutes: number;
  title: string;
  description: string;
  actionHref: string;
  contentId: string | null;
  status: string;
}

export interface WritingTodayPlanDto {
  date: string;
  items: WritingTodayPlanItemDto[];
  totalMinutes: number;
  completedCount: number;
}

export interface WritingCanonRuleDto {
  ruleId: string;
  category: string;
  severity: string;
  ruleText: string;
  appliesToLetterTypes: string[];
  appliesToProfessions: string[];
  correctExamples: string[];
  incorrectExamples: string[];
  lessonHref: string | null;
}

export interface WritingCanonViolationStatDto {
  ruleId: string;
  count: number;
  severity: string;
  lastSeenAt: string;
}

export interface WritingCanonDto {
  rules: WritingCanonRuleDto[];
  recentViolations: WritingCanonViolationStatDto[];
  totalRules: number;
  totalRecentViolations: number;
}

export interface WritingLessonProgressDto {
  bodyRead: boolean;
  drillCompleted: boolean;
  quizScore: number | null;
  quizAttempts: number;
  completedAt: string | null;
}

export interface WritingLessonListItemDto {
  id: string;
  slug: string;
  title: string;
  skillCode: string;
  orderIndex: number;
  estimatedMinutes: number;
  isUnlocked: boolean;
  progress: WritingLessonProgressDto | null;
}

export interface WritingLessonQuizQuestionDto {
  id: string;
  prompt: string;
  options: string[];
}

export interface WritingLessonDetailDto extends WritingLessonListItemDto {
  bodyMarkdownEn: string;
  drillPrompt: string;
  quiz: WritingLessonQuizQuestionDto[];
  previousSlug: string | null;
  nextSlug: string | null;
}

export interface WritingLessonProgressRequest {
  bodyRead?: boolean;
  drillCompleted?: boolean;
  quizScore?: number;
}

export interface WritingDrillSummaryDto {
  id: string;
  drillType: string;
  targetSubSkill: string;
  targetCanonRuleId: string | null;
  difficulty: number;
  estimatedMinutes: number;
  title: string;
  attemptCount: number;
  nextDueAt: string | null;
}

export interface WritingDrillDetailDto extends WritingDrillSummaryDto {
  promptMarkdown: string;
  gradingMethod: string;
}

export interface WritingDrillAttemptDto {
  attemptId: string;
  isCorrect: boolean;
  feedbackText: string;
  nextDueAt: string | null;
  repetitions: number;
}

export interface WritingCaseNoteDrillSummaryDto {
  id: string;
  title: string;
  profession: string;
  letterType: string;
  difficulty: number;
  sentenceCount: number;
  attemptCount: number;
}

export interface WritingCaseNoteDrillSentenceDto {
  id: string;
  ordinal: number;
  sentenceText: string;
}

export interface WritingCaseNoteDrillDetailDto extends WritingCaseNoteDrillSummaryDto {
  format: string;
  caseNotesMarkdown: string;
  sentences: WritingCaseNoteDrillSentenceDto[];
}

export interface WritingCaseNoteDrillAttemptDto {
  attemptId: string;
  correctCount: number;
  totalCount: number;
  scorePercent: number;
  feedback: Array<{ sentenceId: string; isCorrect: boolean; correctLabel: string; rationale: string | null }>;
}

export const getWritingProfile = () => apiClient.get<LearnerWritingProfileDto>('/v1/writing-pathway/profile');

export const submitWritingOnboarding = (request: WritingOnboardingRequest) =>
  apiClient.post<LearnerWritingProfileDto>('/v1/writing-pathway/onboarding', request);

export const getWritingPathway = () => apiClient.get<WritingPathwayDto>('/v1/writing-pathway/pathway');

export const getWritingTodayPlan = () => apiClient.get<WritingTodayPlanDto>('/v1/writing-pathway/plan/today');

export const startWritingPlanItem = (itemId: string) =>
  apiClient.post<void>(`/v1/writing-pathway/plan/items/${encodeURIComponent(itemId)}/start`);

export const completeWritingPlanItem = (itemId: string) =>
  apiClient.post<void>(`/v1/writing-pathway/plan/items/${encodeURIComponent(itemId)}/complete`);

export const skipWritingPlanItem = (itemId: string, reason = 'user_skip') =>
  apiClient.post<void>(`/v1/writing-pathway/plan/items/${encodeURIComponent(itemId)}/skip`, { reason });

export const getWritingCanon = (options: { search?: string; severity?: string } = {}) => {
  const params = new URLSearchParams();
  if (options.search) params.set('search', options.search);
  if (options.severity) params.set('severity', options.severity);
  return apiClient.get<WritingCanonDto>(`/v1/writing-pathway/canon${params.size ? `?${params}` : ''}`);
};

export const getWritingLessons = () => apiClient.get<WritingLessonListItemDto[]>('/v1/writing-pathway/lessons');

export const getWritingLesson = (slug: string) =>
  apiClient.get<WritingLessonDetailDto>(`/v1/writing-pathway/lessons/${encodeURIComponent(slug)}`);

export const updateWritingLessonProgress = (slug: string, request: WritingLessonProgressRequest) =>
  apiClient.post<WritingLessonProgressDto>(`/v1/writing-pathway/lessons/${encodeURIComponent(slug)}/progress`, request);

export const getWritingDrills = (skill?: string) => {
  const params = new URLSearchParams();
  if (skill) params.set('skill', skill);
  return apiClient.get<WritingDrillSummaryDto[]>(`/v1/writing-pathway/drills${params.size ? `?${params}` : ''}`);
};

export const getWritingDrill = (id: string) =>
  apiClient.get<WritingDrillDetailDto>(`/v1/writing-pathway/drills/${encodeURIComponent(id)}`);

export const submitWritingDrillAttempt = (id: string, responseText: string, timeSpentSeconds?: number) =>
  apiClient.post<WritingDrillAttemptDto>(`/v1/writing-pathway/drills/${encodeURIComponent(id)}/attempts`, { responseText, timeSpentSeconds });

export const getWritingCaseNoteDrills = () =>
  apiClient.get<WritingCaseNoteDrillSummaryDto[]>('/v1/writing-pathway/case-note-drills');

export const getWritingCaseNoteDrill = (id: string) =>
  apiClient.get<WritingCaseNoteDrillDetailDto>(`/v1/writing-pathway/case-note-drills/${encodeURIComponent(id)}`);

export const submitWritingCaseNoteDrillAttempt = (id: string, responses: Record<string, string>, timeSpentSeconds?: number) =>
  apiClient.post<WritingCaseNoteDrillAttemptDto>(`/v1/writing-pathway/case-note-drills/${encodeURIComponent(id)}/attempts`, { responses, timeSpentSeconds });

export const writingStageLabels: Record<string, string> = {
  onboarding: 'Onboarding',
  diagnostic: 'Diagnostic',
  foundation: 'Foundation',
  practice: 'Targeted practice',
  mastery: 'Mastery',
};

export const writingSkillLabels: Record<string, string> = {
  W1: 'Case note analysis',
  W2: 'Purpose articulation',
  W3: 'Content selection',
  W4: 'Paraphrasing',
  W5: 'Genre conventions',
  W6: 'Style register',
  W7: 'Language accuracy',
  W8: 'Time management',
};

// ─────────────────────────────────────────────────────────────────────────────
// V2 additive helpers (kept here for existing pathway page convenience).
// Full V2 surface lives in `lib/writing/api.ts`.
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingReadinessSubScoresDto {
  mockAverage: number;
  trajectory: number;
  canonCleanRate: number;
  timeMgmt: number;
  typeConsistency: number;
}

export interface WritingReadinessSummaryDto {
  date: string;
  score: number;
  subScores: WritingReadinessSubScoresDto;
  predictedBand: string | null;
  deltaVsLastWeek: number | null;
  computedAt: string;
}

/**
 * Fetch the current writing readiness score. Routes through V2 stats
 * endpoint introduced by WS6. Existing pathway page can call this
 * without taking a dependency on the broader V2 helper module.
 */
export const getWritingReadiness = () =>
  apiClient.get<WritingReadinessSummaryDto>('/v1/writing/stats/readiness');

/**
 * Force a recompute of the learner's pathway. Routes through V2 endpoint
 * introduced by WS6. Used by the existing `/writing/pathway` page when
 * the learner taps "Recalculate".
 */
export const recalculateWritingPathway = () =>
  apiClient.post<WritingPathwayDto>('/v1/writing/pathway/recalculate', {});