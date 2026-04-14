export type ReviewStatus = 
  | 'queued' 
  | 'assigned' 
  | 'in_progress' 
  | 'draft_saved' 
  | 'awaiting_transcript' 
  | 'awaiting_ai' 
  | 'submitted' 
  | 'rework_requested' 
  | 'completed' 
  | 'overdue' 
  | 'blocked';

export type SubmissionType = 'writing' | 'speaking';

export type Profession = 'medicine' | 'nursing' | 'dentistry' | 'pharmacy' | 'physiotherapy' | 'veterinary' | 'occupational_therapy' | 'dietetics' | 'speech_pathology' | 'radiography' | 'podiatry' | 'optometry';

export type SubTest = 'reading' | 'listening' | 'writing' | 'speaking';

export type AIConfidence = 'high' | 'medium' | 'low' | 'unknown';

export interface ExpertMe {
  userId: string;
  role: 'expert';
  displayName: string;
  email: string;
  timezone: string;
  isActive: boolean;
  specialties: string[];
  createdAt: string;
  isOnboardingComplete?: boolean;
}

export interface ExpertOnboardingProfile {
  displayName: string;
  bio: string;
  photoUrl?: string;
}

export interface ExpertOnboardingQualifications {
  qualifications: string;
  certifications: string;
  experienceYears: number;
}

export interface ExpertOnboardingRates {
  hourlyRateMinorUnits: number;
  sessionRateMinorUnits: number;
  currency: string;
}

export interface ExpertOnboardingStatus {
  isComplete: boolean;
  completedSteps: string[];
  profile?: ExpertOnboardingProfile | null;
  qualifications?: ExpertOnboardingQualifications | null;
  rates?: ExpertOnboardingRates | null;
}

export interface ExpertReviewActions {
  canClaim: boolean;
  canRelease: boolean;
  canOpen: boolean;
  canSaveDraft: boolean;
  canSubmit: boolean;
  canRequestRework: boolean;
  readOnly: boolean;
}

export interface ExpertArtifactState {
  state: 'queued' | 'processing' | 'completed' | 'failed' | 'stale';
  isStale: boolean;
  message?: string | null;
}

export interface ExpertChecklistItem {
  id: string;
  label: string;
  checked: boolean;
}

export type WritingCriterionKey = 'purpose' | 'content' | 'conciseness' | 'genre' | 'organization' | 'language';

export type SpeakingCriterionKey = 'intelligibility' | 'fluency' | 'appropriateness' | 'grammar' | 'clinicalCommunication';

export interface ReviewRequest {
  id: string;
  learnerId: string;
  learnerName: string;
  profession: Profession;
  subTest: SubTest;
  type: SubmissionType;
  aiConfidence: AIConfidence;
  priority: 'high' | 'normal' | 'low';
  slaDue: string; // ISO Date String
  assignedReviewerId?: string;
  assignedReviewerName?: string;
  assignmentState?: 'unassigned' | 'assigned' | 'claimed' | 'reassigned';
  slaState?: 'on_track' | 'at_risk' | 'overdue' | 'completed_on_time' | 'completed_late';
  isOverdue?: boolean;
  availableActions?: ExpertReviewActions;
  status: ReviewStatus;
  contentId?: string;
  attemptId?: string;
  createdAt: string;
}

export interface ReviewQueueResponse {
  items: ReviewRequest[];
  totalCount: number;
  page: number;
  pageSize: number;
  lastUpdatedAt: string;
}

export interface AnchoredComment {
  id: string;
  criterion?: WritingCriterionKey;
  text: string;
  startOffset: number;
  endOffset: number;
  createdAt: string;
}

export interface TimestampComment {
  id: string;
  criterion?: SpeakingCriterionKey;
  text: string;
  timestampStart: number; // seconds
  timestampEnd?: number;
  createdAt: string;
}

export interface ExpertTranscriptLine {
  id: string;
  speaker: 'interlocutor' | 'candidate';
  startTime: number;
  endTime: number;
  text: string;
}

export interface AIFlag {
  id: string;
  type: string;
  message: string;
  timestampStart: number;
  timestampEnd?: number;
  severity: 'info' | 'warning' | 'error';
}

export interface WritingReviewDetail extends ReviewRequest {
  learnerResponse: string;
  caseNotes: string;
  aiDraftFeedback: string;
  aiSuggestedScores?: Partial<Record<WritingCriterionKey, number>>;
  modelAnswer?: string;
  existingDraft?: ExpertSavedDraft | null;
  permissions?: ExpertReviewActions;
  artifactStatus?: Record<string, ExpertArtifactState>;
}

export interface SpeakingReviewDetail extends ReviewRequest {
  audioUrl: string;
  transcriptLines: ExpertTranscriptLine[];
  roleCard: { role: string; setting: string; patient: string; task: string; background?: string };
  aiFlags: AIFlag[];
  aiSuggestedScores?: Partial<Record<SpeakingCriterionKey, number>>;
  existingDraft?: ExpertSavedDraft | null;
  permissions?: ExpertReviewActions;
  artifactStatus?: Record<string, ExpertArtifactState>;
}

export interface ExpertSavedDraft {
  version: number;
  state: string;
  scores: Record<string, number>;
  criterionComments: Record<string, string>;
  finalComment: string;
  anchoredComments: AnchoredComment[];
  timestampComments: TimestampComment[];
  scratchpad: string;
  checklistItems: ExpertChecklistItem[];
  savedAt: string;
}

export interface ReviewDraft {
  reviewRequestId: string;
  scores: Record<string, number>;
  criterionComments: Record<string, string>;
  finalComment: string;
  comments: AnchoredComment[] | TimestampComment[];
  scratchpad?: string;
  checklistItems?: ExpertChecklistItem[];
  savedAt: string;
  version?: number;
}

export interface CalibrationCase {
  id: string;
  title: string;
  profession: Profession;
  subTest: SubTest;
  type: SubmissionType;
  benchmarkScore: number;
  reviewerScore?: number;
  status: 'pending' | 'completed';
  createdAt: string;
}

export interface CalibrationArtifact {
  kind: string;
  title: string;
  content: string;
}

export interface CalibrationRubricEntry {
  criterion: string;
  benchmarkScore: number;
  rationale: string;
}

export interface CalibrationSubmission {
  reviewerId: string;
  reviewerName: string;
  reviewerScore: number;
  alignmentScore: number;
  disagreementSummary: string;
  notes: string;
  submittedScores: Record<string, number>;
  submittedAt: string;
}

export interface CalibrationCaseDetail extends CalibrationCase {
  benchmarkLabel: string;
  difficulty: string;
  artifacts: CalibrationArtifact[];
  benchmarkRubric: CalibrationRubricEntry[];
  referenceNotes: string[];
  existingSubmission?: CalibrationSubmission | null;
}

export interface CalibrationNote {
  id: string;
  type: 'completed' | 'comment' | 'system';
  message: string;
  caseId?: string;
  createdAt: string;
}

export interface ExpertMetrics {
  totalReviewsCompleted: number;
  draftReviews: number;
  averageSlaCompliance: number;
  averageCalibrationAlignment: number;
  reworkRate: number;
  averageTurnaroundHours: number;
}

export interface ExpertCompletionData {
  day: string;
  count: number;
}

export interface ExpertDashboardAvailability {
  timezone: string;
  todayKey: string;
  activeToday: boolean;
  todayWindow?: string | null;
  lastUpdatedAt?: string | null;
}

export interface ExpertDashboardActivity {
  timestamp: string;
  type: string;
  title: string;
  description?: string | null;
  route?: string | null;
}

export interface ExpertDashboardData {
  metrics: ExpertMetrics;
  activeAssignedReviews: number;
  overdueAssignedReviews: number;
  savedDraftCount: number;
  calibrationDueCount: number;
  assignedLearnerCount: number;
  generatedAt: string;
  availability: ExpertDashboardAvailability;
  assignedReviews: ReviewRequest[];
  resumeDrafts: ReviewRequest[];
  recentActivity: ExpertDashboardActivity[];
}

export interface ExpertQueueFilterMetadata {
  types: string[];
  professions: string[];
  priorities: string[];
  statuses: string[];
  confidenceBands: string[];
  assignmentStates: string[];
}

export interface ExpertScheduleDay {
  active: boolean;
  start: string;
  end: string;
}

export type ExpertSchedule = {
  timezone: string;
  days: Record<string, ExpertScheduleDay>;
  lastUpdatedAt?: string | null;
};

export interface ScheduleException {
  id: string;
  date: string;
  isBlocked: boolean;
  startTime?: string | null;
  endTime?: string | null;
  reason?: string | null;
  createdAt: string;
}

export interface LearnerProfile {
  id: string;
  name: string;
  profession: Profession;
  goalScore: string;
  examDate?: string;
  attemptsCount: number;
  joinedAt: string;
}

export interface SubTestScore {
  subTest: SubTest;
  latestScore?: number;
  latestGrade?: string;
  attempts: number;
}

export interface PriorReview {
  id: string;
  type: SubmissionType;
  reviewerName: string;
  date: string;
  overallComment: string;
}

export interface LearnerProfileExpanded extends LearnerProfile {
  totalReviews: number;
  subTestScores: SubTestScore[];
  priorReviews: PriorReview[];
  visibilityScope?: string;
}

export interface ExpertLearnerReviewContext {
  id: string;
  name: string;
  profession: Profession;
  goalScore: string;
  examDate?: string;
  reviewsInScope: number;
  subTestScores: SubTestScore[];
  priorReviews: PriorReview[];
}

export interface ExpertLearnerListItem {
  id: string;
  name: string;
  profession: Profession;
  goalScore: string;
  examDate?: string;
  reviewsInScope: number;
  subTests: string[];
  lastReviewId: string;
  lastReviewType: string;
  lastReviewState: string;
  lastReviewAt: string;
}

export interface ExpertLearnerDirectoryResponse {
  items: ExpertLearnerListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  lastUpdatedAt: string;
}

export interface ExpertReviewHistoryEntry {
  timestamp: string;
  action: string;
  actorName?: string | null;
  details?: string | null;
}

export interface ExpertReviewHistory {
  reviewRequestId: string;
  state: string;
  createdAt: string;
  completedAt?: string | null;
  draftVersionCount: number;
  entries: ExpertReviewHistoryEntry[];
}
