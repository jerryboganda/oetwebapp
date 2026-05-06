'use client';

import { useContext, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { AuthContext } from '@/contexts/auth-context';
import { useAnalytics } from '@/hooks/use-analytics';
import { ApiError, fetchDashboardHome, fetchEngagement, fetchReadiness, fetchStudyPlan, fetchUserProfile, isApiError } from '@/lib/api';
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

interface DashboardHomeState {
  data: DashboardHomeData;
  error: string | null;
  status: 'loading' | 'success' | 'error' | 'partial';
}

const initialState: DashboardHomeState = {
  data: {
    home: null,
    profile: null,
    readiness: null,
    tasks: [],
    engagement: null,
    loadedAt: null,
  },
  error: null,
  status: 'loading',
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
  const [state, setState] = useState<DashboardHomeState>(initialState);

  async function load() {
    if (authLoading || !isAuthenticated) {
      return;
    }

    setState((current) => ({
      ...current,
      error: null,
      status: 'loading',
    }));

    try {
      const [tasksResult, readinessResult, profileResult, homeResult, engagementResult] = await Promise.allSettled([
        fetchStudyPlan(),
        fetchReadiness(),
        fetchUserProfile(),
        fetchDashboardHome(),
        fetchEngagement(),
      ]);

      const settledResults = [tasksResult, readinessResult, profileResult, homeResult, engagementResult];
      const hasAuthFailure = settledResults.some((result) => result.status === 'rejected' && isAuthFailure(result.reason));
      if (hasAuthFailure) {
        if (signOut) {
          try {
            await signOut();
          } catch {
            // If sign-out fails, stop here so auth guards can re-evaluate state.
          }
        }
        return;
      }

      const taskError = tasksResult.status === 'rejected' ? tasksResult.reason : null;
      const readinessError = readinessResult.status === 'rejected' ? readinessResult.reason : null;
      const profileError = profileResult.status === 'rejected' ? profileResult.reason : null;
      const homeError = homeResult.status === 'rejected' ? homeResult.reason : null;
      const engagementError = engagementResult.status === 'rejected' ? engagementResult.reason : null;

      const tasks = tasksResult.status === 'fulfilled' ? tasksResult.value : [];
      const readiness = readinessResult.status === 'fulfilled' ? readinessResult.value : null;
      const profile = profileResult.status === 'fulfilled' ? profileResult.value : null;
      const home = homeResult.status === 'fulfilled' ? homeResult.value : null;
      const engagementData = engagementResult.status === 'fulfilled' ? engagementResult.value : null;

      const raw = (engagementData ?? {}) as Partial<EngagementData>;
      const engagement: EngagementData = {
        currentStreak: raw.currentStreak ?? 0,
        longestStreak: raw.longestStreak ?? 0,
        lastPracticeDate: raw.lastPracticeDate ?? null,
        totalPracticeMinutes: raw.totalPracticeMinutes ?? 0,
        totalPracticeSessions: raw.totalPracticeSessions ?? 0,
        avgSessionMinutes: raw.avgSessionMinutes ?? 0,
        weeklyActivity: raw.weeklyActivity ?? [],
        streakFreezeAvailable: raw.streakFreezeAvailable ?? false,
        streakFreezeUsedThisWeek: raw.streakFreezeUsedThisWeek ?? false,
      };

      const firstError = taskError ?? readinessError ?? profileError ?? homeError ?? engagementError;
      const partial = !!firstError;

      setState({
        data: {
          home,
          profile,
          readiness,
          tasks,
          engagement,
          loadedAt: new Date().toISOString(),
        },
        error: firstError ? toErrorMessage(firstError) : null,
        status: partial ? 'partial' : 'success',
      });

      if (!partial) {
        track('readiness_viewed');
      }
    } catch (error) {
      setState((current) => ({
        ...current,
        error: toErrorMessage(error),
        status: 'error',
      }));
    }
  }

  // NOTE: Stable-callback pattern via useRef instead of React 19.2 `useEffectEvent`.
  // `useEffectEvent` resolves to `undefined` in certain bundler/minifier
  // configurations in our production build, so we use the classic ref-wrapper
  // pattern which has the same stale-closure guarantees.
  const loadRef = useRef<() => void>(() => {});
  useLayoutEffect(() => {
    loadRef.current = () => {
      void load();
    };
  });

  useEffect(() => {
    if (authLoading) {
      return;
    }

    if (!isAuthenticated) {
      setState({
        data: initialState.data,
        error: null,
        status: 'success',
      });
      return;
    }

    loadRef.current();
  }, [authLoading, isAuthenticated]);

  return {
    data: state.data,
    error: state.error,
    reload: load,
    status: state.status,
  };
}
