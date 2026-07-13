'use client';

import { useContext, useEffect, useRef } from 'react';
import { AuthContext } from '@/contexts/auth-context';
import { useAnalytics } from '@/hooks/use-analytics';
import { ApiError, isApiError } from '@/lib/api';
import {
  useDashboardHome as useDashboardHomeQuery,
  useEngagement,
  useReadiness,
  useStudyPlan,
  useUserProfileQuery,
} from '@/lib/query/hooks';
import type { ReadinessData, StudyPlanTask, UserProfile } from '@/lib/mock-data';

export interface EngagementData {
  currentStreak: number;
  longestStreak: number;
  lastPracticeDate: string | null;
  totalPracticeMinutes: number;
  totalPracticeSessions: number;
  avgSessionMinutes: number;
  weeklyActivity: { day: string; active: boolean }[];
  streakFreezeAvailable: boolean;
  streakFreezeUsedThisWeek: boolean;
}

export interface DashboardHomeData {
  home: Record<string, any> | null;
  profile: UserProfile | null;
  readiness: ReadinessData | null;
  tasks: StudyPlanTask[];
  engagement: EngagementData | null;
  loadedAt: string | null;
}

const emptyData: DashboardHomeData = {
  home: null,
  profile: null,
  readiness: null,
  tasks: [],
  engagement: null,
  loadedAt: null,
};

function toErrorMessage(error: unknown): string {
  if (isApiError(error)) {
    return error.userMessage;
  }

  if (error && typeof error === 'object') {
    if ('userMessage' in error && typeof error.userMessage === 'string') {
      return error.userMessage;
    }

    if ('message' in error && typeof error.message === 'string') {
      return error.message;
    }
  }

  return 'Something went wrong. Please try again.';
}

function isAuthFailure(error: unknown): error is ApiError {
  return isApiError(error) && (error.status === 401 || error.status === 403 || error.code === 'not_authenticated' || error.code === 'unauthorized' || error.code === 'forbidden');
}

export function useDashboardHome() {
  const { track } = useAnalytics();
  const authContext = useContext(AuthContext);
  const authLoading = authContext?.loading ?? false;
  const isAuthenticated = authContext?.isAuthenticated ?? true;
  const signOut = authContext?.signOut;
  const userId = authContext?.user?.userId ?? 'current';
  const enabled = !authLoading && isAuthenticated;
  const queryOptions = {
    enabled,
    retry: (failureCount: number, error: Error) => !isAuthFailure(error) && failureCount < 1,
  };
  const tasksQuery = useStudyPlan(userId, queryOptions);
  const readinessQuery = useReadiness(userId, queryOptions);
  const profileQuery = useUserProfileQuery(userId, queryOptions);
  const homeQuery = useDashboardHomeQuery(userId, queryOptions);
  const engagementQuery = useEngagement(userId, queryOptions);
  const queries = [tasksQuery, readinessQuery, profileQuery, homeQuery, engagementQuery];
  const firstError = queries.find((query) => query.error)?.error ?? null;
  const hasAuthFailure = queries.some((query) => isAuthFailure(query.error));
  const handledAuthFailureFor = useRef<string | null>(null);
  const trackedReadinessFor = useRef<string | null>(null);

  useEffect(() => {
    if (!hasAuthFailure || !signOut || handledAuthFailureFor.current === userId) return;
    handledAuthFailureFor.current = userId;
    void Promise.resolve(signOut()).catch(() => {
      // Auth guards will re-evaluate even when the best-effort sign-out request fails.
    });
  }, [hasAuthFailure, signOut, userId]);

  const allSuccessful = enabled && queries.every((query) => query.isSuccess);
  useEffect(() => {
    if (!allSuccessful || trackedReadinessFor.current === userId) return;
    trackedReadinessFor.current = userId;
    track('readiness_viewed');
  }, [allSuccessful, track, userId]);

  const rawEngagement = (engagementQuery.data ?? {}) as Partial<EngagementData>;
  const engagement: EngagementData = {
    currentStreak: rawEngagement.currentStreak ?? 0,
    longestStreak: rawEngagement.longestStreak ?? 0,
    lastPracticeDate: rawEngagement.lastPracticeDate ?? null,
    totalPracticeMinutes: rawEngagement.totalPracticeMinutes ?? 0,
    totalPracticeSessions: rawEngagement.totalPracticeSessions ?? 0,
    avgSessionMinutes: rawEngagement.avgSessionMinutes ?? 0,
    weeklyActivity: rawEngagement.weeklyActivity ?? [],
    streakFreezeAvailable: rawEngagement.streakFreezeAvailable ?? false,
    streakFreezeUsedThisWeek: rawEngagement.streakFreezeUsedThisWeek ?? false,
  };
  const latestUpdate = Math.max(...queries.map((query) => query.dataUpdatedAt), 0);
  const data: DashboardHomeData = !isAuthenticated
    ? emptyData
    : {
        home: homeQuery.data ?? null,
        profile: profileQuery.data ?? null,
        readiness: readinessQuery.data ?? null,
        tasks: tasksQuery.data ?? [],
        engagement,
        loadedAt: latestUpdate > 0 ? new Date(latestUpdate).toISOString() : null,
      };
  const status = authLoading || hasAuthFailure || (enabled && queries.some((query) => query.isPending))
    ? 'loading'
    : firstError
      ? 'partial'
      : 'success';
  const reload = async () => {
    if (!enabled) return;
    await Promise.all(queries.map((query) => query.refetch()));
  };

  return {
    data,
    error: firstError ? toErrorMessage(firstError) : null,
    reload,
    status,
  };
}
