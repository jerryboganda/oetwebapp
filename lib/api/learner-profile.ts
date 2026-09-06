import { apiRequest, type ApiRecord } from './client';
import { titleCase, toSubTest } from './task-mappers';
import { toExamFamilyCode } from './result-mappers';
import { type UserProfile } from '../mock-data';

/**
 * Profile bootstrap, onboarding, tours, diagnostic overview, engagement.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export async function fetchUserProfile(): Promise<UserProfile> {
  const bootstrap = await apiRequest<ApiRecord>('/v1/me/bootstrap');
  const user = bootstrap.user;
  const goals = bootstrap.goals ?? {};

  return {
    id: user.userId,
    email: user.email,
    displayName: user.displayName,
    profession: titleCase(goals.professionId ?? user.activeProfessionId),
    examFamilyCode: toExamFamilyCode(goals.examFamilyCode),
    examDate: goals.targetExamDate ?? null,
    targetScores: {
      Writing: goals.targetScoresBySubtest?.writing ?? null,
      Speaking: goals.targetScoresBySubtest?.speaking ?? null,
      Reading: goals.targetScoresBySubtest?.reading ?? null,
      Listening: goals.targetScoresBySubtest?.listening ?? null,
    },
    previousAttempts: goals.previousAttemptSummary ?? 0,
    weakSubTests: (goals.weakSubtestSelfReport ?? []).map((value: string) => toSubTest(value)),
    studyHoursPerWeek: goals.studyHoursPerWeek ?? 0,
    targetCountry: goals.targetCountry ?? '',
    targetExamMode: (goals.targetExamMode ?? null) as string | null,
    confidenceLevel: (goals.confidenceLevel ?? null) as string | null,
    onboardingComplete: Boolean(bootstrap.onboarding?.completed),
    goalsComplete: Boolean(goals.submittedAt || goals.professionId),
    diagnosticComplete: Array.isArray(bootstrap.readiness?.subTests) && bootstrap.readiness.subTests.length > 0,
    createdAt: user.createdAt,
  };
}

export async function fetchMockSpeakingAccess(): Promise<{ requiresAiOnly: boolean; daysUntilExam: number | null }> {
  const data = await apiRequest<ApiRecord>('/v1/mocks/speaking-access');
  return {
    requiresAiOnly: Boolean(data.requiresAiOnly),
    daysUntilExam: data.daysUntilExam === null || data.daysUntilExam === undefined ? null : Number(data.daysUntilExam),
  };
}

export async function fetchOnboardingState(): Promise<{ completed: boolean; currentStep: number; stepCount: number; canSkip: boolean; checkpoint: string; resumeRoute: string; examDateRequired: boolean; }> {
  const data = await apiRequest<ApiRecord>('/v1/learner/onboarding/state');
  return {
    completed: Boolean(data.completed),
    currentStep: Number(data.currentStep ?? 1),
    stepCount: Number(data.stepCount ?? 4),
    canSkip: Boolean(data.canSkip),
    checkpoint: data.checkpoint ?? 'welcome',
    resumeRoute: data.resumeRoute ?? '/onboarding',
    examDateRequired: Boolean(data.examDateRequired),
  };
}

export async function startOnboarding(): Promise<void> {
  await apiRequest('/v1/learner/onboarding/start', { method: 'POST' });
}

export async function completeOnboarding(): Promise<void> {
  await apiRequest('/v1/learner/onboarding/complete', { method: 'POST' });
}

// ── Onboarding product-tour state ───────────────────────────────────────────
// Backed by /v1/onboarding/tours (GET + PATCH). Available to every authenticated
// role so learner, expert/tutor, and admin workspaces can persist their own tours.
export interface OnboardingTourState {
  onboardingVersion: number;
  role: string;
  lastSeenTourVersion: number;
  completed: {
    intro: boolean;
    dashboard: boolean;
    listening: boolean;
    reading: boolean;
    writing: boolean;
    speaking: boolean;
    admin: boolean;
    expert: boolean;
  };
  skippedTours: string[];
  dismissedTips: string[];
}

function toOnboardingTourState(data: ApiRecord): OnboardingTourState {
  const completed = (data.completed ?? {}) as ApiRecord;
  return {
    onboardingVersion: Number(data.onboardingVersion ?? 1),
    role: typeof data.role === 'string' ? data.role : 'learner',
    lastSeenTourVersion: Number(data.lastSeenTourVersion ?? 0),
    completed: {
      intro: Boolean(completed.intro),
      dashboard: Boolean(completed.dashboard),
      listening: Boolean(completed.listening),
      reading: Boolean(completed.reading),
      writing: Boolean(completed.writing),
      speaking: Boolean(completed.speaking),
      admin: Boolean(completed.admin),
      expert: Boolean(completed.expert),
    },
    skippedTours: Array.isArray(data.skippedTours) ? data.skippedTours.map(String) : [],
    dismissedTips: Array.isArray(data.dismissedTips) ? data.dismissedTips.map(String) : [],
  };
}

export async function fetchTourState(): Promise<OnboardingTourState> {
  const data = await apiRequest<ApiRecord>('/v1/onboarding/tours');
  return toOnboardingTourState(data);
}

export async function markTour(
  tourId: string,
  status: 'completed' | 'skipped' | 'dismissed',
  role?: string,
): Promise<OnboardingTourState> {
  const data = await apiRequest<ApiRecord>('/v1/onboarding/tours', {
    method: 'PATCH',
    body: JSON.stringify({ tourId, status, role }),
  });
  return toOnboardingTourState(data);
}

export interface DiagnosticOverviewResponse {
  subtests?: { subtest: string; estimatedDurationMinutes: number }[];
  estimatedTotalMinutes?: number;
  disclaimer?: string;
}

export async function fetchDiagnosticOverview(): Promise<DiagnosticOverviewResponse> {
  return apiRequest<DiagnosticOverviewResponse>('/v1/diagnostic/overview');
}

