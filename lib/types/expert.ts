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
  status: ReviewStatus;
  contentId?: string;
  attemptId?: string;
  createdAt: string;
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
}

export interface SpeakingReviewDetail extends ReviewRequest {
  audioUrl: string;
  transcriptLines: ExpertTranscriptLine[];
  roleCard: { role: string; setting: string; patient: string; task: string; background?: string };
  aiFlags: AIFlag[];
  aiSuggestedScores?: Partial<Record<SpeakingCriterionKey, number>>;
}

export interface ReviewDraft {
  reviewRequestId: string;
  scores: Record<string, number>;
  criterionComments: Record<string, string>;
  finalComment: string;
  comments: AnchoredComment[] | TimestampComment[];
  savedAt: string;
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

export interface CalibrationNote {
  id: string;
  type: 'completed' | 'comment' | 'system';
  message: string;
  caseId?: string;
  createdAt: string;
}

export interface ExpertMetrics {
  totalReviewsCompleted: number;
  averageSlaCompliance: number;
  averageCalibrationAlignment: number;
  reworkRate: number;
}

export interface ExpertCompletionData {
  day: string;
  count: number;
}

export interface ExpertScheduleDay {
  active: boolean;
  start: string;
  end: string;
}

export type ExpertSchedule = {
  timezone: string;
  days: Record<string, ExpertScheduleDay>;
};

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
}
