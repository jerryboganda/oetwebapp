/**
 * Typed API client for Speaking analytics (Phase 6, plan section B.6).
 *
 * Backs three dashboards:
 *   - Learner:  `LearnerSpeakingAnalyticsDashboard`
 *   - Teacher:  `TeacherClassDashboard`
 *   - Admin:    `/app/admin/speaking/analytics`
 *
 * Wraps the shared `apiClient` from `lib/api.ts` so retry, CSRF, and
 * Authorization headers stay consistent. Do NOT fold these into
 * `lib/api.ts` — that file is owned by the core team and edits there
 * would conflict with parallel work.
 */
import { apiClient } from '@/lib/api';

// ─────────────────────────────────────────────────────────────────────────────
// Learner analytics
// ─────────────────────────────────────────────────────────────────────────────

export interface SpeakingCriterionTrendPoint {
  /** ISO date (week-start) — YYYY-MM-DD. */
  date: string;
  /** Stable criterion code (e.g. `intelligibility`, `relationshipBuilding`). */
  criterion: string;
  score: number;
}

export interface LearnerSpeakingAnalytics {
  estimatedBand: string;
  currentScaled: number;
  sessionCount: number;
  criterionTrends: SpeakingCriterionTrendPoint[];
  avgRolePlayLengthSeconds: number;
  speakingSpeedWpm: number;
  recurringIssues: string[];
  readinessStatus: string;
  weakestCriterion: string;
  strongestCriterion: string;
}

export async function fetchLearnerSpeakingAnalytics(): Promise<LearnerSpeakingAnalytics> {
  return apiClient.get<LearnerSpeakingAnalytics>('/v1/speaking/analytics/me');
}

// ─────────────────────────────────────────────────────────────────────────────
// Teacher / class analytics
// ─────────────────────────────────────────────────────────────────────────────

export interface SpeakingClassIssue {
  code: string;
  count: number;
}

export interface ClassSpeakingAnalytics {
  avgEstimatedBand: number;
  weakestCriterionAcrossClass: string;
  commonIssues: SpeakingClassIssue[];
  sessionVolume7d: number;
  tutorActivityCount: number;
  totalLearners: number;
  totalSessions30d: number;
}

export interface TutorConsistencyAnalytics {
  tutorId: string | null;
  meanAbsoluteErrorVsGold: number;
  meanAbsoluteErrorVsAi: number;
  calibrationSamples: number;
  tutorAssessmentsScored: number;
}

export async function fetchClassSpeakingAnalytics(opts?: {
  cohortId?: string | null;
  professionId?: string | null;
}): Promise<ClassSpeakingAnalytics> {
  const params = new URLSearchParams();
  if (opts?.cohortId) params.set('cohortId', opts.cohortId);
  if (opts?.professionId) params.set('professionId', opts.professionId);
  const query = params.toString();
  return apiClient.get<ClassSpeakingAnalytics>(
    `/v1/expert/speaking/analytics/class${query ? `?${query}` : ''}`,
  );
}

export async function fetchTutorConsistency(opts?: {
  tutorId?: string | null;
}): Promise<TutorConsistencyAnalytics> {
  const params = new URLSearchParams();
  if (opts?.tutorId) params.set('tutorId', opts.tutorId);
  const query = params.toString();
  return apiClient.get<TutorConsistencyAnalytics>(
    `/v1/expert/speaking/analytics/tutor-consistency${query ? `?${query}` : ''}`,
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Admin content difficulty
// ─────────────────────────────────────────────────────────────────────────────

export interface SpeakingCardDifficulty {
  rolePlayCardId: string;
  title: string;
  attempts: number;
  completionRate: number;
  avgScaledScore: number;
  avgTimeOnCardSeconds: number;
}

export interface SpeakingContentDifficultyAnalytics {
  cards: SpeakingCardDifficulty[];
}

export async function fetchSpeakingContentDifficulty(opts?: {
  professionId?: string | null;
}): Promise<SpeakingContentDifficultyAnalytics> {
  const params = new URLSearchParams();
  if (opts?.professionId) params.set('professionId', opts.professionId);
  const query = params.toString();
  return apiClient.get<SpeakingContentDifficultyAnalytics>(
    `/v1/admin/speaking/analytics/content-difficulty${query ? `?${query}` : ''}`,
  );
}
