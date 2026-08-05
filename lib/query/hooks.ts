import {
  useMutation,
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
  fetchUserProfile,
} from '@/lib/api';
import { queryKeys } from './keys';

export { queryKeys } from './keys';

type QueryOpts<TData> = Omit<UseQueryOptions<TData, Error, TData>, 'queryKey' | 'queryFn'>;

/**
 * Migrating a new fetch site to React Query:
 *   1. Add a key to `queryKeys` above
 *   2. Export a `useXxx` hook here that calls `useQuery`
 *   3. Replace the component's `useState+useEffect+fetch` with `useXxx()`
 *   4. Invalidate after mutations with `queryClient.invalidateQueries`
 */


export function useOnboardingState(userId: string, options: QueryOpts<Awaited<ReturnType<typeof fetchOnboardingState>>> = {}) {
  return useQuery({
    queryKey: queryKeys.profile.onboarding(userId),
    queryFn: fetchOnboardingState,
    staleTime: 30_000,
    ...options,
  });
}

export function useUserProfileQuery(userId: string, options: QueryOpts<Awaited<ReturnType<typeof fetchUserProfile>>> = {}) {
  return useQuery({
    queryKey: queryKeys.profile.self(userId),
    queryFn: fetchUserProfile,
    staleTime: 60_000,
    ...options,
  });
}

export function useDashboardHome(userId: string, options: QueryOpts<Awaited<ReturnType<typeof fetchDashboardHome>>> = {}) {
  return useQuery({
    queryKey: queryKeys.dashboard.home(userId),
    queryFn: fetchDashboardHome,
    staleTime: 30_000,
    ...options,
  });
}

export function useEngagement(userId: string, options: QueryOpts<Awaited<ReturnType<typeof fetchEngagement>>> = {}) {
  return useQuery({
    queryKey: queryKeys.dashboard.engagement(userId),
    queryFn: fetchEngagement,
    staleTime: 60_000,
    ...options,
  });
}

export function useReadiness(userId: string, options: QueryOpts<Awaited<ReturnType<typeof fetchReadiness>>> = {}) {
  return useQuery({
    queryKey: queryKeys.readiness.self(userId),
    queryFn: fetchReadiness,
    staleTime: 30_000,
    ...options,
  });
}

export function useStudyPlan(userId: string, options: QueryOpts<Awaited<ReturnType<typeof fetchStudyPlan>>> = {}) {
  return useQuery({
    queryKey: queryKeys.studyPlan.list(userId),
    queryFn: fetchStudyPlan,
    staleTime: 15_000,
    ...options,
  });
}


/**
 * FE-006: mutation helper that invalidates the given query keys on success, so
 * call sites stop hand-rolling `queryClient.invalidateQueries` (or forgetting to,
 * which is the stale-data-after-write bug). Pass the keys whose data the write
 * affects; everything else is a normal TanStack mutation.
 */
export function useApiMutation<TData, TVars>(
  mutationFn: (vars: TVars) => Promise<TData>,
  invalidate: ReadonlyArray<readonly unknown[]> = [],
) {
  const queryClient = useQueryClient();
  return useMutation<TData, Error, TVars>({
    mutationFn,
    onSuccess: () => {
      invalidate.forEach((queryKey) => {
        void queryClient.invalidateQueries({ queryKey });
      });
    },
  });
}

export { useQueryClient };
