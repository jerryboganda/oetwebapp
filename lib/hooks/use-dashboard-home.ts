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

function toErrorMessage(error: unknown, fallbackArea = 'dashboard data'): string {
  if (isApiError(error)) {
    if (error.userMessage && !error.userMessage.toLowerCase().includes('something went wrong')) {
      return error.userMessage;
    }
  }

  if (error && typeof error === 'object') {
    if ('userMessage' in error && typeof error.userMessage === 'string' && !error.userMessage.toLowerCase().includes('something went wrong')) {
      return error.userMessage;
    }

    if ('message' in error && typeof error.message === 'string' && !error.message.toLowerCase().includes('something went wrong')) {
      return error.message;
    }
  }

  return `Unable to load ${fallbackArea}. Please tap Retry or check your connection.`;
}

function isAuthFailure(error: unknown): error is ApiError {
  return isApiError(error) && (error.status === 401 || error.status === 403 || error.code === 'not_authenticated' || error.code === 'unauthorized' || error.code === 'forbidden');
}

function describeQueryError(queries: Array<{ name: string; error: unknown }>): string | null {
  const failed = queries.filter((q) => q.error != null);
  if (failed.length === 0) return null;

  const failureNames = failed.map((q) => q.name);
  const primary = failed[0]!;
  const rawMessage = toErrorMessage(primary.error, primary.name.toLowerCase());

  if (failed.length === 1) {
    return `${primary.name}: ${rawMessage}`;
  }

  return `Unable to load ${failureNames.join(' and ').toLowerCase()}. Your course access is unaffected — tap Retry to refresh.`;
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

  const namedQueries = [
    { name: 'Study Plan', query: tasksQuery },
    { name: 'Readiness Metrics', query: readinessQuery },
    { name: 'Candidate Profile', query: profileQuery },
    { name: 'Dashboard Highlights', query: homeQuery },
    { name: 'Practice Streak', query: engagementQuery },
  ];

  const queries = namedQueries.map((nq) => nq.query);
  const firstError = queries.find((query) => query.error)?.error ?? null;
  const hasAuthFailure = queries.some((query) => isAuthFailure(query.error));
  const handledAuthFailureFor = useRef<string | null>(null);
  const trackedReadinessFor = useRef<string | null>(null);

  const loggedErrorsRef = useRef<Set<string>>(new Set());

  // Log specific errors for telemetry and actionable debugging (deduplicated)
  useEffect(() => {
    const failed = namedQueries.filter((nq) => nq.query.error != null);
    if (failed.length > 0) {
      for (const f of failed) {
        const errorKey = `${f.name}:${String((f.query.error as any)?.message ?? f.query.error)}`;
        if (!loggedErrorsRef.current.has(errorKey)) {
          loggedErrorsRef.current.add(errorKey);
          console.error(`[CandidateDashboard] Query '${f.name}' failed for user ${userId}:`, f.query.error);
        }
      }
    }
  }, [namedQueries, userId]);

  useEffect(() => {
    if (!hasAuthFailure || !signOut || handledAuthFailureFor.current === userId) return;
    handledAuthFailureFor.current = userId;
    void Promise.resolve(signOut()).catch(() => {
      // Auth guards will re-evaluate even when the best-effort sign-out request fails.
    });
  }, [hasAuthFailure, signOut, userId]);

  // The hero, next action, and study-plan surfaces only need these three
  // responses. Readiness and engagement are below the fold and can hydrate
  // independently so a slow supplemental endpoint cannot hold the first
  // meaningful dashboard paint behind the full five-request fan-out.
  const criticalQueries = [tasksQuery, profileQuery, homeQuery];
  const criticalPending = enabled && criticalQueries.some((query) => query.isPending);
  const criticalError = criticalQueries.find((query) => query.error)?.error ?? null;
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
  const status = authLoading || hasAuthFailure || criticalPending
    ? 'loading'
    : criticalError
      ? 'partial'
      : 'success';
  const reload = async () => {
    if (!enabled) return;
    await Promise.all(queries.map((query) => query.refetch()));
  };

  const actionableErrorMessage = describeQueryError(
    namedQueries.map((nq) => ({ name: nq.name, error: nq.query.error })),
  );

  return {
    data,
    error: actionableErrorMessage ?? (firstError ? toErrorMessage(firstError) : null),
    reload,
    status,
  };
}
