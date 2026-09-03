import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseQueryOptions,
} from '@tanstack/react-query';
import {
  fetchDashboardHome,
  fetchEngagement,
  fetchMyEntitlementSnapshot,
  fetchReadiness,
  fetchOnboardingState,
  fetchStreak,
  fetchStudyPlan,
  fetchUserProfile,
  fetchXP,
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

// Streak/level in the header (LearnerStreakBadges) used to be a raw, uncached
// fetchStreak()/fetchXP() call in a useEffect — because the header remounts on
// every learner navigation (AppShell has no persistent layout), that fired two
// fresh network requests on every single tap. Both change at most once per
// completed activity, not per navigation, so a 60s staleTime plus the shared
// QueryClient's cache is enough to make repeat mounts free.
export function useStreak(userId: string, options: QueryOpts<Awaited<ReturnType<typeof fetchStreak>>> = {}) {
  return useQuery({
    queryKey: queryKeys.gamification.streak(userId),
    queryFn: fetchStreak,
    staleTime: 60_000,
    ...options,
  });
}

export function useXp(userId: string, options: QueryOpts<Awaited<ReturnType<typeof fetchXP>>> = {}) {
  return useQuery({
    queryKey: queryKeys.gamification.xp(userId),
    queryFn: fetchXP,
    staleTime: 60_000,
    ...options,
  });
}

// Shared with the Dashboard's own entitlement query (app/page.tsx) via the
// same queryKeys.dashboard.entitlement(userId) key — React Query dedupes by
// key regardless of which hook/component asks first, so the sidebar, bottom
// nav, skill switcher (via useEnabledModules below) and the dashboard hero
// collapse into one /v1/me/entitlement-snapshot request instead of two
// independent, uncoordinated caches (module-level cache vs QueryClient) that
// used to double-fetch and could invalidate out of sync with each other.
export function useEntitlementSnapshot(
  userId: string,
  options: QueryOpts<Awaited<ReturnType<typeof fetchMyEntitlementSnapshot>>> = {},
) {
  return useQuery({
    queryKey: queryKeys.dashboard.entitlement(userId),
    queryFn: fetchMyEntitlementSnapshot,
    // Kept in sync with app/page.tsx's own entitlementQuery, which shares
    // this exact key — every mutation that changes entitlement (purchase
    // completion) already invalidates this key explicitly, so a longer
    // staleTime only skips unnecessary background refetches, not real ones.
    staleTime: 2 * 60_000,
    // Deliberately NOT overriding `retry`/`retryDelay` here (the module-level
    // cache this replaced used a hand-rolled 3-attempt backoff): an explicit
    // per-query retry always wins over a QueryClient's defaultOptions, which
    // would silently defeat a test's `retry: false` and leave real
    // setTimeout-scheduled retries running past that test's lifetime. Falling
    // through to QueryProvider's shared default (retry: 1) keeps a first-load
    // blip from reading as "plan has no modules" while staying consistent
    // with every sibling dashboard query below, none of which override retry.
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
