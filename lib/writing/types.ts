/**
 * Writing Module V2 — shared DTO types.
 *
 * Mirrors backend DTOs serialised by ASP.NET Core (camelCase by default).
 * Source of truth for entities lives in:
 *   backend/src/OetLearner.Api/Domain/Writing*Entities.cs
 *
 * Wave B (WS6 endpoints) MUST serialise responses to match these shapes.
 * Wave C (WS7 pages) imports from this file; if a property is missing here,
 * add it here first, then update the endpoint DTO.
 */

// ─────────────────────────────────────────────────────────────────────────────
// Enums / discriminated unions
// ─────────────────────────────────────────────────────────────────────────────

// All 12 OET Writing professions (spec §1.2) plus a generic fallback. Ids match
// the backend WritingContentStructure allow-list and the admin builder dropdown.
export type WritingProfession =
  | 'medicine'
  | 'nursing'
  | 'dentistry'
  | 'pharmacy'
  | 'physiotherapy'
  | 'occupational-therapy'
  | 'radiography'
  | 'dietetics'
  | 'optometry'
  | 'podiatry'
  | 'speech-pathology'
  | 'veterinary'
  | 'other';

/** Display labels for every WritingProfession id (spec §1.2). */
export const WRITING_PROFESSION_LABELS: Record<WritingProfession, string> = {
  medicine: 'Medicine',
  nursing: 'Nursing',
  dentistry: 'Dentistry',
  pharmacy: 'Pharmacy',
  physiotherapy: 'Physiotherapy',
  'occupational-therapy': 'Occupational Therapy',
  radiography: 'Radiography',
  dietetics: 'Dietetics',
  optometry: 'Optometry',
  podiatry: 'Podiatry',
  'speech-pathology': 'Speech Pathology',
  veterinary: 'Veterinary Science',
  other: 'Other',
};

export const WRITING_PROFESSIONS: WritingProfession[] = [
  'medicine', 'nursing', 'dentistry', 'pharmacy', 'physiotherapy',
  'occupational-therapy', 'radiography', 'dietetics', 'optometry',
  'podiatry', 'speech-pathology', 'veterinary', 'other',
];
export type WritingLetterType =
  | 'LT-RR'
  | 'LT-UR'
  | 'LT-DG'
  | 'LT-TR'
  | 'LT-NM'
  | 'LT-RP';
export type WritingSubSkill = 'W1' | 'W2' | 'W3' | 'W4' | 'W5' | 'W6' | 'W7' | 'W8';
export type WritingCriterionCode = 'c1' | 'c2' | 'c3' | 'c4' | 'c5' | 'c6';
export type WritingStage =
  | 'onboarding'
  | 'diagnostic'
  | 'foundation'
  | 'practice'
  | 'mastery';

export type WritingEditorMode =
  | 'practice'
  | 'coached'
  | 'timed'
  | 'diagnostic'
  | 'mock'
  | 'revision';

export type WritingSeverity = 'high' | 'medium' | 'low';
export type WritingSubmissionStatus =
  | 'queued'
  | 'preflight'
  | 'grading'
  | 'graded'
  | 'failed'
  | 'cancelled';
export type WritingGradingTier = 'express' | 'batched';
export type WritingInputSource = 'editor' | 'paper-ocr' | 'voice-draft';
export type WritingCoachHintCategory = 'style' | 'structure' | 'length' | 'encouragement';
export type WritingDrillType =
  | 'opening-builder'
  | 'denial-conversion'
  | 'closure-selector'
  | 'linker-injection'
  | 'pronoun-rewrite'
  | 'tense-conversion'
  | 'spelling-spot'
  | 'time-marker-rewrite'
  | 're-line-builder'
  | 'examination-finding-rewrite';
export type WritingDrillGradingMethod = 'exact' | 'regex' | 'llm' | 'multiple-choice';
export type WritingDrillInputVariant = 'mcq' | 'fill' | 'open' | 'drag-drop';
export type WritingOcrStatus = 'pending' | 'processing' | 'completed' | 'failed' | 'manual_required';
export type WritingOcrProvider = 'tesseract' | 'gcv';
export type WritingMockSessionStatus =
  | 'started'
  | 'reading'
  | 'writing'
  | 'submitted'
  | 'abandoned';
export type WritingTutorReviewStatus =
  | 'pending'
  | 'claimed'
  | 'in-review'
  | 'submitted'
  | 'expired';
export type WritingCaseNoteRelevance = 'relevant' | 'maybe' | 'irrelevant';
export type WritingItemKind =
  | 'lesson'
  | 'drill'
  | 'letter'
  | 'mock'
  | 'exemplar-review'
  | 'canon-refresher';
export type WritingConfidenceFlag = 'high' | 'medium' | 'low';
export type WritingAppealStatus = 'pending' | 'in-progress' | 'in_progress' | 'pending_manual' | 'resolved' | 'rejected';

// ─────────────────────────────────────────────────────────────────────────────
// Onboarding & profile
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingProfileDto {
  userId: string;
  currentStage: WritingStage;
  profession: WritingProfession;
  subDiscipline: string | null;
  yearsExperience: number | null;
  targetBand: string;
  examDate: string | null;
  daysPerWeek: number;
  minutesPerDay: number;
  targetCountry: string;
  letterTypeFocus: WritingLetterType[];
  readinessScore: number | null;
  predictedScore: number | null;
  onboardingCompletedAt: string | null;
  pathwayGeneratedAt: string | null;
  weeksRemaining: number | null;
  diagnosticCompleted: boolean;
  optInCommunity: boolean;
  optInLeaderboard: boolean;
  optInDataForTraining: boolean;
  accommodationProfile: WritingAccommodationProfileDto | null;
  canonVersionPinned: string | null;
}

export interface WritingAccommodationProfileDto {
  extendedTimerMultiplier: 1 | 1.25 | 1.5;
  largeText: boolean;
  screenReaderMode: boolean;
  dyslexiaFriendlyFont: boolean;
}

export interface WritingProfileBudgetDto {
  minutesAvailablePerDay: number;
  minutesPerDayMin: number;
  minutesPerDayMax: number;
  daysPerWeek: number;
  weeksToExam: number | null;
  totalMinutes: number;
  conflictsWithOtherModules: string[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Pathway / today / daily plan
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingPathwayItemDto {
  id: string;
  orderIndex: number;
  stage: WritingStage;
  phase: string;
  weekNumber: number;
  focusSkill: WritingSubSkill | null;
  focusCriterion: WritingCriterionCode | null;
  itemKind: WritingItemKind;
  contentRefId: string | null;
  title: string;
  description: string;
  estimatedMinutes: number;
  isCompleted: boolean;
}

export interface WritingPathwayV2Dto {
  currentStage: WritingStage;
  totalWeeks: number;
  currentWeek: number;
  weeksRemaining: number;
  readinessScore: number;
  predictedBand: string | null;
  generatedAt: string | null;
  lastRecalculatedAt: string | null;
  weaknessVector: Record<WritingCriterionCode, number>;
  subSkillMastery: Record<WritingSubSkill, number>;
  items: WritingPathwayItemDto[];
}

export interface WritingTodayPlanItemDto {
  id: string;
  ordinal: number;
  itemKind: WritingItemKind;
  focusSkill: WritingSubSkill | null;
  focusCriterion: WritingCriterionCode | null;
  estimatedMinutes: number;
  title: string;
  description: string;
  actionHref: string;
  contentId: string | null;
  status: 'pending' | 'in-progress' | 'completed' | 'skipped';
}

export interface WritingTodayPlanDto {
  date: string;
  items: WritingTodayPlanItemDto[];
  totalMinutes: number;
  completedCount: number;
  regenerationsRemaining: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// Scenarios
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingScenarioStructuredSentenceDto {
  index: number;
  text: string;
  relevance: WritingCaseNoteRelevance;
}

export interface WritingScenarioDto {
  id: string;
  title: string;
  letterType: WritingLetterType;
  profession: WritingProfession;
  subDiscipline: string | null;
  topics: string[];
  difficulty: 1 | 2 | 3 | 4 | 5;
  caseNotesStructured: WritingScenarioStructuredSentenceDto[];
  isDiagnostic: boolean;
  status: 'draft' | 'published' | 'archived';
  createdAt: string;
  updatedAt: string;

  // Exam-faithful authored fields (spec §4/§5). Optional; present on enriched
  // tasks so the paper/computer simulations can render the full task screen.
  internalCode?: string | null;
  taskPromptMarkdown?: string | null;
  writerRole?: string | null;
  todayDate?: string | null;
  fixedInstructions?: string[];
  wordGuideMin?: number;
  wordGuideMax?: number;
  readingTimeSeconds?: number;
  writingTimeSeconds?: number;
  simulationModes?: WritingSimulationMode;
  markingMode?: WritingMarkingMode;

  /** Optional real exam question-paper PDF. Present when the scenario has a stimulus PDF. */
  stimulusPdfMediaAssetId?: string | null;
  /** Pre-computed content path for the stimulus PDF: /v1/media/{id}/content */
  stimulusPdfDownloadPath?: string | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Submissions, grades, appeals
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingSubmissionDto {
  id: string;
  userId: string;
  scenarioId: string;
  mode: WritingEditorMode;
  letterContent: string;
  contentHash: string;
  wordCount: number;
  timeSpentSeconds: number;
  startedAt: string;
  submittedAt: string;
  isRevision: boolean;
  originalSubmissionId: string | null;
  status: WritingSubmissionStatus;
  gradingTier: WritingGradingTier;
  inputSource: WritingInputSource;
}

export interface WritingPerCriterionFeedbackDto {
  score: number;
  feedback: string;
  exemplarFix: string | null;
  citedRuleIds: string[];
}

export interface WritingGradeDto {
  id: string;
  submissionId: string;
  c1Purpose: number;
  c2Content: number;
  c3Conciseness: number;
  c4Genre: number;
  c5Organisation: number;
  c6Language: number;
  rawTotal: number;
  estimatedBand: number;
  bandLabel: string;
  perCriterion: Record<WritingCriterionCode, WritingPerCriterionFeedbackDto>;
  topThreePriorities: string[];
  confidenceFlag: WritingConfidenceFlag;
  modelUsed: string;
  canonVersion: string;
  canonViolations: WritingCanonViolationDto[];
  revisionInvite: {
    shouldOffer: boolean;
    reason: string;
  };
  gradedAt: string;
}

export interface WritingScoreAppealDto {
  id: string;
  submissionId: string;
  status: WritingAppealStatus;
  originalRawTotal: number;
  secondOpinionRawTotal: number | null;
  finalRawTotal: number | null;
  reasoning: string | null;
  requestedAt: string;
  resolvedAt: string | null;
}

export interface WritingDisputeViolationDto {
  ruleId: string;
  violationId: string;
  reason: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Drafts (V2 table)
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingDraftV2Dto {
  userId: string;
  scenarioId: string;
  mode: WritingEditorMode;
  content: string;
  wordCount: number;
  timeSpentSeconds: number;
  lastSavedAt: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Canon
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingCanonRuleV2Dto {
  id: string;
  category: string;
  appliesToLetterTypes: WritingLetterType[];
  appliesToProfessions: WritingProfession[];
  severity: WritingSeverity;
  ruleText: string;
  correctExamples: string[];
  incorrectExamples: string[];
  detectionType: 'regex' | 'llm' | 'structural';
  lessonId: string | null;
  version: string;
  active: boolean;
}

export interface WritingCanonViolationDto {
  id: string;
  submissionId: string;
  ruleId: string;
  ruleText: string;
  severity: WritingSeverity;
  snippet: string;
  lineNumber: number;
  charStart: number;
  charEnd: number;
  suggestedFix: string | null;
  disputed: boolean;
  disputeResolution: 'pending' | 'upheld' | 'rejected' | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Drills
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingDrillDto {
  id: string;
  drillType: WritingDrillType;
  inputVariant: WritingDrillInputVariant;
  targetSubSkill: WritingSubSkill;
  targetCanonRuleId: string | null;
  appliesToProfessions: WritingProfession[];
  appliesToLetterTypes: WritingLetterType[];
  difficulty: 1 | 2 | 3 | 4 | 5;
  promptMarkdown: string;
  expectedAnswer: string | null;
  alternatives: string[] | null;
  options: string[] | null;
  gradingMethod: WritingDrillGradingMethod;
  status: 'draft' | 'published' | 'archived';
}

export interface WritingDrillResponseDto {
  drillId: string;
  responseText: string;
  selectedOptionIndex?: number;
  orderedItems?: string[];
}

export interface WritingDrillAttemptResultDto {
  drillId: string;
  isCorrect: boolean;
  feedbackText: string;
  citedRuleId: string | null;
  nextDueAt: string;
  easeFactor: number;
}

export interface WritingCaseNoteDrillSentenceDto {
  index: number;
  text: string;
  relevance?: WritingCaseNoteRelevance;
}

export interface WritingCaseNoteDrillDto {
  id: string;
  format: 'highlight-relevant' | 'identify-letter-type' | 'identify-recipient' | 'sequence-events' | 'spot-trigger' | 'sort-include-exclude';
  scenarioId: string | null;
  profession: WritingProfession;
  promptMarkdown: string;
  sentences: WritingCaseNoteDrillSentenceDto[];
  options: string[] | null;
  status: 'draft' | 'published' | 'archived';
}

export interface WritingCaseNoteDrillAttemptResultDto {
  drillId: string;
  scorePercent: number;
  perSentence: Array<{
    index: number;
    learnerLabel: WritingCaseNoteRelevance | null;
    correctLabel: WritingCaseNoteRelevance;
    verdict: 'correct' | 'incorrect' | 'partial' | 'missed';
  }>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Lessons & quizzes
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingLessonQuizQuestionDto {
  id: string;
  question: string;
  options: string[];
  correctIndex: number;
  explanation: string;
}

export interface WritingLessonDto {
  id: string;
  subSkill: WritingSubSkill;
  orderInCourse: number;
  title: string;
  bodyMarkdown: string;
  videoUrl: string | null;
  estimatedMinutes: number;
  quizQuestions: WritingLessonQuizQuestionDto[];
  status: 'draft' | 'published' | 'archived';
}

export interface WritingLessonCompletionDto {
  lessonId: string;
  completedAt: string;
  quizScore: number;
  quizAttempts: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// Mocks
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingMockDto {
  id: string;
  scenarioId: string;
  title: string;
  status: 'draft' | 'published' | 'archived';
}

export interface WritingMockSessionDto {
  id: string;
  mockId: string;
  scenarioId: string;
  status: WritingMockSessionStatus;
  startedAt: string;
  readingPhaseEndedAt: string | null;
  submittedAt: string | null;
  submissionId: string | null;
  readingSecondsRemaining: number;
  writingSecondsRemaining: number;
  isPractice: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Coach
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingCoachHintDto {
  id: string;
  sessionId: string;
  category: WritingCoachHintCategory;
  text: string;
  ruleId: string | null;
  charStart: number | null;
  charEnd: number | null;
  createdAt: string;
  dismissed: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Stats & readiness
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingReadinessSubScoreDto {
  mockAverage: number;
  trajectory: number;
  canonCleanRate: number;
  timeMgmt: number;
  typeConsistency: number;
}

export interface WritingReadinessScoreDto {
  date: string;
  score: number;
  subScores: WritingReadinessSubScoreDto;
  predictedBand: string | null;
  deltaVsLastWeek: number | null;
  computedAt: string;
}

export interface WritingBandHistoryPointDto {
  submissionId: string;
  date: string;
  rawTotal: number;
  estimatedBand: number;
  letterType: WritingLetterType;
  isRevision: boolean;
}

export interface WritingCriteriaScoresDto {
  c1: number;
  c2: number;
  c3: number;
  c4: number;
  c5: number;
  c6: number;
}

export interface WritingStatsDashboardDto {
  latestBand: string | null;
  latestRawTotal: number | null;
  trendDeltaLastFive: number;
  targetBand: string;
  daysToExam: number | null;
  streakDays: number;
  topWeaknessCriterion: WritingCriterionCode | null;
  topWeaknessLabel: string | null;
  readiness: WritingReadinessScoreDto | null;
}

export interface WritingStatsBandsDto {
  history: WritingBandHistoryPointDto[];
  targetBand: number | null;
}

export interface WritingStatsCriteriaDto {
  current: WritingCriteriaScoresDto;
  target: WritingCriteriaScoresDto;
}

export interface WritingStatsLetterTypesDto {
  rows: Array<{
    letterType: WritingLetterType;
    attempts: number;
    averageBand: number;
  }>;
}

export interface WritingStatsCanonDto {
  topViolations: Array<{
    ruleId: string;
    ruleText: string;
    count: number;
    trendLast30Days: number;
  }>;
}

export interface WritingStatsTimeDto {
  averageCompletionSeconds: number;
  percentCompletedWithin40Min: number;
  distribution: Array<{ bucketLabel: string; count: number }>;
}

export interface WritingStatsSkillsDto {
  mastery: Record<WritingSubSkill, number>;
}

export interface WritingStatsCalendarDto {
  days: Array<{ date: string; count: number }>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Mistakes
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingCommonMistakeDto {
  id: string;
  category: string;
  summary: string;
  exampleWrong: string;
  exampleRight: string;
  canonRuleId: string | null;
  relatedSubSkill: WritingSubSkill | null;
}

export interface WritingLearnerMistakeStatDto {
  mistakeId: string;
  occurrenceCount: number;
  lastOccurredAt: string;
}

export interface WritingContentAuditEntryDto {
  id: string;
  entityType: string;
  entityId: string;
  action: string;
  actorUserId: string;
  note: string | null;
  occurredAt: string;
}

export interface WritingContentAuditListDto {
  items: WritingContentAuditEntryDto[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Tutor review
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingTutorReviewDto {
  id: string;
  submissionId: string;
  tutorId: string;
  tutorDisplayName: string | null;
  status: WritingTutorReviewStatus;
  freeTextFeedback: string | null;
  perCriterionComments: Record<WritingCriterionCode, string> | null;
  scoreOverride: Partial<WritingCriteriaScoresDto> | null;
  submittedAt: string | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// OCR
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingOcrJobDto {
  id: string;
  submissionId: string | null;
  status: WritingOcrStatus;
  provider: WritingOcrProvider | null;
  confidenceScore: number | null;
  extractedText: string | null;
  imageUrls: string[];
  errorMessage: string | null;
  createdAt: string;
  completedAt: string | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Showcase
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingShowcasePostDto {
  id: string;
  submissionId: string;
  anonymizedLetterContent: string;
  profession: WritingProfession;
  letterType: WritingLetterType;
  status: 'pending' | 'needs_redaction' | 'published' | 'rejected';
  publishedAt: string;
  reactionCount: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// Tools (rewrite / paraphrase / ask / outline)
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingRewriteResultDto {
  originalText: string;
  rewrittenText: string;
  diff: Array<{ kind: 'equal' | 'insert' | 'delete'; text: string }>;
}

export interface WritingParaphraseResultDto {
  originalText: string;
  alternatives: Array<{ formality: 'formal' | 'neutral' | 'concise'; text: string }>;
}

export interface WritingAskMessageDto {
  role: 'learner' | 'coach';
  content: string;
  createdAt: string;
}

export interface WritingAskTurnResponseDto {
  threadId: string;
  reply: WritingAskMessageDto;
}

export interface WritingOutlineResultDto {
  scenarioId: string;
  outline: Array<{
    paragraph: number;
    purpose: string;
    suggestedSentences: string[];
  }>;
}

// ─────────────────────────────────────────────────────────────────────────────
// OET exam-faithful closure (spec §4/§5/§6/§9/§12/§13/§14/§15/§16/§17/§18)
// ─────────────────────────────────────────────────────────────────────────────

export type WritingSimulationMode = 'paper' | 'computer' | 'both';
export type WritingMarkingMode = 'tutor' | 'ai_assisted' | 'double';
export type WritingChecklistRequiredStatus = 'required' | 'optional' | 'irrelevant';
export type WritingChecklistVerdict = 'included' | 'missing' | 'inaccurate' | 'irrelevant';
export type WritingMarkerSequence = 'first' | 'second' | 'senior';
export type WritingModerationStatus =
  | 'pending_first'
  | 'pending_second'
  | 'pending_moderation'
  | 'finalized';
export type WritingAttemptEventType =
  | 'attempt_started'
  | 'reading_started'
  | 'reading_ended'
  | 'writing_started'
  | 'response_typed'
  | 'auto_saved'
  | 'paste'
  | 'focus_lost'
  | 'submit_clicked'
  | 'timer_expired'
  | 'attempt_locked';

/** Enriched authored task = the unified, attemptable, markable WritingScenario. */
export interface WritingTaskDto {
  id: string;
  internalCode: string | null;
  title: string;
  profession: WritingProfession;
  letterType: WritingLetterType;
  difficulty: 1 | 2 | 3 | 4 | 5;
  status: 'draft' | 'published' | 'archived';
  version: number;
  writerRole: string | null;
  todayDate: string | null;
  taskPromptMarkdown: string | null;
  expectedPurpose: string | null;
  expectedAction: string | null;
  fixedInstructions: string[];
  wordGuideMin: number;
  wordGuideMax: number;
  readingTimeSeconds: number;
  writingTimeSeconds: number;
  simulationModes: WritingSimulationMode;
  markingMode: WritingMarkingMode;
  sourceProvenance: string | null;
  createdAt: string;
  updatedAt: string;
  stimulusPdfMediaAssetId?: string | null;
  stimulusPdfDownloadPath?: string | null;
}

/** Admin create/update payload for a writing task (subset that is editable). */
export interface WritingTaskUpsertDto {
  internalCode?: string | null;
  title: string;
  profession: WritingProfession;
  letterType: WritingLetterType;
  difficulty: number;
  writerRole?: string | null;
  todayDate?: string | null;
  taskPromptMarkdown: string;
  expectedPurpose?: string | null;
  expectedAction?: string | null;
  fixedInstructions: string[];
  wordGuideMin: number;
  wordGuideMax: number;
  simulationModes: WritingSimulationMode;
  markingMode: WritingMarkingMode;
  sourceProvenance: string;
  integrityAcknowledged: boolean;
  stimulusPdfMediaAssetId?: string | null;
}

export interface WritingTaskValidationDto {
  isPublishReady: boolean;
  issues: Array<{ code: string; severity: 'error' | 'warning' | string; message: string }>;
}

/** Spec §18 import/export envelope. */
export interface WritingTaskImportJson {
  taskTitle: string;
  internalCode?: string;
  profession: string;
  taskType: string;
  duration?: { readingTimeSeconds?: number; writingTimeSeconds?: number };
  caseNotes: {
    todayDate?: string;
    candidateRole?: string;
  };
  writingTask: {
    instruction: string;
    fixedInstructions?: string[];
    wordGuide?: { min: number; max: number };
  };
  marking?: {
    expectedPurpose?: string;
    expectedAction?: string;
  };
}

export interface WritingAttemptEventDto {
  eventType: WritingAttemptEventType;
  timestamp: string;
  mode: 'paper' | 'computer';
  sessionId?: string | null;
  scenarioId?: string | null;
  submissionId?: string | null;
  payload?: Record<string, unknown>;
}

export interface WritingFeedbackAnnotationDto {
  id: string;
  submissionId: string;
  reviewId: string | null;
  tutorId: string;
  criterion: WritingCriterionCode | null;
  highlightedText: string;
  startOffset: number;
  endOffset: number;
  severity: WritingSeverity;
  suggestion: string | null;
  feedbackText: string;
  createdAt: string;
}

/** Deterministic (or LLM-enriched) pre-assessment feeding the tutor (spec §13.2). */
export interface WritingPreAssessmentDto {
  source: 'heuristic' | 'llm';
  estimatedBands: WritingCriteriaScoresDto;
  estimatedRawTotal: number;
  estimatedBandLabel: string;
  confidence: WritingConfidenceFlag;
  wordCount: number;
  withinWordGuide: boolean;
  keyContentCoveragePercent: number;
  missingKeyContent: string[];
  detectedIrrelevantContent: string[];
  languageNotes: string[];
  suggestedCriterionFeedback: Partial<Record<WritingCriterionCode, string>>;
}

/** Everything a tutor needs on the marking screen (spec §14.2). */
export interface WritingTutorMarkingContextDto {
  submission: WritingSubmissionDto;
  task: WritingTaskDto;
  aiGrade: WritingGradeDto | null;
  preAssessment: WritingPreAssessmentDto;
  existingReview: WritingTutorReviewDto | null;
  annotations: WritingFeedbackAnnotationDto[];
  moderation: WritingModerationDto | null;
  markerSequence: WritingMarkerSequence;
}

export interface WritingTutorReviewSubmitDto {
  freeTextFeedback?: string | null;
  perCriterionComments?: Partial<Record<WritingCriterionCode, string>>;
  scoreOverride?: Partial<WritingCriteriaScoresDto>;
  contentChecklistVerdict?: Record<string, WritingChecklistVerdict>;
  markerSequence?: WritingMarkerSequence;
  acceptedAiPreAssessment?: boolean;
}

export interface WritingModerationDto {
  id: string;
  submissionId: string;
  firstMarkerId: string | null;
  secondMarkerId: string | null;
  seniorMarkerId: string | null;
  firstScore: WritingCriteriaScoresDto | null;
  secondScore: WritingCriteriaScoresDto | null;
  finalScore: WritingCriteriaScoresDto | null;
  variancePoints: number | null;
  varianceReason: string | null;
  finalDecisionNote: string | null;
  status: WritingModerationStatus;
}

export interface WritingResultVisibilityDto {
  showSubmissionReceived: boolean;
  showAiEstimate: boolean;
  showTutorScore: boolean;
  showFullCriteria: boolean;
  showAnnotatedResponse: boolean;
  showMissingContent: boolean;
  showModelAnswer: boolean;
  showContentChecklist: boolean;
  allowRewrite: boolean;
}

/** Gated learner feedback bundle (spec §15.2). Null fields = not yet visible. */
export interface WritingSubmissionFeedbackDto {
  submission: WritingSubmissionDto;
  visibility: WritingResultVisibilityDto;
  status: 'submitted_awaiting_review' | 'ai_estimated' | 'tutor_reviewed';
  grade: WritingGradeDto | null;
  tutorReview: WritingTutorReviewDto | null;
  annotations: WritingFeedbackAnnotationDto[];
  nextSteps: string[];
}

export interface WritingRewriteComparisonDto {
  original: { submissionId: string; letterContent: string; grade: WritingGradeDto | null };
  rewrite: { submissionId: string; letterContent: string; grade: WritingGradeDto | null };
  perCriterionDelta: Partial<Record<WritingCriterionCode, number>>;
}

// ── Admin analytics (spec §16) ──

export interface WritingAdminAnalyticsDto {
  totals: { tasks: number; submissions: number; reviewed: number; learners: number };
  averageCriteria: WritingCriteriaScoresDto;
  averageBandByProfession: Array<{ profession: WritingProfession; averageBand: number; attempts: number }>;
  averageBandByLetterType: Array<{ letterType: WritingLetterType; averageBand: number; attempts: number }>;
  hardestTasks: Array<{ taskId: string; title: string; averageBand: number; attempts: number }>;
  commonMissingContent: Array<{ itemText: string; count: number }>;
  commonIrrelevantContent: Array<{ itemText: string; count: number }>;
  commonLanguageErrors: Array<{ ruleId: string; ruleText: string; count: number; criterion: WritingCriterionCode | null }>;
  wordCountDistribution: Array<{ bucketLabel: string; count: number }>;
  writingPhaseSeconds: { average: number; median: number };
  abandonmentRatePercent: number;
  resubmissionImprovementAverage: number;
}

export interface WritingMarkingQualityDto {
  tutorConsistency: Array<{
    tutorId: string;
    displayName: string | null;
    reviews: number;
    averageRawTotal: number;
    leniencyDelta: number; // vs cohort mean (+ = lenient)
    agreementCoefficient: number;
  }>;
  aiVsTutorVariance: { meanAbsoluteDelta: number; samples: number };
  averageReviewTurnaroundHours: number;
  criteriaDisagreement: Array<{ criterion: WritingCriterionCode; meanAbsoluteDelta: number }>;
  moderationsTriggered: number;
}
