# Issue 03 — Dashboard Performance Analysis

## Route topology

`app/dashboard/page.tsx` is a literal re-export of `app/page.tsx` (`export { default } from '../page';`) — there is exactly one Dashboard implementation. It is a Client Component (`'use client'`) rendered inside `LearnerDashboardShell` → `AppShell` → `AuthGuard`, with a heavier detail section (`components/learner/learner-dashboard-details.tsx`: readiness, pronunciation, add-ons, streak cards) deliberately deferred via `next/dynamic({ ssr: false })` to keep it off the hydration-critical path — this part of the architecture was already sound.

## API audit (before this pass)

**13 distinct GET requests** fire when the Dashboard mounts, all via TanStack Query and genuinely **parallel** (not a sequential waterfall — each is its own independent `useQuery` call):

- `lib/hooks/use-dashboard-home.ts`: study-plan, readiness, user-profile, dashboard-home, engagement (5)
- `app/page.tsx` directly: scoring-policy, entitlement-snapshot, subscription, ai-package-credits (4)
- `hooks/use-enabled-modules.ts` (rendered inside the dashboard body via the skill switcher, **and** inside the always-mounted `Sidebar`/`BottomNav`): entitlement-snapshot again, via a separate, uncoordinated cache (1 duplicate)
- `hooks/use-feature-flag-map.ts` (nav feature flags): 1–2 requests
- `use-exam-date-gate` (`AuthGuard`, once per app session, module-cached after that): 1

No sequential-await chain was found inside the Dashboard's own hooks — parallelization was already correct. The concrete problems were duplication and refetch cadence, not the fetch topology.

## Root causes found

1. **Duplicate entitlement fetch.** `/v1/me/entitlement-snapshot` was fetched twice on a cold Dashboard load — once by `app/page.tsx`'s own `entitlementQuery`, once by `useEnabledModules` (used by the skill switcher rendered inside the Dashboard, and by the always-mounted nav chrome) via its own hand-rolled module-level cache, completely uncoordinated with the Dashboard's TanStack Query cache. A purchase-success invalidation (`app/page.tsx`, after checkout) only reached the Dashboard's own copy, not the nav-chrome copy — nav/module visibility could go stale relative to the hero after a purchase.
2. **Short staleTime on slow-changing, mutation-invalidated data caused an avoidable refetch burst on every dashboard revisit.** `scoringPolicyQuery` (60s), `entitlementQuery`/`subscriptionQuery`/`aiPackageCreditsQuery` (30s each) all carried staleTimes shorter than a typical "leave the dashboard, do something else, come back" gap. React Query's default behavior (`refetchOnMount: true` when data is stale) means every such revisit re-issued all four requests in the background — the cached data still painted instantly, but this is real, avoidable network/CPU/battery churn that competes with whatever the user actually navigated to. **Verified before raising these**: every one of these four is invalidated explicitly at its own mutation site (`app/page.tsx`'s purchase-success effect, lines ~287-294) — so a longer staleTime only skips an *unnecessary* opportunistic background refetch, it does not risk showing genuinely stale data after an actual change, because invalidation (not staleTime) is what keeps them correct after a write.
3. **`studyPlan`/`readiness`/`dashboardHome` were deliberately left unchanged.** These reflect the learner's own just-completed practice, and — unlike the four above — **no explicit invalidation exists anywhere in the codebase for `queryKeys.dashboard.home`, `queryKeys.studyPlan.list`, or `queryKeys.readiness.self`** (confirmed by `grep`). Raising their staleTime without that invalidation coverage would have created a real correctness regression: a learner who just finished a practice session and returns to the Dashboard within, say, two minutes would see stale "next action" data with no mechanism to force a refresh. Left at their existing 15–60s staleTimes.
4. **The remount-on-revisit cost is architectural, not query-level** — same root cause as Issue 02 (`03-global-performance-analysis.md` §C): leaving `/dashboard` and returning remounts the whole `AppShell`, including the Dashboard content itself (`app-shell.tsx`'s `key={pathname}` on `motion.main`, which is also what drives the intended route-transition animation). The TanStack Query cache survives the remount (so cached data paints immediately on return), but every `useEffect` in the remounted tree reruns. This is the same deferred architectural fix as Issue 02, not something addressable from the Dashboard's own code alone.

## Changes implemented (this pass)

- `hooks/use-enabled-modules.ts` now shares `queryKeys.dashboard.entitlement(userId)` with the Dashboard's own query via a new `useEntitlementSnapshot` hook (`lib/query/hooks.ts`) — one request instead of two, one invalidation path instead of two out-of-sync caches.
- `app/page.tsx`: `scoringPolicyQuery` staleTime 60s → 5 min; `entitlementQuery`/`subscriptionQuery`/`aiPackageCreditsQuery` 30s → 2 min. `lib/query/hooks.ts`'s new `useEntitlementSnapshot` uses the same 2 min to stay consistent with the query it shares a key with.
- `studyPlan`, `readiness`, `dashboardHome`, `profile`, `engagement` staleTimes: **unchanged**, for the reason in (3) above.

## Dashboard data lifecycle (confirmed, not changed)

- **First visit:** shell/hero render unconditionally and immediately; `AsyncStateWrapper` gates only on the two genuinely critical queries (`tasksQuery`/`profileQuery`), not all 13; the heavier detail widgets are deferred via `next/dynamic`. This matches the required "shell first, progressive fill" behavior already.
- **Return visit:** with the fixes above, previously-loaded Dashboard data paints from cache immediately (TanStack Query always does this on remount regardless of staleTime) and now genuinely skips an unnecessary background refetch for the four mutation-invalidated queries when the visit happens within their new staleTime window, instead of always re-requesting them.

## Not changed, and why

`components/auth/auth-guard.tsx` renders a full structural skeleton (`LearnerSessionLoadingState` — header/sidebar/hero/bottom-nav shapes, not a generic spinner) while `loading` is true, then swaps to the real shell. This looked, at first read, like it was gating the whole shell behind every navigation. It is not: `AuthProvider` is mounted once at the persistent root layout (`app/providers.tsx`) and does not re-run its session-restore effect per navigation — `loading` is only `true` on a genuine cold app launch, once per session, not on "leave Dashboard, come back." Given the real impact is narrow (cold start only) and `AuthGuard` is the security gate protecting every protected route, it was left unchanged rather than restructured for a benefit that would only ever apply once per session — see `03-global-performance-analysis.md` for the equivalent reasoning applied to the shell-remount question generally.
