# Frontend Audit Report — OET Web App

**Method:** Code-grounded audit (every issue cites `file:line`) + objective toolchain baseline. A
live, logged-in browser sweep (responsive/visual/keyboard/Core-Web-Vitals + console/network) is
**PENDING** — see §F and §I — because the local backend API was crash-looping during this pass.
**No frontend code was changed** (audit-first, per agreement). The only edits applied are the
authorized backend boot-fix (`BillingCartEndpoints.cs`) and a throwaway `.claude/launch.json`.

**Stack:** Next.js 16.2.6 (App Router, React 19, TS strict), Tailwind v4 `@theme` tokens,
`next-themes` dark mode, TanStack Query + Context + Zustand, Vitest + Playwright + axe. ~560 routes.

---

## A. Executive Summary

- **Objective baseline is GREEN:** `pnpm build` ✅ (2.4 min), `pnpm lint` ✅, `tsc --noEmit` ✅,
  `pnpm test` ✅ **279 files / 1863 unit tests passing**. No type/lint/build errors.
- **Security/privacy: clean pass** (§G/§9) — tokens never persisted to web storage (in-memory on web,
  native secure-storage on mobile), **zero `dangerouslySetInnerHTML`/`innerHTML`**, nonce-based CSP,
  correctly-layered route protection (middleware → `AuthGuard` → role checks) with no
  flash-of-protected-content, enforced admin-permission gating, no secrets in the client bundle, and a
  correct CSRF double-submit-cookie scheme. **No security findings.**
- **16 frontend issues found**, all code-grounded: **1 Critical, 2 High, 9 Medium, 4 Low**, plus a
  fragile-area watch (Writing V2 marking).
- **Highest risk (FE-001, Critical):** persisted client state (React Query cache + Zustand review
  drafts in `localStorage['expert-console-store']`) is **not cleared on logout** → on a shared
  expert/tutor device the next user can see the previous user's review drafts/cached data.
- **Environment blocker (not a frontend bug):** the local `.NET` API was crash-looping on boot
  (exit 134) due to Minimal-API handlers missing `[FromServices]` on injected services
  (`BillingCartEndpoints.cs`, then `BillingCheckoutEndpoints.cs`, likely more). Cart was fixed under
  your authorization; the rest was handed back to you. This blocked the live data-driven sweep.

**Severity counts:** Critical 1 · High 2 · Medium 9 · Low 4.

---

## B. Frontend Issue Register

| ID | Sev | Category | Route/Surface | File(s) | Symptom → Root cause | Recommended fix (reuse-first) | Status |
|---|---|---|---|---|---|---|---|
| FE-001 | **Critical** | Auth/State | Logout (all roles) | `contexts/auth-context.tsx:176`; `lib/stores/expert-store.ts:67`; `lib/writing/store.ts`; sites `components/layout/sidebar.tsx:239`, `top-nav.tsx:136`, `app/settings/[section]/page.tsx:916` | Query cache + Zustand `reviewDrafts` (localStorage `expert-console-store`) survive logout → cross-user data exposure on shared devices. `signOut()` resets only auth state | In `signOut().finally`: `useQueryClient().clear()` + a store-registry `resetAllStores()` (`AuthProvider` is inside `QueryProvider`, so the hook is valid); expert store adds `reset()`+`persist.clearStorage()` | Confirmed (code) |
| FE-002 | High | API/Config | Writing coach realtime | `lib/writing/realtime.ts:56-76` | No `window` guard; coach opens a **raw `ws://…/ws/writing/coach/…`** (not via `/api/backend`, which can't upgrade WS). In prod, silently falls back to `ws://127.0.0.1:5198` unless `NEXT_PUBLIC_REALTIME_API_BASE_URL`/`NEXT_PUBLIC_API_BASE_URL` is an absolute WS-reachable origin. Also: access token in URL query (log-exposure) | Require an absolute realtime origin in browser; remove/fail-loud on the localhost fallback. Move token to a subprotocol/header where possible | Confirmed (code) |
| FE-003 | High | API/Config | Tutor-book download | `lib/api.ts:5632-5634` | `tutorBookDownloadUrl()` re-reads env with `?? 'http://localhost:5199'` (**wrong port**) instead of the module's `API_BASE_URL` → broken/incorrect download URL where the fallback is hit | Reuse `API_BASE_URL` (= `env.apiBaseUrl` → `/api/backend` in browser) | Confirmed (code) |
| FE-004 | Medium | Placeholder data | `/expert/mocks/bookings` | `app/expert/mocks/bookings/page.tsx:29-132` | `MOCK_HEATMAP_ROWS`/`MOCK_QUEUE_ITEMS`/`MOCK_CONSISTENCY_SIGNALS` rendered as if real (endpoints don't exist yet) | Visible "Sample data — preview" `Badge`+`InlineAlert`, or feature-flag the route off in prod | Confirmed (code) |
| FE-005 | Medium | Unwired form | `/settings/reminders` | `app/settings/reminders/page.tsx:54-66` | `handleSave()` is a fake `setTimeout(500)` that shows a **success toast** + fires `reminders_preferences_saved` analytics with **no backend call**; prefs never loaded → false "saved", resets on reload | Wire to existing `fetchSettingsSection('notifications')` / `updateSettingsSection('notifications', …)` (confirm the section schema) **or** gate honestly (disable save, remove fake success). Toggles also need `role="switch"`/`aria-checked` | Confirmed (firsthand) |
| FE-006 | Medium | Data layer | reading/writing/listening/speaking | `lib/query/hooks.ts` (5 keys/5 hooks); raw fetch e.g. `app/reading/practice/page.tsx:99` | Only 5 query namespaces centralized; no `useMutation`/invalidation helpers — most flows use `useState+useEffect+fetch` with manual refresh → stale data after mutations | Extend `queryKeys` + add `useQuery`/`useMutation` per flow, invalidate on mutate (migrate highest-churn first) | Confirmed (firsthand) |
| FE-007 | Medium | Accessibility | all non-Speaking surfaces | `tests/a11y/*` | axe coverage is **Speaking-only**; Reading/Writing/Listening/Dashboard/Billing/Auth/Admin unaudited | Add `*.a11y.spec.ts` via existing `runAxe` (WCAG 2.1/2.2 AA) | Confirmed (code) |
| FE-008 | Medium | Motion/A11y | learner/auth/dashboard/conversation | `components/domain/conversation/ConversationChatView.tsx:34`; sign-in/register `motion.p`; dashboard cards | framer-motion animations ignore `prefers-reduced-motion` (admin/core UI honor it) | Gate via `lib/motion.ts` `prefersReducedMotion()` + `useReducedMotion()` | Confirmed (code) |
| FE-009 | Medium | Design tokens | billing/pricing/learner widgets | `components/billing/addon-purchase-modal.tsx`, `components/pricing/*`, `components/learner/dashboard-addons-widget.tsx` | Gold/bronze `#996F1F`/`#D4A44F` not tokenized; `#6d28d9`/`#7c3aed` literals duplicate existing tokens | Add gold `@theme` tokens; replace dup literals. **Leave** `OetStatementOfResultsCard.tsx` (spec-critical) | Confirmed (code) |
| FE-010 | Medium | API contract | reading | `app/reading/practice/page.tsx:68,234`; `app/reading/paper/[paperId]/results/page.tsx:144` | Attempt cooldown missing; mini-test duration hardcoded `10`; errorBankCleared toast commented (API field absent) | Extract constant; coordinate DTO fields with backend; leave commented toast until field ships | Confirmed (code) |
| FE-011 | Low | Forms | admin create/edit | `app/admin/billing/coupons/[code]/page.tsx`, `products/[productCode]/page.tsx` | Mutation errors are toast-only (dismissable, no inline persistence); some submit buttons may not disable in-flight | Add inline `InlineAlert` + `disabled={saving}` (learner forms e.g. appeal already do this) | Confirmed (code) |
| FE-012 | Low | A11y/responsive | misc | `components/domain/reading-pdf-viewer.tsx:139`; `components/admin/ui/dense-table.tsx` | Programmatic `new Image()` lacks `alt` (measurement img, minor); `min-w-[34rem]` can scroll <640px (inside overflow container) | Set `image.alt`; verify dense-table scroll container acceptable | Confirmed (code) |
| FE-013 | Medium | SEO/metadata | auth + learner pages | `app/(auth)/register/page.tsx`, `forgot-password`, `reset-password`, `mfa/*`; `/listening`,`/reading`,`/writing`,`/speaking` pages | ~all auth/learner pages lack `export const metadata` → generic inherited `<title>`; only ~5 pages set metadata. No sitemap/robots verified (root metadata in `app/layout.tsx:83-105` is good) | Add per-page `metadata`/`generateMetadata` for public + key learner pages; add `robots`/`sitemap` if public SEO matters | Confirmed (code) |
| FE-014 | Low-Med | Dark mode | learner surface + a few components | `components/domain/learner-breadcrumbs.tsx` (`bg-white/80` no `dark:`); `components/domain/writing/writing-review-queue.tsx:97` (`text-navy` no `dark:`); `components/ui/data-table.tsx:159` (mobile card `text-navy`) | Specific light-only colors without `dark:` variants → low-contrast/invisible in dark mode. Core UI primitives + admin tokens are otherwise well-covered | Add `dark:` variants (e.g. `dark:text-slate-100`). Confirm whether the learner surface is meant to support dark mode at all | Confirmed (code) |
| FE-015 | Medium | i18n/dates | app-wide | 81× `toLocaleDateString()` (e.g. `app/account/billing/page.tsx`, `app/achievements/certificate/page.tsx`); `learner-freshness-indicator.tsx:75` `toLocaleString()` | Date/time/number formatting is locale/timezone-fragile and inconsistent (mix of no-arg, `'en-GB'`, `'en-AU'`); no central date util (`lib/domain/format.ts` only covers currency) | Add a shared date/number formatter with explicit locale+timezone; replace ad-hoc calls | Confirmed (code) |
| FE-016 | Low | Build/forward-compat | middleware | `middleware.ts` | Next 16 build warns: *"the `middleware` file convention is deprecated. Please use `proxy` instead."* | Rename/migrate `middleware.ts` → `proxy` per the Next 16 guide (no behavior change) | Confirmed (build log) |
| FE-WATCH | Watch | Writing V2 marking | expert/tutor | `components/domain/writing/marking/AiPreAnalysisPanel.tsx`, `RubricPanel.tsx`; `app/tutor/writing/reviews/[submissionId]`, `app/expert/queue/assigned` | Just hotfixed (commit `027c4abc`: undefined `suggestedCriterionFeedback` crash + tutor-grade duplicate-key) **without regression tests** | Add defensive unit tests for both crash modes; re-verify the flow live | Pending live re-verify |
| — | Pass | Security/Privacy | all | see §G | Tokens, XSS, route-guards, role-UI, secrets, CSRF all correctly handled | — | Verified (code) |

---

## C. Page / Route status

The build confirms **~560 app routes compile cleanly and are all dynamic** (`ƒ`, server-rendered).
Per-route desktop/tablet/mobile × auth × API × console verification is part of the **live sweep
(PENDING)** — it requires the API healthy and a driveable browser. To be populated:

| Surface | Desktop | Tablet | Mobile | Auth | API | Console | Status |
|---|---|---|---|---|---|---|---|
| Auth/onboarding | ⏳ | ⏳ | ⏳ | n/a | renders w/o API | ⏳ | Pending live (can run now) |
| Learner test-taking | ⏳ | ⏳ | ⏳ | needs login | needs API | ⏳ | Blocked on API |
| Expert/Tutor | ⏳ | ⏳ | ⏳ | needs login | needs API | ⏳ | Blocked on API |
| Admin | ⏳ | ⏳ | ⏳ | needs login | needs API | ⏳ | Blocked on API |

---

## D. Files that *would* change (recommended fixes — NOT applied)

- FE-001: `contexts/auth-context.tsx`, new `lib/stores/registry.ts`, `lib/stores/expert-store.ts`, `lib/writing/store.ts`
- FE-002: `lib/writing/realtime.ts`
- FE-003: `lib/api.ts`
- FE-004: `app/expert/mocks/bookings/page.tsx`
- FE-005: `app/settings/reminders/page.tsx`
- FE-006: `lib/query/hooks.ts` + per-flow page/hook files
- FE-007: new `tests/a11y/*.a11y.spec.ts`
- FE-008: `lib/motion.ts` consumers (conversation/auth/dashboard components)
- FE-009: `app/globals.css` + `components/billing|pricing|learner/*`
- FE-013: per-page `metadata` across `app/(auth)/*` and learner module pages
- FE-014: `learner-breadcrumbs.tsx`, `writing-review-queue.tsx`, `data-table.tsx`
- FE-015: new shared date util + call sites
- FE-016: `middleware.ts` → `proxy`

**Edits actually applied this session:** `backend/src/OetWithDrHesham.Api/Endpoints/BillingCartEndpoints.cs`
(authorized `[FromServices]` boot-fix) and `.claude/launch.json` (throwaway tooling — safe to delete).

---

## E. Commands run

| Command | Result |
|---|---|
| `pnpm build` | ✅ exit 0 — compiled in 2.4 min; ~560 dynamic routes; warns `middleware`→`proxy` (FE-016) |
| `pnpm lint` (ESLint) | ✅ exit 0 |
| `pnpm exec tsc --noEmit` | ✅ exit 0 |
| `pnpm test` (Vitest) | ✅ exit 0 — 279 files / 1863 tests passing |
| `pnpm test:e2e` / `tests/a11y` | ⏳ Not run — require the API healthy (PENDING) |
| Podman API health probes + `podman restart` | API crash-looping on boot (exit 134); diagnosed + cart fix applied |

---

## F. Visual verification — PENDING

A live browser sweep was not completed because (1) the local API was down (now partly fixed on your
side) and (2) no driveable browser was available (the Chrome MCP extension isn't connected, and the
Preview tool won't share the already-running `:3000` dev server). **No screenshots were fabricated.**
To run it I need: API healthy on `:8080`, **and** either the Chrome extension connected or your OK to
let the Preview tool own port `:3000`. Then I'll capture the viewport matrix (320→1920), console/network
errors, and broken-asset checks per route.

---

## G. Accessibility summary

- **Code-grounded findings:** FE-007 (axe coverage Speaking-only), FE-008 (reduced-motion gaps),
  FE-012 (`new Image()` alt; dense-table min-width), FE-014 (dark-mode contrast), FE-005 (reminder
  toggles lack `role="switch"`).
- **Well-handled:** modal/drawer/dialog focus trap + restore + Escape + `aria-modal` (`components/ui/modal.tsx`,
  `components/admin/ui/dialog.tsx`/`drawer.tsx`); icon-only buttons generally have `aria-label`; data
  tables wrap in `overflow-x` containers.
- **Pending live:** keyboard tab-order, focus-return after modal close, contrast measurement, SR
  announcements — require the browser sweep.

## H. Performance summary

- Build compiles in **2.4 min**; **every route is `force-dynamic`** (root sets it for CSP nonce) — so
  there is **no static/ISR optimization**; all ~560 routes are SSR-on-demand (higher server cost; a
  deliberate security tradeoff worth a conscious decision).
- Heavy client libs present (tiptap, wavesurfer, pdfjs-dist, recharts, signalr, zoom sdk, motion) —
  verify they're lazy/dynamically imported on the routes that need them (pending bundle-analyzer/live).
- Precise First-Load-JS/CWV numbers require the live pass + a bundle analyzer.

## I. Remaining risks / open questions / next steps

**Open questions (block specific fixes):**
1. **WS through proxy (FE-002):** confirmed the `/api/backend` route handler **cannot** upgrade
   WebSockets — so the writing coach needs an absolute WS-reachable origin in prod. Confirm the
   intended prod realtime origin.
2. **Reminders schema (FE-005):** does `PATCH /v1/settings/notifications` accept the reminder toggles +
   slot + push/email keys? → wire vs honest-gate.
3. **Expert mocks (FE-004):** preview-label the synthetic data, or hard-hide behind a flag in prod?
4. **Learner dark mode (FE-014):** is the learner surface meant to support dark mode at all?

**To finish the audit (live sweep):**
1. Bring the API healthy (add `[FromServices]` to the remaining billing endpoint handlers — `BillingCheckoutEndpoints.cs:24,43` next — and confirm the billing services are DI-registered).
2. Enable a browser (connect Chrome MCP, or allow Preview to own `:3000`).
3. I run: per-role login → per-route console/network capture → viewport matrix screenshots →
   keyboard/focus/contrast (axe) → Writing V2 marking re-verification → fill in §C and §F.

---

# Part 2 — Exhaustive Deep-Sweep (FE-017…FE-043)

Second exhaustive parallel sweep (8 specialist agents: loading/error/empty, forms, a11y, responsive,
performance, component-quality/duplication, routing, API-contract). All `file:line` code-grounded.
**API is now healthy** (`:8080` 200/200 after the billing `[FromServices]` fixes were committed), so
the live browser sweep is unblocked on the backend side (still pending a browser — §F).

### New Critical

| ID | Category | File(s) | Symptom → Root cause | Fix |
|---|---|---|---|---|
| **FE-017** | API — **cart broken end-to-end** | `lib/api.ts:13592-13673` vs `Services/Billing/ICartService.cs`, `Endpoints/BillingCartEndpoints.cs` | FE `Cart`/`CartLineItem` field names ≠ backend `CartDto`/`CartItemDto` (`cartId`↔`id`, `promoCodes`↔`appliedPromoCodes`, `subtotalAmount`↔`subtotal`, `unitAmount`↔`unitPrice`); mutations omit required `[FromQuery] cartId` → `/v1/cart/items/undefined`; `addCartItem` omits required `billingPriceId`. Cart money 0/undefined, ops 400 | Align FE cart interfaces+mappers to backend DTOs; thread `cartId`; send `billingPriceId` |
| **FE-018** | API — **checkout broken** | `lib/api.ts:13686-13714`, `lib/native/billing-bridge.ts:276` vs `Endpoints/BillingCheckoutEndpoints.cs` | `createCheckoutSession` sends `{successUrl,cancelUrl}` but backend `CreateCheckoutRequest` is `{cartId}`; response DTO mismatch (`sessionId`/items undefined); native bridge POSTs `{productCode}` not `{cartId}` | Reconcile DTOs; pass a valid `cartId`; fix native bridge |

### New High

| ID | Category | File(s) | Symptom → Root cause | Fix |
|---|---|---|---|---|
| **FE-019** | API 404 | `app/admin/notifications/campaigns/page.tsx:114,179,190,218` vs `Endpoints/AdminCampaignEndpoints.cs:11` | FE calls `/v1/admin/notification-campaigns` + actions `test-send/schedule/pause`; backend group is `/v1/admin/campaigns` with `approve/cancel/send/evaluate-segment` → every call 404s (page non-functional) | Fix base path + action names |
| **FE-020** | State bug — unstable keys | `grammar-lesson-editor.tsx:205,248,371,438`; `ConversationTemplateEditor.tsx:284` | `key={i}` + remove-by-index `filter((_,idx)=>idx!==i)` → deleting a middle row shifts keys, wrong input keeps focus/value | Key by stable id (`crypto.randomUUID()` on create) |
| **FE-021** | Loading — infinite spinner | `app/listening/lessons/page.tsx:42`; `app/listening/strategies/page.tsx:46` | `.catch(()=>{})` swallows error, only sets `loading:false` → "Loading…" forever on API failure | Add `error` state + `ErrorState` with retry |
| **FE-022** | Perf — eager signalr | `contexts/notification-center-context.tsx:3` (via `app-shell.tsx:165`) | `@microsoft/signalr` value-imported in a provider mounted on ~every authed page → shared first-load bundle everywhere | Lazy `await import('@microsoft/signalr')` in connect effect; `import type` only |
| **FE-023** | Perf — recharts not lazy | `app/progress/page.tsx:5`, `expert/metrics:4`, `CriteriaRadar.tsx:3`, `readiness-trend-chart.tsx:3`, +9 | recharts (~120kB) static into 13+ routes despite `components/charts/dynamic-recharts.tsx` | Route chart leaves through the dynamic wrapper |
| **FE-024** | Perf — client overuse | 514/555 `page.tsx` are `'use client'`; `app/layout.tsx:16` force-dynamic | Static display pages (e.g. `app/exam-guide/page.tsx`, zero state) ship full client trees; no static/ISR | Push client boundary below the shell |

### New Medium

| ID | Category | File(s) | Symptom → Fix |
|---|---|---|---|
| FE-025 | API 404 | `app/admin/ai-assistant/config/page.tsx:56,74` | `/v1/admin/ai-assistant/config` doesn't exist → 404 on load/save. Add route or remove page |
| FE-026 | API method | `lib/api.ts:9899` | `updateMarketplaceProfile` PUT vs backend `MapPatch` → 405. Change to PATCH |
| FE-027 | Dead feature | `app/community/threads/[threadId]/edit/page.tsx:97`, `[threadId]/page.tsx:242`, `threads/my/page.tsx:111` | Thread edit/delete PUT/DELETE with no backend route → 404/405. Add handlers or hide UI |
| FE-028 | Proxy bypass | `lib/api.ts:9357,9403` | Admin TTS/voice preview fetch `${NEXT_PUBLIC_API_BASE_URL}/v1/…` directly → 404 proxy-only. Route via `apiClient` |
| FE-029 | Error handling | `lib/materials-api.ts:114`, `listening-pathway-api.ts:296`, `reading-pathway-api.ts:395`, +6 | Raw fetch, plain `Error` (drop status), no retry, not shared `ApiError` → `isApiError`/retry broken. Migrate to `apiClient` |
| FE-030 | Toasts double | `app/admin/rulebooks/page.tsx:359`, `study-plan-templates/page.tsx:260` (+ root `providers.tsx:82`) | Sonner renders each toast in every mounted `<Toaster>` → toasts twice. Delete page-level Toasters |
| FE-031 | Scroll-lock leak | `components/ui/modal.tsx:225,378` | Body overflow reset to `''` (not prev), no refcount → bg scrolls when stacked overlays close. Capture/restore + refcount |
| FE-032 | Responsive | `AiAssistantPanel.tsx:39` (`w-[400px]`), `notification-center.tsx:370` (`w-[24rem]`), `grammar-cards.tsx:71` (`grid-cols-3`), `admin-pronunciation-drill-form.tsx:52,85`, `SpeakingStructureEditor.tsx:208`, `AnnotationLayer.tsx:357`, `metric-grid-2x2.tsx:15`, `InvoiceTable.tsx:40` | Clip/cramp on mobile. Add `max-w`, `grid-cols-1 sm:`, `overflow-x-auto` |
| FE-033 | Loading/empty/error | `app/reading/page.tsx:296`, `app/billing/page.tsx:146`, leaf `[id]` routes lack `error.tsx`, generic copy `app/progress/page.tsx:97` | Add section skeletons, leaf `error.tsx`, specific error copy |
| FE-034 | Forms valid/a11y | `reset-password:104` (no Enter), `settings/[section]:730` (no `aria-invalid`), `tutor/profile:96` (no required), `mfa-challenge-form:92` (no autofocus/`aria-live`), `coupons/[code]:248,264` (no date-order/bounds), `forgot-password:72` | Add validation, inline errors, `aria-invalid`/`aria-live`, Enter, autofocus-on-error |
| FE-035 | A11y | `CanonViolationCard.tsx:90` (`text-navy dark:text-navy`), `achievement-toast.tsx:72` (muted dark), `subtest-switcher.tsx:24` (tab no arrows/`aria-controls`), `mfa-*:93/178` (label no `htmlFor`), `register/success:105` (H4 w/o H2), `CartDrawer.tsx:84` (no inert), `AnnotationLayer.tsx:441` (button-div no `aria-label`) | Fix contrast, keyboard nav, labels, heading order, `inert`/`aria-label` |
| FE-036 | DS duplication | `Card` surface inline in ~50 files (`cart/CartPageView.tsx:206,225,278,320,382`, `pricing/*`, `billing/*`); 4 KPI/stat tiles (StatCard/KpiTile/PulseTile/metric-grid); `Button asChild` unused in 6+ link sites (`ContentLockedNotice.tsx:61`, `learner-empty-state.tsx:30`); `AddonPurchaseModal.tsx:71` hand-rolls modal (loses focus-trap); 83 raw `<input>`; skeleton radius `rounded-[24px]`≠Card `rounded-2xl` | Consolidate onto shared primitives |
| FE-037 | Fetching leaves | `readiness-delta-banner.tsx:24`, `profession-remediation-callout.tsx:16`, `dashboard-addons-widget.tsx:34` (~30 `domain/**`) | Presentational leaves own API+polling → untestable, refetch on mount. Lift to hooks/parents |
| FE-038 | Perf bundling | `next.config.ts` (no `optimizePackageImports`; `lucide-react` in 250 files); `components/domain/index.ts:17` barrel re-exports wavesurfer (118 importers) | Add `optimizePackageImports`; split heavy exports out of barrels |
| FE-039 | Tokens (ext FE-009) | OET navy `#0E2841`/teal `#156082` (`addon-purchase-modal.tsx:133`, `dashboard-addons-widget.tsx:112`, `WritingEditorV2.tsx:229`); auth react-select indigo (`country-code-select.tsx:53,66,170`); chart color collision (`readiness-trend-chart.tsx:13` overall+listening both `#4f46e5`) | Add `--color-oet-navy/teal`; CSS-var react-select; shared `chartPalette` |

### New Low

| ID | Category | File(s) | Symptom → Fix |
|---|---|---|---|
| FE-040 | Icon split | 11 auth files use `@tabler/icons-react`; 697 use `lucide-react` | Two icon bundles. Standardize auth on lucide |
| FE-041 | Assets | `manifest.json:36,43` missing `/screenshots/*` (PWA 404); `public/brand/oet-square-logo.png` 2.6 MB dead; `icon-512.png` 240 KB | Add/remove screenshots; delete/compress dead logo |
| FE-042 | Z-index/toasts | `modal.tsx:249,405` `z-50` vs `notification-center.tsx:435` `z-[100]`; 0/58 `toast()` have stable `id` | z-index token scale; add `{id}` to de-dupable toasts |
| FE-043 | Perf minor | `audio-player-waveform.tsx:4` (wavesurfer eager), `country-code-select.tsx:10` (react-select eager on register), 40+ `MotionItem delayIndex={i}` lists unvirtualized (mostly bounded) | `next/dynamic`; drop per-row motion / virtualize unbounded feeds |

### Clean passes (positive — no findings)

- **Routing/navigation:** all nav hrefs resolve; no dead links, no orphans, no role leaks; redirects + post-login `next` are open-redirect-safe; not-found/loading coverage good. (INFO: sidebar vs top-nav prefix-match normalization differs but is currently safe.)
- **Performance already-optimized (don't touch):** tiptap, pdfjs-dist, @zoom/meetingsdk, driver.js lazy; `next/font` (swap/subsets); `next/image` width/height; learner+admin `DataTable` virtualization/pagination; context values memoized.
- **Security/privacy** (§G): clean.

### Updated severity tally (Parts 1 + 2)

- **Critical: 3** (FE-001 logout leak, FE-017 cart, FE-018 checkout)
- **High: 8** (FE-002, 003, 019, 020, 021, 022, 023, 024)
- **Medium: 24** (FE-004–010, 013, 015, 025–039)
- **Low: 10** (FE-011, 012, 014, 016, 040–043)
- **Clean passes: 3** (security, routing, perf-already-optimized)

> ⚠️ FE-017/018 sit in the **billing area under active backend edits** — some DTOs may already be
> shifting; re-verify against the current `ICartService`/`BillingCheckoutEndpoints` before fixing.

**Remediation:** see **`FRONTEND_FIX_PLAN.md`** for the wave-by-wave fix design for every FE-0xx.

---

# Part 3 — Remediation Status

Implemented as small, verified commits (lint + `tsc` + affected tests green), now on branch
**`fix/frontend-remediation-cont`** — created in an isolated git worktree off the team's
`fix/liveclass-tutor-idor` (so it carries all this work + the cart fix) and worked there to avoid
disturbing a parallel session active in the same checkout. Findings were **re-verified before fixing**;
several turned out to be false positives / already-fixed / backend gaps and are recorded honestly.

### ✅ Fixed & committed (~28)
FE-001 (logout state leak +test) · FE-002 (coach realtime fallback) · FE-003 (tutor-book port) ·
FE-004 (mock-bookings "sample data" banner) · FE-005 (reminders fake-save → honest gate) ·
FE-006 #1 (`useApiMutation` infra + listening read flows → `useQuery`, +test) · FE-008 (conversation
reduced-motion, partial) · FE-009/039 (brand/OET/gold tokens) · FE-011 (admin billing inline save
errors) · FE-012 (pdf measurement-image alt) · **FE-017 (cart contract: `mapCart` + `cartId` threading
— tsc-verified; needs a live smoke-test)** · FE-019 (campaigns base path + drop unsupported actions) ·
FE-021 (listening error states) · FE-022 (lazy SignalR) · FE-025 (ai-config honest "not built" state) ·
FE-026 (marketplace PATCH) · FE-027 (hide community edit/delete — no backend route) · FE-029 (7 api
modules → shared `apiClient`/`ApiError`) · FE-030 (dup Toasters) · FE-031 (ref-counted scroll-lock) ·
FE-032 (AI panel + grid/popover/table mobile fixes) · FE-036 (Card consolidation, 5 exact-match
surfaces) · FE-038 (optimizePackageImports) · FE-041 (broken PWA screenshots).

### 🟡 Re-assessed — not a defect / already resolved
- **FE-035 CanonViolationCard** — FALSE POSITIVE (already `dark:text-white`).
- **FE-032 metric-grid-2x2** — 2-col tiles are mobile-fine; no change.
- **FE-028 TTS proxy bypass** — already resolved upstream (uses `apiBlobRequest`/proxy now).
- **FE-020 key={index}** — controlled inputs → focus glitch, not value corruption; safe fix is a
  content-editor `_uid` refactor best done with live testing.
- **FE-029 reading-authoring-api** — its migration was reverted (it regressed a results-page test); the
  other 7 modules migrated cleanly.

### 🔴 Needs the live browser (reconnect the Chrome extension)
- **FE-017/018** — a ~2-min cart→checkout click-through to confirm the contract fix end-to-end.
- The per-route **runtime/responsive/console/keyboard sweep**; **FE-007** (axe vs the dev server);
  **FE-033/034** (live state/form checks); **FE-014** dark-mode, **FE-023** charts-lazy, **FE-040** icon
  unify — all visual; must be *seen* to verify (esp. the recharts composed-chart child-type risk).

### 🟠 Backend-dependent / large / risky (not safely doable headless)
- **FE-010** — reading cooldown/duration/errorBankCleared need backend DTO fields.
- **FE-024** client-boundary (514 `'use client'` pages); **FE-006** / **FE-036** remainder — multi-day, verified passes.
- **FE-016** `middleware`→`proxy` (owns CSP/CSRF — needs care + a live check); **FE-013** per-page
  metadata (auth/learner pages are client components → needs a server-wrapper refactor).
- **FE-042** z-index scale + toast ids — low value.

---

# Part 4 — Live Runtime Sweep (browser-verified)

Drove a real (headless) browser against the app on the Podman stack — the runtime sweep Parts 1–3
deferred for lack of a connected browser. Three new findings, all **fixed and verified live**. The two
code fixes are committed on `fix/frontend-remediation-cont`; the config fix is local-only
(`.env.local`, gitignored).

| ID | Sev | Category | File(s) | Symptom → Root cause | Fix | Verification |
|---|---|---|---|---|---|---|
| FE-044 | **High (dev/ops)** | Config | `.env.local` (main + worktree) | **"localhost not working"**: page shell loads but *every* browser API call 500s. The browser uses the same-origin proxy `/api/backend/*` (`lib/env.ts:15`); the proxy route (`app/api/backend/[...path]/route.ts:29`) reads `API_PROXY_TARGET_URL`, which was **unset** → fell back to the native-dev default `http://127.0.0.1:5198` (dead in the Podman setup; API is on :8080) → `ECONNREFUSED 5198` | Add `API_PROXY_TARGET_URL=http://127.0.0.1:8080` to `.env.local` (gitignored — re-add after env resets). `.env.example` still advertises `5198` (native); update if standardizing on Podman | **Live**: `GET /api/backend/v1/auth/catalog/signup` **500 → 200** (2282 B JSON); `/register` renders with populated catalog pickers; 0 console errors; 0 failed requests |
| FE-045 | **Medium** | PWA/assets | `public/sw.js:18-23,202-203`; `public/notification-worker.js:19-20` | SW precached `/icon.svg` in `APP_SHELL_URLS`, but no `icon.svg` exists (only `icon-192/512.png`). `cache.addAll()` rejects atomically on any 404 → **the SW install fails entirely** → offline/PWA caching never activates; push icon/badge also broken | Point all 3 references at the existing `/icon-192.png` | `GET /icon.svg → 404` observed live; asset `/icon-192.png` confirmed present; commit `1f0d1c9b`; lint+tsc+1865 tests green |
| FE-046 | **Medium** | Motion/A11y (extends FE-008) | `components/ui/motion-primitives.tsx` (`MotionReveal`) | Hydration mismatch on every `(auth)` page **under `prefers-reduced-motion`**: the `hidden` variant shape-shifts by reduced-motion (`{opacity:0}` vs `{opacity:0,y,scale}`), but the server can't read the client preference → SSR emits the transform, the reduced-motion client emits opacity-only → React *"tree hydrated but … didn't match"* (flooded console) | Route `reducedMotion` through the file's existing `useSyncExternalStore` hydration gate (already used for `runtimeKind`) so SSR + first client render agree, then adopt the real preference post-hydration. Non-reduced users unaffected | **Gold-standard live**: reproduced on unfixed `:3000`; **zero** new hydration errors on fixed `:3001` (same reduced-motion browser; forgot-password + sign-in); commit `41b0985f`; tsc + 1865 tests green |

### Public-page sweep — clean
`/sign-in`, `/register`, `/forgot-password` render correctly with real catalog data, no console errors,
no failed requests (after FE-044). Register multi-step wizard, password/email inputs, and back-links present.

### FE-047 (low / likely dev-only) — hidden duplicate of the auth form post-hydration
`/sign-in` renders **two** `<form>` / `#email` / `<h1>` client-side, yet the SSR HTML has exactly one
(verified via `curl`); the second copy sits inside React's streaming Suspense segment
`<div hidden id="S:0">` (`display:none`). Root cause: `app/(auth)/sign-in/page.tsx` is `'use client'`
and calls `useSearchParams()` at the top level **without an explicit `<Suspense>`**, so the whole page
suspends and the streamed segment lingers. Impact is low — the duplicate is invisible and
screen-reader-ignored — and it reproduces identically on fixed (:3001) and unfixed (:3000) code, so it
is **unrelated to FE-046**. Likely a Turbopack-dev streaming artifact; **verify against a production
build** before acting. If confirmed real, wrap the search-param read in `<Suspense>` (also silences the
Next `useSearchParams` CSR-bailout warning); the same pattern recurs on other `'use client'` +
`useSearchParams` pages.

### Still blocked / deferred (honest)
- **Authenticated runtime verification** (cart FE-017/018, dashboards, expert/tutor/admin) needs login —
  the agent cannot type passwords, so these must be verified by a human in their own browser at
  `localhost:3000` (now working).
- **FE-016** `middleware`→`proxy`: deferred — `middleware.ts` owns app-wide CSP-nonce / CSRF /
  auth-redirects; a wrong Next-16 migration white-screens the whole app to silence a non-urgent
  deprecation. Needs explicit sign-off + a focused CSP/CSRF live check.
- **FE-040** icon-bundle unify (subtly changes icon visuals — design), **FE-013** per-page metadata,
  **FE-024** client-boundary refactor, **FE-006/FE-036** remainder, **FE-010** backend DTO fields —
  unchanged from Part 3 (large / design-affecting / backend-dependent).
