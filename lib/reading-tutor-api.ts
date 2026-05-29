/**
 * Reading tutor API client (wave 2). Mirrors the .NET endpoints in
 * `ReadingTutorAdminEndpoints.cs` and the cohort rollup in
 * `ReadingAnalyticsAdminEndpoints.cs`.
 *
 * The privileged surface is mounted on BOTH the admin group
 * (`/v1/admin/reading`) and the expert group (`/v1/expert/reading`), so each
 * function takes an optional `area` argument (default `'admin'`) that selects
 * the route prefix. The learner-facing assignment read lives on `/v1/reading`.
 *
 * All HTTP goes through the shared `apiClient` so auth, CSRF, retries, and
 * timeout handling stay consistent. Field names match the backend DTOs 1:1
 * (the API serialises camelCase).
 */

import { apiClient } from './api';
import type { ReadingDistractorCategory, ReadingPartCode } from './reading-authoring-api';

// ── Route helpers ─────────────────────────────────────────────────────────

export type ReadingTutorArea = 'admin' | 'expert';

function tutorBase(area: ReadingTutorArea): string {
  return `/v1/${area}/reading`;
}

// ── Request bodies ──────────────────────────────────────────────────────────

export interface ReadingScoreOverrideInput {
  rawScore?: number | null;
  scaledScore?: number | null;
  reason: string;
}

export type ReadingRecalcScope = 'thisAttempt' | 'allAttemptsForPaper';

export interface ReadingRecalcInput {
  scope: ReadingRecalcScope;
  attemptId?: string | null;
}

export type ReadingFeedbackScope = string;

export interface ReadingFeedbackInput {
  scope: ReadingFeedbackScope;
  targetRef?: string | null;
  feedbackText: string;
}

export interface ReadingAssignmentCreateInput {
  assignedToUserId: string;
  paperId: string;
  kind: string;
  scopeJson?: string | null;
  note?: string | null;
  dueAt?: string | null;
}

export interface ReadingCohortAnalyticsInput {
  paperId: string;
  userIds: string[];
}

// ── Response DTOs (mirror the .NET records 1:1) ──────────────────────────────

export interface ReadingRecalcResult {
  recalculatedCount: number;
  skippedOverrideCount: number;
  totalConsidered: number;
}

export interface ReadingFeedbackDto {
  id: string;
  readingAttemptId: string;
  scope: string;
  targetRef: string | null;
  authorUserId: string;
  feedbackText: string;
  createdAt: string;
  updatedAt: string;
}

export interface ReadingAssignmentDto {
  id: string;
  assignedByUserId: string;
  assignedToUserId: string;
  paperId: string;
  kind: string;
  scopeJson: string | null;
  note: string | null;
  dueAt: string | null;
  completedAttemptId: string | null;
  status: string;
  createdAt: string;
  updatedAt: string;
}

export interface ReadingPrivilegedSection {
  partCode: string;
  rawScore: number;
  maxRawScore: number;
  accuracyPercent: number | null;
  correctCount: number;
  incorrectCount: number;
  unansweredCount: number;
}

export interface ReadingPrivilegedQuestion {
  questionId: string;
  partCode: string;
  displayOrder: number;
  questionType: string;
  stem: string;
  skillTag: string | null;
  userAnswer: unknown;
  isCorrect: boolean | null;
  pointsEarned: number;
  maxPoints: number;
  correctAnswer: unknown;
  explanationMarkdown: string | null;
  acceptedSynonyms: string[];
  selectedDistractorCategory: string | null;
  distractorRationale: unknown;
  missReason: string | null;
  flaggedForReview: boolean;
  elapsedMs: number | null;
  totalElapsedMs: number | null;
  answerRevisionCount: number;
}

export interface ReadingPrivilegedAttemptReview {
  attemptId: string;
  paperId: string;
  paperTitle: string;
  userId: string;
  status: string;
  mode: string;
  startedAt: string;
  submittedAt: string | null;
  gradedRawScore: number | null;
  gradedScaledScore: number | null;
  gradedGradeLetter: string;
  effectiveRawScore: number | null;
  effectiveScaledScore: number | null;
  effectiveGradeLetter: string;
  hasOverride: boolean;
  overrideRaw: number | null;
  overrideScaled: number | null;
  overrideReason: string | null;
  overriddenByUserId: string | null;
  overriddenAt: string | null;
  maxRawScore: number;
  sections: ReadingPrivilegedSection[];
  questions: ReadingPrivilegedQuestion[];
  flaggedQuestionIds: string[];
}

export interface ReadingCohortHardestQuestion {
  questionId: string;
  partCode: string;
  displayOrder: number;
  stem: string;
  answerCount: number;
  correctCount: number;
  correctRate: number;
}

export interface ReadingCohortDistractorRow {
  questionId: string;
  category: ReadingDistractorCategory;
  optionKey: string;
  selectedCount: number;
}

export interface ReadingCohortPartAverage {
  partCode: string;
  averageRawScore: number;
  averageAccuracyPercent: number;
  maxRawScore: number;
}

export interface ReadingCohortSkillAverage {
  skill: string;
  averageAccuracyPercent: number;
  questionCount: number;
}

export interface ReadingCohortStudent {
  userId: string;
  hasAttempt: boolean;
  rawScore: number | null;
  scaledScore: number | null;
  gradeLetter: string;
  rag: string;
  assignmentsAssigned: number;
  assignmentsCompleted: number;
}

export interface ReadingCohortAnalytics {
  paperId: string;
  studentCount: number;
  partAverages: ReadingCohortPartAverage[];
  skillAverages: ReadingCohortSkillAverage[];
  hardestQuestions: ReadingCohortHardestQuestion[];
  topDistractors: ReadingCohortDistractorRow[];
  students: ReadingCohortStudent[];
}

// Re-exported for convenience at tutor call sites.
export type { ReadingDistractorCategory, ReadingPartCode };

// ── Manual override ─────────────────────────────────────────────────────────

export const overrideReadingAttemptScore = (
  attemptId: string,
  body: ReadingScoreOverrideInput,
  area: ReadingTutorArea = 'admin',
) =>
  apiClient.post<ReadingPrivilegedAttemptReview>(
    `${tutorBase(area)}/attempts/${attemptId}/override`,
    body,
  );

export const clearReadingAttemptScoreOverride = (
  attemptId: string,
  area: ReadingTutorArea = 'admin',
) =>
  apiClient.delete<ReadingPrivilegedAttemptReview>(
    `${tutorBase(area)}/attempts/${attemptId}/override`,
  );

// ── Accepted-answer recalculation ────────────────────────────────────────────

export const recalcReadingPaper = (
  paperId: string,
  body: ReadingRecalcInput,
  area: ReadingTutorArea = 'admin',
) =>
  apiClient.post<ReadingRecalcResult>(
    `${tutorBase(area)}/papers/${paperId}/recalc`,
    body,
  );

// ── Privileged (non-redacted) attempt review ─────────────────────────────────

export const getPrivilegedReadingAttemptReview = (
  attemptId: string,
  area: ReadingTutorArea = 'admin',
) =>
  apiClient.get<ReadingPrivilegedAttemptReview>(
    `${tutorBase(area)}/attempts/${attemptId}`,
  );

// ── Feedback CRUD ─────────────────────────────────────────────────────────

export const listReadingAttemptFeedback = (
  attemptId: string,
  area: ReadingTutorArea = 'admin',
) =>
  apiClient.get<ReadingFeedbackDto[]>(
    `${tutorBase(area)}/attempts/${attemptId}/feedback`,
  );

export const createReadingAttemptFeedback = (
  attemptId: string,
  body: ReadingFeedbackInput,
  area: ReadingTutorArea = 'admin',
) =>
  apiClient.post<ReadingFeedbackDto>(
    `${tutorBase(area)}/attempts/${attemptId}/feedback`,
    body,
  );

export const updateReadingAttemptFeedback = (
  attemptId: string,
  feedbackId: string,
  body: ReadingFeedbackInput,
  area: ReadingTutorArea = 'admin',
) =>
  apiClient.put<ReadingFeedbackDto>(
    `${tutorBase(area)}/attempts/${attemptId}/feedback/${feedbackId}`,
    body,
  );

export const deleteReadingAttemptFeedback = (
  attemptId: string,
  feedbackId: string,
  area: ReadingTutorArea = 'admin',
) =>
  apiClient.delete<void>(
    `${tutorBase(area)}/attempts/${attemptId}/feedback/${feedbackId}`,
  );

// ── Assignment workflow ─────────────────────────────────────────────────────

export const createReadingAssignment = (
  body: ReadingAssignmentCreateInput,
  area: ReadingTutorArea = 'admin',
) =>
  apiClient.post<ReadingAssignmentDto>(
    `${tutorBase(area)}/assignments`,
    body,
  );

export const listReadingAssignments = (
  assignedToUserId: string,
  area: ReadingTutorArea = 'admin',
) =>
  apiClient.get<ReadingAssignmentDto[]>(
    `${tutorBase(area)}/assignments?assignedToUserId=${encodeURIComponent(assignedToUserId)}`,
  );

export const cancelReadingAssignment = (
  id: string,
  area: ReadingTutorArea = 'admin',
) =>
  apiClient.delete<void>(`${tutorBase(area)}/assignments/${id}`);

/** Learner-facing read of the caller's active Reading assignments. */
export const listMyReadingAssignments = () =>
  apiClient.get<ReadingAssignmentDto[]>('/v1/reading/assignments');

// ── Cohort analytics ────────────────────────────────────────────────────────

export const getReadingCohortAnalytics = (
  { paperId, userIds }: ReadingCohortAnalyticsInput,
  area: ReadingTutorArea = 'admin',
) => {
  const params = new URLSearchParams({ paperId });
  if (userIds.length > 0) params.set('userIds', userIds.join(','));
  return apiClient.get<ReadingCohortAnalytics>(
    `${tutorBase(area)}/analytics/cohort?${params.toString()}`,
  );
};
