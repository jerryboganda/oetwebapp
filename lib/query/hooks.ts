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

/**
 * Canonical query-key factory for the OET app.
 *
 * All query keys are centralized here so cache invalidation is a one-liner.
 * Keys are deeply-typed tuples; if you rename a hook, TS will catch every
 * dangling `invalidateQueries` call.
 *
 * Pattern:
 *   queryKeys.dashboard.home(userId)   // ['dashboard', userId, 'home']
 *   queryKeys.readiness.self(userId)   // ['readiness', userId, 'self']
 *
 * Invalidation:
 *   queryClient.invalidateQueries({ queryKey: queryKeys.dashboard._def })
 *     // nukes the whole dashboard namespace
 */
export const queryKeys = {
  profile: {
    _def: ['profile'] as const,
    self: (userId: string) => ['profile', userId, 'self'] as const,
    onboarding: (userId: string) => ['profile', userId, 'onboarding'] as const,
  },
  dashboard: {
    _def: ['dashboard'] as const,
    home: (userId: string) => ['dashboard', userId, 'home'] as const,
    engagement: (userId: string) => ['dashboard', userId, 'engagement'] as const,
    scoringPolicy: (userId: string) => ['dashboard', userId, 'scoring-policy'] as const,
    entitlement: (userId: string) => ['dashboard', userId, 'entitlement'] as const,
    subscription: (userId: string) => ['dashboard', userId, 'subscription'] as const,
    aiPackageCredits: (userId: string) => ['dashboard', userId, 'ai-package-credits'] as const,
  },
  readiness: {
    _def: ['readiness'] as const,
    self: (userId: string) => ['readiness', userId, 'self'] as const,
  },
  studyPlan: {
    _def: ['study-plan'] as const,
    list: (userId: string) => ['study-plan', userId, 'list'] as const,
  },
  onboardingTours: {
    _def: ['onboarding-tours'] as const,
    state: ['onboarding-tours', 'state'] as const,
  },
  settings: {
    _def: ['settings'] as const,
    home: (userId: string) => ['settings', userId, 'home'] as const,
    freeze: (userId: string) => ['settings', userId, 'freeze'] as const,
    section: (userId: string, section: string) => ['settings', userId, 'section', section] as const,
  },
  progress: {
    _def: ['progress'] as const,
    trend: (userId: string) => ['progress', userId, 'trend'] as const,
    completion: (userId: string) => ['progress', userId, 'completion'] as const,
    submissionVolume: (userId: string) => ['progress', userId, 'submission-volume'] as const,
    evidence: (userId: string) => ['progress', userId, 'evidence'] as const,
  },
  leaderboard: {
    _def: ['leaderboard'] as const,
    list: (userId: string, examType: string, period: string) =>
      ['leaderboard', userId, 'list', examType, period] as const,
    position: (userId: string, examType: string, period: string) =>
      ['leaderboard', userId, 'position', examType, period] as const,
  },
  vocabulary: {
    _def: ['vocabulary'] as const,
    categories: (userId: string, examType: string, profession?: string) =>
      ['vocabulary', userId, 'categories', examType, profession ?? null] as const,
    recallSets: (userId: string, examType: string, profession?: string) =>
      ['vocabulary', userId, 'recall-sets', examType, profession ?? null] as const,
  },
  listening: {
    _def: ['listening'] as const,
    lessons: ['listening', 'lessons'] as const,
    strategies: (category: string) => ['listening', 'strategies', category] as const,
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
