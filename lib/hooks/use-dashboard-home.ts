'use client';

import { useContext, useEffect, useState } from 'react';
import { AuthContext } from '@/contexts/auth-context';
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchDashboardHome, fetchEngagement, fetchReadiness, fetchStudyPlan, fetchUserProfile } from '@/lib/api';
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
}

interface DashboardHomeState {
  data: DashboardHomeData;
  error: string | null;
  status: 'loading' | 'success' | 'error';
}

const initialState: DashboardHomeState = {
  data: {
    home: null,
    profile: null,
    readiness: null,
    tasks: [],
    engagement: null,
  },
  error: null,
  status: 'loading',
};

function toErrorMessage(error: unknown): string {
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

export function useDashboardHome() {
  const { track } = useAnalytics();
  const authContext = useContext(AuthContext);
  const authLoading = authContext?.loading ?? false;
  const isAuthenticated = authContext?.isAuthenticated ?? true;
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
      const [tasks, readiness, profile, home, engagementData] = await Promise.all([
        fetchStudyPlan(),
        fetchReadiness(),
        fetchUserProfile(),
        fetchDashboardHome(),
        fetchEngagement(),
      ]);

      const raw = engagementData as Partial<EngagementData>;
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

      setState({
        data: {
          home,
          profile,
          readiness,
          tasks,
          engagement,
        },
        error: null,
        status: 'success',
      });

      track('readiness_viewed');
    } catch (error) {
      setState((current) => ({
        ...current,
        error: toErrorMessage(error),
        status: 'error',
      }));
    }
  }

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

    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- load is intentionally excluded; effect reacts to auth state only, not function identity
  }, [authLoading, isAuthenticated]);

  return {
    data: state.data,
    error: state.error,
    reload: load,
    status: state.status,
  };
}
