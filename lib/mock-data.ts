// ──────────────────────────────────────────────
// Canonical domain types for the OET Learner App
//
// These types define the frontend contract for API responses and component
// props across the learner, expert, and admin surfaces. They are imported
// by lib/api.ts and consumed throughout the application.
// ──────────────────────────────────────────────

// ═══════════════════ SHARED TYPES ═══════════════════

export type SubTest = 'Writing' | 'Speaking' | 'Reading' | 'Listening';
export type ExamFamilyCode = 'oet' | 'ielts' | 'pte';
export type Difficulty = 'Easy' | 'Medium' | 'Hard';
export type TaskStatus = 'not_started' | 'in_progress' | 'completed' | 'failed';
export type ReviewStatus = 'reviewed' | 'pending' | 'not_requested';
export type EvalStatus = 'queued' | 'processing' | 'completed' | 'failed';
export type Confidence = 'High' | 'Medium' | 'Low';
export type RiskLevel = 'Low' | 'Moderate' | 'High';

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
  profession: string;
  examFamilyCode: ExamFamilyCode;
  ieltsPathway?: 'academic' | 'general' | null;
  examDate: string | null;
  targetScores: Record<SubTest, number | null>;
  previousAttempts: number;
  weakSubTests: SubTest[];
  studyHoursPerWeek: number;
  targetCountry: string;
  onboardingComplete: boolean;
  goalsComplete: boolean;
  diagnosticComplete: boolean;
  createdAt: string;
}

export interface StudyPlanTask {
  id: string;
  title: string;
  subTest: SubTest;
  duration: string;
  rationale: string;
  dueDate: string;
  status: TaskStatus;
  section: 'today' | 'thisWeek' | 'nextCheckpoint' | 'weakSkillFocus';
  contentId?: string;
  type?: string;
  route?: string;
}

// ═══════════════════ WRITING TYPES ═══════════════════

export interface WritingTask {
  id: string;
  title: string;
  difficulty: Difficulty;
  profession: string;
  time: string;
  criteriaFocus: string;
  scenarioType: string;
  caseNotes: string;
  letterType?: string;
  scenario?: string;
  taskDate?: string;
  writerRole?: string;
  recipient?: string;
  purpose?: string;
  status?: string;
}

export interface ChecklistItem {
  id: number;
  text: string;
  completed: boolean;
}

export interface AnchoredComment {
  id: string;
  text: string;
  comment: string;
}

export interface CriterionFeedback {
  name: string;
  score: number;
  maxScore: number;
  grade: string;
  explanation: string;
  anchoredComments: AnchoredComment[];
  omissions: string[];
  unnecessaryDetails: string[];
  revisionSuggestions: string[];
  strengths: string[];
  issues: string[];
}

export interface WritingResult {
  id: string;
  taskId: string;
  taskTitle: string;
  examFamilyCode: ExamFamilyCode;
  examFamilyLabel: string;
  estimatedScoreRange: string;
  estimatedGradeRange: string;
  confidenceBand: Confidence;
  confidenceLabel: string;
  learnerDisclaimer: string;
  methodLabel: string;
  provenanceLabel: string;
  humanReviewRecommended: boolean;
  escalationRecommended: boolean;
  isOfficialScore: boolean;
  topStrengths: string[];
  topIssues: string[];
  criteria: CriterionFeedback[];
  submittedAt: string;
  evalStatus: EvalStatus;
}

export interface CriteriaDelta {
  name: string;
  original: number;
  revised: number;
  max: number;
}

export interface ModelParagraph {
  id: string;
  text: string;
  rationale: string;
  criteria: string[];
  included: string[];
  excluded: string[];
  languageNotes: string;
}

export interface ModelAnswer {
  taskId: string;
  taskTitle: string;
  profession: string;
  paragraphs: ModelParagraph[];
}

export interface WritingSubmission {
  id: string;
  taskId: string;
  taskTitle: string;
  content: string;
  submittedAt: string;
  evalStatus: EvalStatus;
  scoreEstimate?: string;
  reviewStatus: ReviewStatus;
}

// ═══════════════════ SPEAKING TYPES ═══════════════════

export interface SpeakingTask {
  id: string;
  title: string;
  scenarioType: string;
  difficulty: Difficulty;
  profession: string;
  criteriaFocus: string;
  duration: string;
  prepTimeSeconds?: number;
  roleplayTimeSeconds?: number;
  patientEmotion?: string;
  communicationGoal?: string;
  clinicalTopic?: string;
  criteriaFocusTags?: string[];
  disclaimer?: string;
}

export interface CandidateCard {
  role?: string;
  candidateRole?: string;
  setting?: string;
  patient?: string;
  patientRole?: string;
  brief?: string;
  task?: string;
  background?: string;
  tasks?: string[];
}

export interface RoleCard {
  id: string;
  title: string;
  profession: string;
  setting: string;
  patient: string;
  brief: string;
  tasks: string[];
  background: string;
  candidateCard?: CandidateCard;
  warmUpQuestions?: string[];
  prepTimeSeconds?: number;
  roleplayTimeSeconds?: number;
  patientEmotion?: string;
  communicationGoal?: string;
  clinicalTopic?: string;
  criteriaFocus?: string[];
  disclaimer?: string;
}

export type MarkerType =
  | 'pronunciation'
  | 'fluency'
  | 'grammar'
  | 'vocabulary'
  | 'empathy'
  | 'structure';

export interface TranscriptMarker {
  id: string;
  type: MarkerType;
  startTime: number;
  endTime: number;
  text: string;
  suggestion: string;
}

export interface TranscriptLine {
  id: string;
  speaker: string;
  text: string;
  startTime: number;
  endTime: number;
  markers?: TranscriptMarker[];
}

export interface SpeakingCriterionScore {
  criterionCode: string;
  family: 'linguistic' | 'clinical';
  score: number;
  max: number;
  scoreRange?: string;
  descriptor?: string;
  confidenceBand?: string;
  source?: 'ai_grounded' | 'rulebook_fallback';
  linkedRuleIds?: string[];
  explanation?: string;
}

export type SpeakingReadinessBand =
  | 'not_ready'
  | 'developing'
  | 'borderline'
  | 'exam_ready'
  | 'strong';

export interface SpeakingResult {
  id: string;
  taskId: string;
  taskTitle: string;
  examFamilyCode: ExamFamilyCode;
  examFamilyLabel: string;
  scoreRange: string;
  confidence: Confidence;
  confidenceLabel: string;
  learnerDisclaimer: string;
  methodLabel: string;
  provenanceLabel: string;
  humanReviewRecommended: boolean;
  escalationRecommended: boolean;
  isOfficialScore: boolean;
  strengths: string[];
  improvements: string[];
  evalStatus: EvalStatus;
  submittedAt: string;
  nextDrill?: { title: string; description: string; id: string; route?: string };
  recommendedDrills?: Array<{ title: string; description: string; id: string; route?: string }>;
  // Wave 1 Speaking criterion contract (docs/SPEAKING-MODULE-PLAN.md §3 Wave 1).
  criteria?: SpeakingCriterionScore[];
  criteriaSource?: 'ai_grounded' | 'rulebook_fallback';
  readinessBand?: SpeakingReadinessBand;
  readinessBandLabel?: string;
  estimatedScaledScore?: number;
  passThreshold?: number;
  rubricMax?: number;
  statusReasonCode?: string;
  statusMessage?: string;
  retryable?: boolean;
  retryAfterMs?: number;
  timing?: {
    prepTimeSeconds?: number;
    roleplayTimeSeconds?: number;
    recordedSeconds?: number;
  };
}

export interface PhrasingSegment {
  id: string;
  originalPhrase: string;
  issueExplanation: string;
  strongerAlternative: string;
  drillPrompt: string;
}

// ═══════════════════ READING TYPES ═══════════════════

export interface ReadingText {
  id: string;
  title: string;
  content: string;
}

export type QuestionType = 'mcq' | 'short_answer' | 'matching' | 'gap_fill';

export interface Question {
  id: string;
  number: number;
  text: string;
  type: QuestionType;
  options?: string[];
  correctAnswer: string;
}

export interface ReadingTask {
  id: string;
  title: string;
  part: 'A' | 'B' | 'C';
  timeLimit: number;
  texts: ReadingText[];
  questions: Question[];
}

export interface ReadingResultItem {
  id: string;
  number: number;
  text: string;
  userAnswer: string;
  correctAnswer: string;
  isCorrect: boolean;
  errorType: string;
  explanation: string;
}

export interface ErrorCluster {
  type: string;
  count: number;
  total: number;
  percentage: number;
}

export interface ReadingResult {
  taskId: string;
  title: string;
  score: number;
  totalQuestions: number;
  percentage: number;
  grade: string;
  errorClusters: ErrorCluster[];
  items: ReadingResultItem[];
}

// ═══════════════════ LISTENING TYPES ═══════════════════

export interface ListeningTask {
  id: string;
  title: string;
  audioSrc: string;
  duration: number;
  audioAvailable: boolean;
  audioUnavailableReason?: string;
  transcriptPolicy?: string;
  questions: Question[];
}

export interface ListeningResultQuestion {
  id: string;
  number: number;
  text: string;
  userAnswer: string;
  correctAnswer: string;
  isCorrect: boolean;
  explanation: string;
  allowTranscriptReveal: boolean;
  transcriptExcerpt?: string;
  distractorExplanation?: string;
}

export interface ListeningResult {
  id: string;
  title: string;
  score: number;
  total: number;
  questions: ListeningResultQuestion[];
  recommendedDrill: { id: string; title: string; description: string };
}

export interface ListeningDrill {
  id: string;
  title: string;
  focusLabel: string;
  description: string;
  errorType: string;
  estimatedMinutes: number;
  highlights: string[];
  launchRoute: string;
  reviewRoute: string;
}

export interface ListeningReviewQuestionEvidence {
  id: string;
  number: number;
  text: string;
  learnerAnswer: string;
  correctAnswer: string;
  explanation: string;
  transcriptExcerpt?: string;
  distractorExplanation?: string;
}

export interface ListeningReview {
  id: string;
  title: string;
  transcriptPolicy: string;
  questions: ListeningReviewQuestionEvidence[];
  recommendedDrill?: { id: string; title: string; description: string };
}

// ═══════════════════ MOCK TYPES ═══════════════════

/**
 * Canonical OET mock-type taxonomy (spec §1, Wave 1).
 * Mirrors `MockTypes` constants in `Domain/MockTypes.cs`.
 */
export type MockTypeToken =
  | 'full'
  | 'lrw'
  | 'sub'
  | 'part'
  | 'diagnostic'
  | 'final_readiness'
  | 'remedial';

/** Spec §2 delivery model. */
export type MockDeliveryMode = 'computer' | 'paper' | 'oet_home';

/** Spec §3 strictness preset. */
export type MockStrictness = 'learning' | 'exam' | 'final_readiness';

export interface MockConfig {
  id: string;
  title: string;
  bundleId?: string;
  type: MockTypeToken;
  subType?: SubTest;
  mode: 'practice' | 'exam';
  profession: string;
  strictTimer: boolean;
  includeReview: boolean;
  reviewSelection: 'none' | 'writing' | 'speaking' | 'writing_and_speaking' | 'current_subtest';
  targetCountry?: string | null;
  deliveryMode?: MockDeliveryMode;
  strictness?: MockStrictness;
}

export interface MockSessionSection {
  id: string;
  sectionAttemptId?: string;
  bundleSectionId?: string;
  title: string;
  subtest?: string;
  state: string;
  reviewAvailable: boolean;
  reviewSelected: boolean;
  launchRoute: string;
  contentPaperId?: string;
  contentPaperTitle?: string;
  timeLimitMinutes?: number;
  startedAt?: string | null;
  deadlineAt?: string | null;
  submittedAt?: string | null;
  completedAt?: string | null;
  rawScore?: number | null;
  rawScoreMax?: number | null;
  scaledScore?: number | null;
  grade?: string | null;
}

export interface MockSession {
  sessionId: string;
  state: string;
  config: MockConfig;
  sectionStates: MockSessionSection[];
  resumeRoute: string;
  reportRoute?: string | null;
  reportId?: string | null;
  reviewReservation?: {
    id: string;
    state: string;
    selection: MockConfig['reviewSelection'];
    reservedCredits: number;
    consumedCredits: number;
    releasedCredits: number;
    pendingCredits: number;
    reservedAt: string;
    expiresAt: string;
  } | null;
}

export interface SubTestScore {
  id: string;
  name: SubTest;
  score: string;
  rawScore: string;
  color: string;
  bg: string;
  scaledScore?: number | null;
  grade?: string | null;
  state?: string;
  reviewRequestId?: string | null;
  reviewState?: string | null;
}

export interface MockReport {
  id: string;
  reportId?: string;
  mockAttemptId?: string;
  state?: string;
  title: string;
  date: string;
  profession?: string | null;
  targetCountry?: string | null;
  deliveryMode?: string | null;
  strictness?: string | null;
  overallScore: string;
  overallGrade?: string | null;
  summary: string;
  subTests: SubTestScore[];
  weakestCriterion: { subtest: string; criterion: string; description: string };
  priorComparison: { exists: boolean; priorMockName: string; overallTrend: 'up' | 'down' | 'flat'; details: string };
  reviewSummary?: { queued: number; inReview: number; completed: number; pending: number };
  perModuleReadiness?: Array<{
    subtest: string;
    scaledScore?: number | null;
    grade?: string | null;
    rag: string;
    message: string;
    passThreshold?: number | null;
  }>;
  partScores?: Array<{
    subtest: string;
    rawScore?: string | null;
    scaledScore?: number | null;
    grade?: string | null;
    state?: string | null;
  }>;
  timingAnalysis?: Array<{
    sectionId: string;
    subtest: string;
    startedAt?: string | null;
    submittedAt?: string | null;
    completedAt?: string | null;
    deadlineAt?: string | null;
    secondsUsed?: number | null;
  }>;
  errorCategories?: Array<{
    category: string;
    subtest: string;
    severity: string;
    description: string;
  }>;
  teacherReviewState?: { queued: number; inReview: number; completed: number; pending: number };
  bookingAdvice?: { status: string; message: string; route?: string; score?: number | null };
  retakeAdvice?: { recommendedWindowDays: number; nextMockType: string; subtest: string; message: string };
  proctoringSummary?: {
    totalEvents: number;
    advisoryOnly: boolean;
    criticalEvents: number;
    warningEvents: number;
    byKind: Array<{ kind: string; count: number }>;
    message: string;
  };
  remediationPlan?: Array<{ day: string; title: string; description: string; route: string }>;
  releasePolicy?: 'instant' | 'after_teacher_marking' | 'scheduled' | string;
}

export interface MockBundleOptionSection {
  id: string;
  subtest: string;
  title: string;
  timeLimitMinutes: number;
  reviewEligible: boolean;
  contentPaperId: string;
}

export interface MockBundleOption {
  id: string;
  bundleId: string;
  title: string;
  mockType: MockTypeToken;
  subtest?: string | null;
  professionId?: string | null;
  appliesToAllProfessions: boolean;
  estimatedDurationMinutes: number;
  sections: MockBundleOptionSection[];
  difficulty?: string;
  sourceStatus?: string;
  qualityStatus?: string;
  releasePolicy?: string;
  topicTags?: string[];
  skillTags?: string[];
  watermarkEnabled?: boolean;
  randomiseQuestions?: boolean;
}

export interface MockOptions {
  mockTypes: { id: MockTypeToken; label: string; description: string }[];
  subTypes: { id: string; label: string }[];
  modes: { id: 'exam' | 'practice'; label: string }[];
  professions: { id: string; label: string }[];
  reviewSelections: { id: MockConfig['reviewSelection']; label: string; cost: number }[];
  wallet: { availableCredits: number };
  availableBundles: MockBundleOption[];
  deliveryModes?: { id: MockDeliveryMode; label: string }[];
  strictnessOptions?: { id: MockStrictness; label: string; description?: string }[];
}

export interface MockBooking {
  id: string;
  bookingId: string;
  mockBundleId: string;
  mockAttemptId?: string | null;
  title?: string;
  scheduledStartAt: string;
  timezoneIana: string;
  status: string;
  deliveryMode?: MockDeliveryMode;
  liveRoomState?: string;
  consentToRecording?: boolean;
  rescheduleCount?: number;
  joinUrl?: string | null;
  zoomJoinUrl?: string | null;
  learnerNotes?: string | null;
  releasePolicy?: string;
  candidateCardVisible?: boolean;
  interlocutorCardVisible?: boolean;
}

export interface MockDiagnosticEntitlement {
  allowed: boolean;
  entitlement: string;
  reason?: string | null;
  message?: string | null;
}

// ═══════════════════ PROGRESS & READINESS TYPES ═══════════════════

export interface TrendPoint {
  date: string;
  reading?: number;
  listening?: number;
  writing?: number;
  speaking?: number;
  [key: string]: string | number | undefined;
}

export interface SubTestReadiness {
  id: string;
  name: SubTest;
  readiness: number;
  target: number;
  status: string;
  color: string;
  bg: string;
  barColor: string;
  isWeakest?: boolean;
}

export interface ReadinessData {
  targetDate: string;
  weeksRemaining: number;
  overallRisk: RiskLevel;
  recommendedStudyHours: number;
  weakestLink: string;
  subTests: SubTestReadiness[];
  blockers: { id: number; title: string; description: string }[];
  evidence: {
    source?: string;
    mocksCompleted: number;
    practiceQuestions: number;
    expertReviews: number;
    recentTrend: string;
    lastUpdated: string;
  };
}

export interface Submission {
  id: string;
  contentId: string;
  taskName: string;
  subTest: SubTest;
  attemptDate: string;
  scoreEstimate: string;
  reviewStatus: ReviewStatus;
  evaluationId?: string;
  state?: string;
  comparisonGroupId?: string | null;
  canRequestReview: boolean;
  actions: {
    reopenFeedbackRoute?: string | null;
    compareRoute?: string | null;
    requestReviewRoute?: string | null;
  };
}

export interface SubmissionDetail {
  submission: Submission;
  evidenceSummary: {
    title: string;
    scoreLabel: string;
    stateLabel: string;
    reviewLabel: string;
    nextActionLabel: string;
  };
  strengths: string[];
  issues: string[];
  transcript?: TranscriptLine[];
  criteria?: CriterionFeedback[];
  questionReview?: Array<{
    id: string;
    number: number;
    text: string;
    learnerAnswer: string;
    correctAnswer: string;
    isCorrect: boolean;
    explanation: string;
    transcriptExcerpt?: string;
    distractorExplanation?: string;
  }>;
}

export interface SubmissionComparison {
  canCompare: boolean;
  reason?: string;
  left?: {
    attemptId: string;
    evaluationId?: string;
    scoreRange?: string;
    subtest: SubTest;
  };
  right?: {
    attemptId: string;
    evaluationId?: string;
    scoreRange?: string;
    subtest: SubTest;
  };
  summary?: string;
  comparisonGroupId?: string | null;
}

// ═══════════════════ BILLING TYPES ═══════════════════

export interface Invoice {
  id: string;
  date: string;
  amount: string;
  status: 'Paid' | 'Pending' | 'Failed';
  currency?: string;
  downloadUrl?: string;
}

export interface BillingEntitlements {
  productiveSkillReviewsEnabled: boolean;
  supportedReviewSubtests: string[];
  invoiceDownloadsAvailable: boolean;
}

export interface BillingPlan {
  id: string;
  label: string;
  tier: string;
  description: string;
  price: string;
  interval: string;
  reviewCredits: number;
  canChangeTo: boolean;
  changeDirection: 'current' | 'upgrade' | 'downgrade';
  badge: string;
}

export interface BillingExtra {
  id: string;
  quantity: number;
  price: string;
  description: string;
}

export interface BillingData {
  currentPlan: string;
  currentPlanId: string;
  price: string;
  interval: string;
  status: string;
  nextRenewal: string;
  reviewCredits: number;
  entitlements: BillingEntitlements;
  plans: BillingPlan[];
  extras: BillingExtra[];
  invoices: Invoice[];
}

export type SettingsSectionId = 'profile' | 'goals' | 'study' | 'privacy' | 'notifications' | 'audio' | 'accessibility' | 'danger-zone';

export interface SettingsSectionData {
  section: SettingsSectionId;
  values: Record<string, unknown>;
}

export interface SpeakingTranscriptReview {
  title: string;
  date: string;
  duration: number;
  transcript: TranscriptLine[];
  audioAvailable: boolean;
  audioUrl?: string;
  waveformPeaks: number[];
  disclaimer?: string;
  roleCard?: RoleCard;
}

export interface ProgressEvidenceSummary {
  reviewUsage: {
    totalRequests: number;
    completedRequests: number;
    averageTurnaroundHours: number | null;
    creditsConsumed: number;
  };
  freshness: {
    generatedAt: string;
    usesFallbackSeries: boolean;
  };
}

// ═══════════════════ EXPERT REVIEW TYPES ═══════════════════

export interface TurnaroundOption {
  id: string;
  label: string;
  time: string;
  cost: number;
  description: string;
}

export interface FocusArea {
  id: string;
  label: string;
  description: string;
}

// ═══════════════════ DIAGNOSTIC TYPES ═══════════════════

export interface DiagnosticSubTest {
  subTest: SubTest;
  status: TaskStatus;
  estimatedDuration: string;
  completedAt?: string;
  contentId?: string;
}

export interface DiagnosticSession {
  id: string;
  status: 'not_started' | 'in_progress' | 'completed';
  subTests: DiagnosticSubTest[];
  startedAt?: string;
  completedAt?: string;
}

export interface DiagnosticResult {
  subTest: SubTest;
  scoreRange: string;
  confidence: Confidence;
  strengths: string[];
  issues: string[];
  readiness: number;
  criterionBreakdown: CriterionFeedback[];
}

// ══════════════════════════════════════════════════════
//                    MOCK DATA FIXTURES
// ══════════════════════════════════════════════════════
