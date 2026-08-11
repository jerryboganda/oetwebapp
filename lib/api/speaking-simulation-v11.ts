import { apiClient } from '@/lib/api';

export const SPEAKING_SIMULATION_V11_DISCLAIMER =
  'AI Estimated Practice Score — not an official OET result';

export type SpeakingSimulationV11AssessmentStatus =
  | 'Pending'
  | 'Complete'
  | 'TechnicalReview'
  | 'Invalid';

export interface SpeakingSimulationV11Evidence {
  evidenceType: string;
  evidenceStatus: 'supported' | 'unsupported' | string;
  primaryCriterionCode: string;
  turnNumber: number | null;
  quoteText: string;
  startMs: number | null;
  endMs: number | null;
  finding: string | null;
  action: string | null;
  confidenceLabel: 'high' | 'medium' | 'low' | string;
  confidenceScore: number | null;
  sourceTranscriptId: string | null;
  sourceRecordingId: string | null;
  isPrimary: boolean;
}

export interface SpeakingSimulationV11Criterion {
  criterionCode: string;
  label: string;
  weight: number;
  rawScore: number;
  weightedScore: number;
  scoreBand: string;
  rationale: string;
  evidence: SpeakingSimulationV11Evidence[];
  strength: string | null;
  weakness: string | null;
  action: string | null;
  confidenceLabel: string;
  confidenceScore: number | null;
}

export interface SpeakingSimulationV11TaskResult {
  taskNumber: number;
  status: string;
  evidence: string | null;
}

export interface SpeakingSimulationV11TimelineItem {
  label: string;
  startMs: number | null;
  endMs: number | null;
  note: string | null;
}

export interface SpeakingSimulationV11Alternative {
  originalQuote: string;
  betterAlternative: string;
  turnNumber: number | null;
  startMs: number | null;
  endMs: number | null;
}

export interface SpeakingSimulationV11PracticePlanItem {
  focus: string;
  action: string;
  frequency: string;
  successMeasure: string;
}

export interface SpeakingSimulationV11CardBreakdown {
  cardSlot: string;
  speakingSessionId: string;
  assessmentId: string;
  estimatedPracticeScore: number | null;
  scoreRangeLow: number | null;
  scoreRangeHigh: number | null;
  confidenceLabel: string;
  confidenceScore: number | null;
  criteria: SpeakingSimulationV11Criterion[];
  strengths: string[];
  weaknesses: string[];
  taskMap: SpeakingSimulationV11TaskResult[];
  timeline: SpeakingSimulationV11TimelineItem[];
  languageAnalysis: Record<string, unknown>;
  timeManagement: Record<string, unknown>;
  topFive: string[];
  betterAlternatives: SpeakingSimulationV11Alternative[];
  tips: string[];
  practicePlan: SpeakingSimulationV11PracticePlanItem[];
  sourceTranscriptId: string | null;
  sourceRecordingId: string | null;
  cardVersion: string | null;
}

export interface SpeakingSimulationV11AssessmentReport {
  assessmentId: string;
  assessmentKind: 'card' | 'combined' | string;
  cardSlot: 'a' | 'b' | 'standalone' | 'combined' | string;
  specVersion: string;
  rubricVersion: string;
  calibrationVersion: string;
  graphDisclaimer: string;
  estimatedPracticeScore: number | null;
  scoreRangeLow: number | null;
  scoreRangeHigh: number | null;
  confidenceLabel: string;
  confidenceScore: number | null;
  overallSummary: string | null;
  criteria: SpeakingSimulationV11Criterion[];
  cardBreakdowns: SpeakingSimulationV11CardBreakdown[];
  strengths: string[];
  weaknesses: string[];
  taskMap: SpeakingSimulationV11TaskResult[];
  timeline: SpeakingSimulationV11TimelineItem[];
  languageAnalysis: Record<string, unknown>;
  timeManagement: Record<string, unknown>;
  topFive: string[];
  betterAlternatives: SpeakingSimulationV11Alternative[];
  tips: string[];
  practicePlan: SpeakingSimulationV11PracticePlanItem[];
  sourceTranscriptId: string | null;
  sourceRecordingId: string | null;
  cardVersion: string | null;
  generatedAt: string;
}

export interface SpeakingSimulationV11AssessmentResponse {
  assessmentId: string;
  status: SpeakingSimulationV11AssessmentStatus | string;
  assessmentKind: 'card' | 'combined' | string;
  cardSlot: string;
  estimatedPracticeScore: number | null;
  scoreRangeLow: number | null;
  scoreRangeHigh: number | null;
  graphDisclaimer: string;
  confidenceLabel: string;
  confidenceScore: number | null;
  report: SpeakingSimulationV11AssessmentReport | null;
  technicalReviewCode: string | null;
  generatedAt: string;
}

export interface SpeakingSimulationV11LearnerTutorOverride {
  overrideId: string;
  assessmentId: string;
  estimatedPracticeScore: number;
  scoreRangeLow: number;
  scoreRangeHigh: number;
  reason: string;
  overrideReportJson: string;
  createdAt: string;
}

export function runSpeakingSimulationV11Assessment(sessionId: string) {
  return apiClient.post<SpeakingSimulationV11AssessmentResponse>(
    `/v1/speaking/sessions/${encodeURIComponent(sessionId)}/v1.1-assess`,
    {},
  );
}

export function getSpeakingSimulationV11Assessment(sessionId: string) {
  return apiClient.get<SpeakingSimulationV11AssessmentResponse>(
    `/v1/speaking/sessions/${encodeURIComponent(sessionId)}/v1.1-assessment`,
  );
}

export function runSpeakingSimulationV11CombinedAssessment(examId: string) {
  return apiClient.post<SpeakingSimulationV11AssessmentResponse>(
    `/v1/speaking/exams/${encodeURIComponent(examId)}/v1.1-combined-assess`,
    {},
  );
}

export function getSpeakingSimulationV11CombinedAssessment(examId: string) {
  return apiClient.get<SpeakingSimulationV11AssessmentResponse>(
    `/v1/speaking/exams/${encodeURIComponent(examId)}/v1.1-combined-report`,
  );
}

export function speakingSimulationV11AudioPath(sessionId: string, recordingId: string) {
  return `/v1/speaking/sessions/${encodeURIComponent(sessionId)}/v1.1-audio/${encodeURIComponent(recordingId)}`;
}

export function getSpeakingSimulationV11TutorOverride(sessionId: string) {
  return apiClient.get<SpeakingSimulationV11LearnerTutorOverride>(
    `/v1/speaking/sessions/${encodeURIComponent(sessionId)}/v1.1-tutor-override`,
  );
}
