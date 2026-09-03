# Issue 02 — Global Navigation Performance Analysis

## Method

Two independent, read-only code-level investigations (app shell/provider tree, route topology, TanStack Query usage, motion/transition config) plus direct file inspection and `git log`/`git show` on the relevant history. No physical device, emulator, or browser profiler was available in this environment (see `07-release-verification.md`) — findings below are evidence from the code itself (file/line citations), not guesses.

## A. Main/UI thread

`app/providers.tsx` mounts once at the root layout and does not re-run per navigation (`AuthProvider`, `QueryProvider`, `RuntimeConfigProvider`, the mobile runtime bridge, etc.). No synchronous heavy work was found sitting directly on a per-navigation critical path. This was **not** a bottleneck.

## B. Rendering

`contexts/auth-context.tsx`'s `AuthContext.Provider value` is properly `useMemo`'d — it does not cascade re-renders on unrelated state changes. `lib/motion.ts`'s route transition (`getSurfaceMotion('route', ...)`) only animates `opacity`/`transform` (composited, not layout-triggering). Neither was a bottleneck **in isolation** — but see C below: the `AnimatePresence` wrapper that's supposed to carry that transition across a route change is itself torn down and recreated on every learner navigation, so the intended crossfade doesn't get to play as designed; the visual result reads closer to an abrupt swap.

## C. Navigation — the primary root cause

**`components/layout/app-shell.tsx` (via `LearnerDashboardShell`) is instantiated inside each of ~202 individual `app/**/page.tsx` files, not in a shared Next.js `layout.tsx`.** Verified by direct count:

- `grep -rl "LearnerDashboardShell" --include="page.tsx" app` → 202 files
- `grep -rl "LearnerDashboardShell" --include="layout.tsx" app` → 0 files
- Contrast: `app/admin/layout.tsx` and `app/expert/layout.tsx`/`app/tutor/layout.tsx` render their shell **once**, in a real `layout.tsx` — 0 admin/expert `page.tsx` files instantiate it directly. Those workspaces do not have this problem.

Every navigation between two top-level learner routes therefore fully **unmounts and remounts** `AuthGuard`, `Sidebar`, `TopNav`, and `BottomNav` — not because any one of them does expensive work, but because they are structurally recreated from zero on every tap. This is what makes navigation read as "more like page reloads than native navigation": the sidebar and top bar are not actually persistent UI today.

### Why this was not fixed in this pass

Fixing this correctly requires moving route **subtrees** (not just index pages) into a Next.js route group with a shared `layout.tsx`, because a URL segment's whole subtree must be owned by one physical directory — you cannot move `app/listening/page.tsx` into a `(learner)` group while leaving `app/listening/paper/[paperId]/page.tsx` outside it; Next.js requires the entire `app/listening/**` tree to move together. Several of those nested routes (exam-taking flows) use `distractionFree` shell mode or bespoke chrome, so a blind migration risks double-wrapping or breaking full-screen exam UI across dozens of routes on a **live, paying production app**, with no browser/device available in this session to visually verify the result. That is a materially different risk profile from the header and query-caching fixes in this same pass, which are verifiable by code inspection, type-checking, and targeted unit tests alone.

**Recommended follow-up** (not executed here): introduce `app/(learner)/layout.tsx` rendering `LearnerDashboardShell` once, plus a small `usePageChrome({ title, actions })` hook pages call to register their header title/actions with the already-mounted shell instead of instantiating their own copy. Migrate route-subtree by route-subtree (dashboard first, since it has no nested dynamic children — see `04-dashboard-performance-analysis.md`), each one verified with `tsc --noEmit`, the existing Playwright/Vitest suites for that section, and a manual pass in a real browser/device before merging. This is genuinely a multi-PR initiative, not a single-session change.

## D. Networking — fixed in this pass

1. **`components/layout/learner-streak-badges.tsx`** called `fetchStreak()`/`fetchXP()` in a raw `useEffect` with zero caching. Because this component lives inside `TopNav`, which remounts on every navigation (C above), this fired two fresh, uncached network requests on **every single tap** — concrete, reproducible extra network work sitting directly on the navigation path. **Fixed:** moved onto the shared TanStack Query client (`lib/query/hooks.ts` `useStreak`/`useXp`, 60s staleTime — streak/XP change at most once per completed activity, not per navigation).
2. **`hooks/use-enabled-modules.ts`** had its own hand-rolled, module-level single-flight cache for `/v1/me/entitlement-snapshot`, entirely independent of the Dashboard's own React Query cache for the same endpoint (`app/page.tsx`'s `entitlementQuery`, same `queryKeys.dashboard.entitlement(userId)` semantics). Two uncoordinated caches meant two network hits for the same data, and the purchase-success invalidation in `app/page.tsx` never reached the nav-chrome cache, so nav/module visibility could go stale relative to the dashboard hero after a purchase. **Fixed:** `useEnabledModules` now calls the same shared `useEntitlementSnapshot` hook/query-key, so TanStack Query's own deduping collapses concurrent callers into one request, and one invalidation path covers both.

## E. `prefetch={false}` — investigated, deliberately left unchanged

Every primary nav `Link` in `Sidebar`/`BottomNav`/`TopNav` carries `prefetch={false}`, which removes Next.js's opportunity to warm a destination route ahead of a tap — a real contributor to "tap doesn't feel instant." This session initially removed it, then found `docs/performance/2026-08-07-performance-evidence.md`: a prior, **measured, CI-budget-gated** performance pass explicitly disabled this exact prefetching as one of four changes that "stabilised the learner first paint" (LCP/FCP), backed by a passing Playwright browser-budget gate (LCP ≤ 2.5s, FCP ≤ 1.8s, CLS ≤ 0.1) and a k6 load gate. The change was reverted in this session rather than shipped, because:

- This session has no way to reproduce that same rigorous, isolated-stack CI gate to re-verify the trade-off (no Docker/staging stack available here — see `07-release-verification.md`).
- Blanket prefetch-on-viewport for a sidebar with ~14 simultaneously-visible links is plausible cold-load contention (competing network/CPU with the current page's own critical fetches right as it hydrates) — a real, different mechanism from the warm-navigation tap latency this task is targeting.

**Recommended follow-up** (not executed here): replace blanket viewport-prefetch with **intent-based** prefetch — trigger `router.prefetch(href)` on pointer/touch `onMouseEnter`/`onTouchStart` (near-zero cost, no automatic viewport-wide prefetch storm on mount) rather than flipping `prefetch` back to the Next.js default. This should recover most of the warm-tap latency win without the cold-load contention the prior fix addressed, but it must go through the same `docs/performance/` browser-budget CI gate before shipping, not just this session's typecheck/lint/unit-test ladder.

## F. Caching / app shell / assets / animation

Covered above (C, D). No oversized-image, animation-blocking-interaction, or asset-loading issue was found specific to global navigation in this investigation; `next.config.ts` already sets `experimental.optimizePackageImports` for `lucide-react`/`recharts`/`motion`.

## Changes implemented (this pass)

- `components/layout/learner-streak-badges.tsx` — React-Query-backed, no more per-remount uncached fetch.
- `hooks/use-enabled-modules.ts`, `lib/query/hooks.ts`, `lib/query/keys.ts` — shared entitlement cache with the Dashboard.
- `tests/test-utils.tsx` — `renderWithRouter` now also provides a `QueryClientProvider`, since real app-shell chrome now depends on one (see `05-before-after-performance.md` for the two tests this fixed).

## Result

Every learner navigation now issues two fewer network requests on average (streak/XP no longer refire; entitlement is deduped against the dashboard's own fetch) with zero change to route-remount behavior. The dominant remaining lever — the shell remount itself — is root-caused and left as a scoped, reviewable follow-up rather than executed blind.
