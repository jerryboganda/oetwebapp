/**
 * Speaking module rebuild (2026-06-11 spec).
 *
 * Typed API client for the two-card Speaking exam (Intro → Card A → Card B).
 * Backed by `SpeakingExamEndpoints.cs`:
 *
 *   POST   /v1/speaking/exams
 *   GET    /v1/speaking/exams/{id}
 *   GET    /v1/speaking/exams/{id}/clock
 *   POST   /v1/speaking/exams/{id}/finish-intro
 *   POST   /v1/speaking/exams/{id}/start-card
 *   POST   /v1/speaking/exams/{id}/cancel
 *   POST   /v1/speaking/exams/{id}/technical-issue
 *   GET    /v1/speaking/exams/{id}/results
 *
 * MISSION CRITICAL: the learner-facing types here never include the roleplayer
 * (patient) card, the hidden card type, or any interlocutor field.
 */
import { apiClient } from '@/lib/api';

export type SpeakingExamMode = 'ai' | 'live_tutor';

export type SpeakingExamState =
  | 'intro'
  | 'prep_a'
  | 'active_a'
  | 'prep_b'
  | 'active_b'
  | 'completed'
  | 'cancelled'
  | 'expired';

/** Learner-safe candidate card (mirrors SpeakingSessionService.ProjectLearnerCard). */
export interface ExamCandidateCard {
  cardId: string;
  professionId: string;
  scenarioTitle: string;
  setting: string;
  candidateRole: string;
  interlocutorRole: string;
  patientName?: string | null;
  patientAge?: string | null;
  background: string;
  tasks: string[];
  allowedNotes: boolean;
  prepTimeSeconds: number;
  rolePlayTimeSeconds: number;
  difficulty: string;
  disclaimer: string;
  displayCardNumber?: number | null;
}

export interface SpeakingExamClock {
  stage: SpeakingExamState;
  serverNow: string;
  stageStartedAt?: string | null;
  stageEndsAt?: string | null;
  secondsRemaining?: number | null;
  expired: boolean;
}

export interface SpeakingExamDetail {
  examId: string;
  mode: SpeakingExamMode;
  state: SpeakingExamState;
  professionId: string;
  currentCardNumber: number;
  currentSessionId?: string | null;
  currentCard?: ExamCandidateCard | null;
  clock: SpeakingExamClock;
  completedAt?: string | null;
  mockAttemptId?: string | null;
  mockSectionId?: string | null;
}

export interface CreateSpeakingExamInput {
  mode: SpeakingExamMode;
  mockSetId?: string | null;
  professionId?: string | null;
  bookingId?: string | null;
  mockAttemptId?: string | null;
  mockSectionId?: string | null;
}

export interface SpeakingExamCriterionScore {
  score: number;
  maxScore: number;
  rationale: string;
  evidenceQuotes: string[];
}

export interface SpeakingExamAssessment {
  assessmentId: string;
  provider: string;
  modelId: string;
  criterionScores: Record<string, SpeakingExamCriterionScore>;
  estimatedScaledScore: number;
  readinessBand: string;
  overallSummary: string;
  confidenceBand: string;
  generatedAt: string;
  isAdvisory: boolean;
}

export interface SpeakingExamCardResult {
  cardNumber: number;
  sessionId: string;
  status: 'scored' | 'pending' | 'awaiting_tutor';
  assessment?: SpeakingExamAssessment | null;
}

export interface SpeakingExamResults {
  examId: string;
  mode: SpeakingExamMode;
  state: SpeakingExamState;
  overallStatus: 'scored' | 'pending' | 'awaiting_tutor';
  combinedScaledScore?: number | null;
  readinessBand?: string | null;
  cards: SpeakingExamCardResult[];
}

// ─────────────────────────────────────────────────────────────────────────────

export function createSpeakingExam(input: CreateSpeakingExamInput) {
  return apiClient.post<SpeakingExamDetail>('/v1/speaking/exams', input);
}

/** Creates (or resumes) the live-tutor exam for a PrivateSpeaking booking. */
export function createSpeakingExamFromBooking(bookingId: string) {
  return apiClient.post<SpeakingExamDetail>(
    `/v1/speaking/exams/from-booking/${encodeURIComponent(bookingId)}`,
    {},
  );
}

// ── Tutor-only view of a live-tutor exam (roleplayer cards + phase) ──────────

export interface SpeakingExamRoleplayerCard {
  cardNumber: number;
  setting: string;
  interlocutorRole: string;
  patientName?: string | null;
  patientAge?: string | null;
  patientBackground: string;
  patientTasks: string[];
  displayCardNumber?: number | null;
  cardTypeName?: string | null;
}

export interface SpeakingExamTutorView {
  examId: string;
  mode: SpeakingExamMode;
  state: SpeakingExamState;
  currentCardNumber: number;
  professionId: string;
  bookingId?: string | null;
  clock: SpeakingExamClock;
  cards: SpeakingExamRoleplayerCard[];
}

/** Tutor/expert: fetch both roleplayer cards + the live phase for an exam. */
export function getSpeakingExamTutorView(examId: string) {
  return apiClient.get<SpeakingExamTutorView>(
    `/v1/expert/speaking/exams/${encodeURIComponent(examId)}`,
  );
}

export function getSpeakingExam(examId: string) {
  return apiClient.get<SpeakingExamDetail>(`/v1/speaking/exams/${encodeURIComponent(examId)}`);
}

export function getSpeakingExamClock(examId: string) {
  return apiClient.get<SpeakingExamClock>(`/v1/speaking/exams/${encodeURIComponent(examId)}/clock`);
}

export function finishSpeakingExamIntro(examId: string) {
  return apiClient.post<SpeakingExamDetail>(
    `/v1/speaking/exams/${encodeURIComponent(examId)}/finish-intro`,
    {},
  );
}

export function startSpeakingExamCard(examId: string) {
  return apiClient.post<SpeakingExamDetail>(
    `/v1/speaking/exams/${encodeURIComponent(examId)}/start-card`,
    {},
  );
}

export function cancelSpeakingExam(examId: string) {
  return apiClient.post<SpeakingExamDetail>(
    `/v1/speaking/exams/${encodeURIComponent(examId)}/cancel`,
    {},
  );
}

export function reportSpeakingExamTechnicalIssue(examId: string, note?: string) {
  return apiClient.post<SpeakingExamDetail>(
    `/v1/speaking/exams/${encodeURIComponent(examId)}/technical-issue`,
    { note: note ?? null },
  );
}

export function getSpeakingExamResults(examId: string) {
  return apiClient.get<SpeakingExamResults>(`/v1/speaking/exams/${encodeURIComponent(examId)}/results`);
}
