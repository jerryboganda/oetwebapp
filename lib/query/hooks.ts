import {
  useQuery,
  useQueryClient,
  type UseQueryOptions,
} from '@tanstack/react-query';
import {
  fetchDashboardHome,
  fetchEngagement,
  fetchReadiness,
  fetchOnboardingState,
  fetchStudyPlan,
} from '@/lib/api';

/**
 * Canonical query-key factory for the OET app.
 *
 * All query keys are centralized here so cache invalidation is a one-liner.
 * Keys are deeply-typed tuples; if you rename a hook, TS will catch every
 * dangling `invalidateQueries` call.
 *
 * Pattern:
 *   queryKeys.dashboard.home   // ['dashboard', 'home']
 *   queryKeys.readiness.self   // ['readiness', 'self']
 *
 * Invalidation:
 *   queryClient.invalidateQueries({ queryKey: queryKeys.dashboard._def })
 *     // nukes the whole dashboard namespace
 */
export const queryKeys = {
  profile: {
    _def: ['profile'] as const,
    self: ['profile', 'self'] as const,
    onboarding: ['profile', 'onboarding'] as const,
  },
  dashboard: {
    _def: ['dashboard'] as const,
    home: ['dashboard', 'home'] as const,
    engagement: ['dashboard', 'engagement'] as const,
  },
  readiness: {
    _def: ['readiness'] as const,
    self: ['readiness', 'self'] as const,
  },
  studyPlan: {
    _def: ['study-plan'] as const,
    list: ['study-plan', 'list'] as const,
  },
} as const;

type QueryOpts<TData> = Omit<UseQueryOptions<TData, Error, TData>, 'queryKey' | 'queryFn'>;

/**
 * Migrating a new fetch site to React Query:
 *   1. Add a key to `queryKeys` above
 *   2. Export a `useXxx` hook here that calls `useQuery`
 *   3. Replace the component's `useState+useEffect+fetch` with `useXxx()`
 *   4. Invalidate after mutations with `queryClient.invalidateQueries`
 */


export function useOnboardingState(options: QueryOpts<Awaited<ReturnType<typeof fetchOnboardingState>>> = {}) {
  return useQuery({
    queryKey: queryKeys.profile.onboarding,
    queryFn: fetchOnboardingState,
    staleTime: 30_000,
    ...options,
  });
}

export function useDashboardHome(options: QueryOpts<Awaited<ReturnType<typeof fetchDashboardHome>>> = {}) {
  return useQuery({
    queryKey: queryKeys.dashboard.home,
    queryFn: fetchDashboardHome,
    staleTime: 30_000,
    ...options,
  });
}

export function useEngagement(options: QueryOpts<Awaited<ReturnType<typeof fetchEngagement>>> = {}) {
  return useQuery({
    queryKey: queryKeys.dashboard.engagement,
    queryFn: fetchEngagement,
    staleTime: 60_000,
    ...options,
  });
}

export function useReadiness(options: QueryOpts<Awaited<ReturnType<typeof fetchReadiness>>> = {}) {
  return useQuery({
    queryKey: queryKeys.readiness.self,
    queryFn: fetchReadiness,
    staleTime: 30_000,
    ...options,
  });
}

export function useStudyPlan(options: QueryOpts<Awaited<ReturnType<typeof fetchStudyPlan>>> = {}) {
  return useQuery({
    queryKey: queryKeys.studyPlan.list,
    queryFn: fetchStudyPlan,
    staleTime: 15_000,
    ...options,
  });
}


export { useQueryClient };
