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
import type { AiAssessment, SpeakingIntelligibilityEvidence } from '@/lib/api/speaking-assessments';

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
  liveRoomId?: string | null;
  /** Consent recorded at the intro (before prep_a); child sessions inherit it. */
  consentAccepted: boolean;
  /** False = recorder fallback: each active card is recorded and uploaded to its child session. */
  liveVoiceAvailable: boolean;
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
  grade?: string | null;
  scoreLabel?: string | null;
  /** What Intelligibility was judged from: the recording, or the transcript only (limited evidence). */
  intelligibilityEvidence?: SpeakingIntelligibilityEvidence | null;
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
  /** Reported score: 0–500 in steps of 10. */
  combinedScaledScore?: number | null;
  readinessBand?: string | null;
  cards: SpeakingExamCardResult[];
  /** OET letter for `combinedScaledScore`; there is no B+. */
  grade?: string | null;
  /** `provisional` until the graders involved were calibrated. Missing = provisional. */
  scoreLabel?: string | null;
  /**
   * The ONE judgement of both role-plays together (owner spec 4 Oct 2026): a single set of nine criterion scores, one
   * score out of 500 and one grade, with the coaching report and what Intelligibility was judged from.
   */
  combinedAssessment?: AiAssessment | null;
  /**
   * `ready` (the combined result is in `combinedAssessment`), `pending` (both cards graded, the whole test is being
   * judged), `failed` (try again, no charge) or `legacy` (an older exam keeps its averaged number). Missing for a
   * tutor exam, or while a card is still being graded.
   */
  combinedState?: 'ready' | 'pending' | 'failed' | 'legacy' | null;
}

// ─────────────────────────────────────────────────────────────────────────────

/** "Try again" for the combined judgement of a Full Mock that failed: re-queues it, no charge. */
export function retrySpeakingExamCombinedAssessment(examId: string) {
  return apiClient.post<{ combinedState: string }>(
    `/v1/speaking/exams/${encodeURIComponent(examId)}/combined-assess`,
    {},
  );
}

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

/** Creates (or resumes) the assigned tutor's live exam for a booking. */
export function createSpeakingExamFromBookingAsTutor(bookingId: string) {
  return apiClient.post<SpeakingExamDetail>(
    `/v1/expert/speaking/exams/from-booking/${encodeURIComponent(bookingId)}`,
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
  currentCardId?: string | null;
  liveRoomId?: string | null;
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

/** Rules + consent at the intro, before prep_a. Records account consents server-side too. */
export function recordSpeakingExamConsent(examId: string) {
  return apiClient.post<{ consentAccepted: boolean }>(
    `/v1/speaking/exams/${encodeURIComponent(examId)}/consent`,
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
