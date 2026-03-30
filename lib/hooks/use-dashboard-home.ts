'use client';

import { useEffect, useState } from 'react';
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchDashboardHome, fetchReadiness, fetchStudyPlan, fetchUserProfile } from '@/lib/api';
import type { ReadinessData, StudyPlanTask, UserProfile } from '@/lib/mock-data';

export interface DashboardHomeData {
  home: Record<string, any> | null;
  profile: UserProfile | null;
  readiness: ReadinessData | null;
  tasks: StudyPlanTask[];
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
  const [state, setState] = useState<DashboardHomeState>(initialState);

  async function load() {
    setState((current) => ({
      ...current,
      error: null,
      status: 'loading',
    }));

    try {
      const [tasks, readiness, profile, home] = await Promise.all([
        fetchStudyPlan(),
        fetchReadiness(),
        fetchUserProfile(),
        fetchDashboardHome(),
      ]);

      setState({
        data: {
          home,
          profile,
          readiness,
          tasks,
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
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return {
    data: state.data,
    error: state.error,
    reload: load,
    status: state.status,
  };
}
