import type { WeaknessDataPoint } from '../writing-analytics/types';
import type { SpeakingTask } from '../mock-data';
import { apiRequest, type ApiRecord } from './client';
import { normalizeRouteValues } from './route-normalizer';
import { mapSpeakingTask } from './task-mappers';
import { fetchUserProfile } from './learner-profile';
import { type UserProfile } from '../mock-data';

/**
 * Learner home surfaces + device check + profession + profile updates.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export interface ReadingHomeResponse {
  [key: string]: unknown;
}

export async function fetchReadingHome(): Promise<ReadingHomeResponse> {
  const data = await apiRequest<ReadingHomeResponse>('/v1/reading-papers/home');
  return normalizeRouteValues(data) as ReadingHomeResponse;
}

export interface ListeningHomeResponse {
  [key: string]: unknown;
}

export async function fetchListeningHome(): Promise<ListeningHomeResponse> {
  const data = await apiRequest<ListeningHomeResponse>('/v1/listening/home');
  return normalizeRouteValues(data) as ListeningHomeResponse;
}

export interface WritingHomeResponse {
  recommendedTask?: Record<string, unknown> & {
    id?: string;
    contentId?: string;
    title?: string;
    criteriaFocus?: string | string[];
    scenarioType?: string;
    profession?: string;
    time?: string;
    estimatedDurationMinutes?: number;
    difficulty?: string;
  };
  reviewCredits?: { available?: number };
  fullMockEntry?: { title?: string; route?: string; rationale?: string };
  actions?: unknown[];
  latestEvaluation?: unknown | null;
  criterionDrillLibrary?: unknown[];
  [key: string]: unknown;
}

export async function fetchWritingHome(): Promise<WritingHomeResponse> {
  const data = await apiRequest<WritingHomeResponse>('/v1/writing/home');
  return normalizeRouteValues(data) as WritingHomeResponse;
}

export interface WritingWeaknessAnalyticsResponse {
  generatedAt: string;
  windowDays: number;
  points: WeaknessDataPoint[];
}

export async function fetchWritingWeaknessData(options: { days?: number } = {}): Promise<WritingWeaknessAnalyticsResponse> {
  const params = new URLSearchParams();
  if (options.days !== undefined) {
    params.set('days', String(options.days));
  }

  return apiRequest<WritingWeaknessAnalyticsResponse>(`/v1/writing/analytics/weaknesses${params.size ? `?${params}` : ''}`);
}

export async function fetchSpeakingHome(): Promise<SpeakingHome> {
  const data = await apiRequest<ApiRecord>('/v1/speaking/home');
  const normalized = normalizeRouteValues(data) as ApiRecord;
  return {
    recommendedRolePlay: normalized.recommendedRolePlay ? mapSpeakingTask(normalized.recommendedRolePlay) : null,
    commonIssuesToImprove: Array.isArray(normalized.commonIssuesToImprove) ? normalized.commonIssuesToImprove : [],
    drillGroups: Array.isArray(normalized.drillGroups)
      ? normalized.drillGroups.map((group: ApiRecord) => ({
        id: String(group.id ?? group.title ?? 'drill-group'),
        title: String(group.title ?? 'Speaking drill group'),
        items: Array.isArray(group.items)
          ? group.items.map((item: ApiRecord) => ({
            id: String(item.id ?? item.route ?? item.title ?? 'drill'),
            title: String(item.title ?? 'Open drill'),
            description: item.description,
            route: String(item.route ?? '/speaking/selection'),
          }))
          : [],
      }))
      : [],
    pastAttempts: Array.isArray(normalized.pastAttempts)
      ? normalized.pastAttempts.map((attempt: ApiRecord) => ({
        attemptId: String(attempt.attemptId ?? ''),
        state: String(attempt.state ?? 'unknown'),
        scoreEstimate: attempt.scoreEstimate ?? null,
        route: String(attempt.route ?? '/speaking/selection'),
      }))
      : [],
    reviewCredits: {
      available: Number(normalized.reviewCredits?.available ?? 0),
      route: String(normalized.reviewCredits?.route ?? '/reviews'),
      billingRoute: normalized.reviewCredits?.billingRoute,
    },
    supportEntries: Array.isArray(normalized.supportEntries)
      ? normalized.supportEntries.map((entry: ApiRecord) => ({
        id: String(entry.id ?? entry.route ?? entry.title ?? 'support-entry'),
        title: String(entry.title ?? 'Speaking support'),
        description: entry.description,
        route: String(entry.route ?? '/speaking/selection'),
      }))
      : [],
    featuredTasks: Array.isArray(normalized.featuredTasks) ? normalized.featuredTasks.map(mapSpeakingTask) : [],
    latestEvaluation: normalized.latestEvaluation ?? null,
    tips: Array.isArray(normalized.tips) ? normalized.tips : [],
    dashboardRoute: normalized.dashboardRoute,
    historyRoute: normalized.historyRoute,
    writingLibraryRoute: normalized.writingLibraryRoute,
    writingTaskRoute: normalized.writingTaskRoute,
    readingTaskRoute: normalized.readingTaskRoute,
    listeningTaskRoute: normalized.listeningTaskRoute,
  };
}

export interface MocksHomeResponse {
  reports?: Record<string, unknown>[];
  resumableAttempts?: unknown[];
  recommendedNextMock?: {
    id?: string;
    title?: string;
    rationale?: string;
    route?: string;
    latestOverallScore?: string | null;
    latestOverallGrade?: string | null;
    trend?: string | null;
    readiness?: {
      tier?: string;
      message?: string;
      passThreshold?: number;
      overallScore?: number;
    } | null;
  } | null;
  purchasedMockReviews?: unknown;
  collections?: { fullMocks?: unknown[]; subTestMocks?: unknown[] };
  emptyState?: { title?: string; description?: string; route?: string } | null;
  learnerProfession?: string | null;
  availableProfessions?: { id: string; label: string }[];
  scoreGuarantee?: unknown | null;
  cohortPercentile?: unknown | null;
  [key: string]: unknown;
}

export async function fetchMocksHome(): Promise<MocksHomeResponse> {
  const data = await apiRequest<MocksHomeResponse>('/v1/mocks');
  return normalizeRouteValues(data) as MocksHomeResponse;
}

export interface SpeakingDeviceCheckResponse {
  [key: string]: unknown;
}

export async function postSpeakingDeviceCheck(payload: {
  microphoneGranted: boolean;
  networkStable: boolean;
  deviceType?: string;
  taskId?: string;
  noiseLevel?: number;
  noiseAcceptable?: boolean;
}): Promise<SpeakingDeviceCheckResponse> {
  return apiRequest<SpeakingDeviceCheckResponse>('/v1/speaking/device-checks', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function setActiveProfession(professionId: string): Promise<void> {
  if (!professionId || typeof professionId !== 'string') {
    throw new Error('professionId is required');
  }
  await apiRequest('/v1/settings/profile', {
    method: 'PATCH',
    body: JSON.stringify({ values: { professionId } }),
  });
}

export async function updateUserProfile(updates: Partial<UserProfile>): Promise<UserProfile> {
  const profileValues: ApiRecord = {};
  if (updates.displayName) profileValues.displayName = updates.displayName;
  if (updates.email) profileValues.email = updates.email;
  if (updates.profession) profileValues.profession = updates.profession.toLowerCase();
  if (Object.keys(profileValues).length > 0) {
    await apiRequest('/v1/settings/profile', { method: 'PATCH', body: JSON.stringify({ values: profileValues }) });
  }

  const goalValues: ApiRecord = {};
  if (updates.profession) goalValues.professionId = updates.profession.toLowerCase();
  if (updates.examFamilyCode) goalValues.examFamilyCode = updates.examFamilyCode;
  if (updates.examDate !== undefined) goalValues.targetExamDate = updates.examDate;
  if (updates.studyHoursPerWeek !== undefined) goalValues.studyHoursPerWeek = updates.studyHoursPerWeek;
  if (updates.targetCountry !== undefined) goalValues.targetCountry = updates.targetCountry;
  if (updates.targetScores) {
    goalValues.targetWritingScore = updates.targetScores.Writing;
    goalValues.targetSpeakingScore = updates.targetScores.Speaking;
    goalValues.targetReadingScore = updates.targetScores.Reading;
    goalValues.targetListeningScore = updates.targetScores.Listening;
  }
  if (updates.previousAttempts !== undefined) goalValues.previousAttempts = updates.previousAttempts;
  if (updates.weakSubTests) goalValues.weakSubtests = updates.weakSubTests.map((value) => value.toLowerCase());
  if (updates.targetExamMode !== undefined) goalValues.targetExamMode = updates.targetExamMode;
  if (updates.confidenceLevel !== undefined) goalValues.confidenceLevel = updates.confidenceLevel;

  if (Object.keys(goalValues).length > 0) {
    await apiRequest('/v1/learner/goals', { method: 'PATCH', body: JSON.stringify(goalValues) });
  }

  return fetchUserProfile();
}



export interface SpeakingHomeAction {
  id: string;
  title: string;
  description?: string;
  route: string;
}

export interface SpeakingHomeDrillGroup {
  id: string;
  title: string;
  items: SpeakingHomeAction[];
}

export interface SpeakingHomeAttempt {
  attemptId: string;
  state: string;
  scoreEstimate?: string | null;
  route: string;
}

export interface SpeakingHomeReviewCredits {
  available: number;
  route: string;
  billingRoute?: string;
}

export interface SpeakingHome {
  recommendedRolePlay?: SpeakingTask | null;
  commonIssuesToImprove: string[];
  drillGroups: SpeakingHomeDrillGroup[];
  pastAttempts: SpeakingHomeAttempt[];
  reviewCredits: SpeakingHomeReviewCredits;
  supportEntries: SpeakingHomeAction[];
  featuredTasks: SpeakingTask[];
  latestEvaluation?: ApiRecord | null;
  tips: string[];
  dashboardRoute?: string;
  historyRoute?: string;
  writingLibraryRoute?: string;
  writingTaskRoute?: string;
  readingTaskRoute?: string;
  listeningTaskRoute?: string;
}
