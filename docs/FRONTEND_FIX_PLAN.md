# Frontend Fix Plan — Fix Every Gap (FE-001 … FE-043)

Companion to `FRONTEND_AUDIT_REPORT.md`. Plan to remediate **every** finding. *(Git-untracked — commit
it so `git clean` doesn't remove it.)*

## Principles
- **Branch per wave** (`fix/fe-wave-N`), never `main`. **Small reviewable commits**, one issue-cluster
  each, tree stays green (`lint` + `tsc --noEmit` + `test`) at every commit.
- **Reuse-first** — existing primitives: `InlineAlert`/`ErrorState`/`EmptyState` (`components/ui`),
  `Badge`, `Modal`/`Button asChild`, `lib/motion.ts`, `queryKeys` + `apiClient`/`ApiError`, axe `runAxe`.
- **No redesign**; preserve brand/design language.
- **Verify every fix**: unit (Vitest) + targeted Playwright e2e/a11y (API is up now) + live spot-check.
- **Flag backend-coordination**: contract findings can't be "frontend-only".

## Backend-coordination matrix (resolve before/with the FE fix)
| Finding | Needs backend? | Note |
|---|---|---|
| FE-017 cart, FE-018 checkout | **Likely FE-only mapping**, but billing DTOs are in active flux — re-verify current `ICartService`/`BillingCheckoutEndpoints` first |
| FE-019 campaigns, FE-025 ai-config, FE-027 community edit/delete | **Backend** | Routes missing/renamed — confirm canonical path or remove FE |
| FE-026 marketplace | FE-only | PUT→PATCH |
| FE-005 reminders | Confirm | Does `PATCH /v1/settings/notifications` accept the fields? |
| FE-010 reading | Backend (later) | DTO fields (cooldown/duration/errorBankCleared) |
| FE-002 realtime | Confirm | Prod WS-reachable origin |

---

## Wave 0 — Shared infra (unblocks many fixes; low risk)
| For | Action | Files | Verify |
|---|---|---|---|
| FE-001 | `resetAllStores()` registry; stores self-register `reset()` | new `lib/stores/registry.ts`; `lib/stores/expert-store.ts` (+`persist.clearStorage()`); `lib/writing/store.ts` | unit |
| FE-015 | Shared date/number formatter (explicit locale+tz) | new `lib/domain/datetime.ts` | unit |
| FE-038 | `experimental.optimizePackageImports: ['lucide-react','@tabler/icons-react','recharts','motion','@radix-ui/*']` | `next.config.ts` | `pnpm build` (bundle ↓) |
| FE-009/039 | Add `@theme` tokens: gold (`#996F1F/#D4A44F`), `--color-oet-navy #0E2841`, `--color-oet-teal #156082`; export `chartPalette` | `app/globals.css`; new `lib/domain/chart-palette.ts` | build |
| FE-006 | Extend `queryKeys` namespaces; add a `useApiMutation` helper that invalidates | `lib/query/hooks.ts` | unit |

---

## Wave 1 — Critical correctness & safety
| ID | Fix approach (reuse-first) | Files | Test/verify |
|---|---|---|---|
| **FE-001** | In `signOut().finally`: `useQueryClient().clear()` + `resetAllStores()` (AuthProvider is inside QueryProvider) | `contexts/auth-context.tsx` (+ Wave-0 registry) | Vitest: seed cache+drafts+localStorage → signOut → all cleared (resolve & reject paths). **e2e:** expert login→draft→logout→login→gone |
| **FE-017** | Align `Cart`/`CartLineItem`/`CartPromoCode` interfaces+mappers to backend DTOs; thread `cartId` into item/promo mutations; send `billingPriceId` in `addCartItem` | `lib/api.ts:13592-13673`, `components/cart/*` | unit (mapper); **live cart e2e** against API |
| **FE-018** | Reconcile checkout req/resp DTOs; pass valid `cartId`; fix native bridge to cart-based flow | `lib/api.ts:13686-13714`, `lib/native/billing-bridge.ts:276` | unit; **live checkout e2e** |
| **FE-002** | Browser: derive realtime base from same-origin/explicit env, fail-loud (no `127.0.0.1`); move token off URL query if backend allows | `lib/writing/realtime.ts` | unit on resolver; build (no localhost in bundle) |
| **FE-003** | Use module `API_BASE_URL` (drop `:5199` fallback) | `lib/api.ts:5632` | unit asserts `/api/backend`, no `5199` |
| **FE-021** | Add `error` state + `ErrorState` (retry) in catch | `app/listening/lessons/page.tsx`, `strategies/page.tsx` | render test: error → ErrorState |

---

## Wave 2 — High: API contract + state bug + perf-structural
| ID | Fix approach | Files | Test/verify |
|---|---|---|---|
| **FE-019** | Base `/v1/admin/campaigns`; map actions to `approve/cancel/send/evaluate-segment` | `app/admin/notifications/campaigns/page.tsx` | e2e: list/create/action 2xx |
| **FE-020** | Stable ids on items (`crypto.randomUUID()` at create); `key={item.id}` | `grammar-lesson-editor.tsx`, `ConversationTemplateEditor.tsx` | unit: delete middle row keeps others' values/focus |
| **FE-022** | Lazy `await import('@microsoft/signalr')` in connect effect; `import type` only | `contexts/notification-center-context.tsx`, `hooks/use-ai-assistant.ts`, `lib/ai-assistant/signalr.ts` | build (signalr out of shared chunk) |
| **FE-023** | Route every chart leaf via `components/charts/dynamic-recharts` / `next/dynamic({ssr:false})` | `app/progress/page.tsx`, `CriteriaRadar.tsx`, `readiness-trend-chart.tsx`, `BandHistoryChart.tsx`, `EarningsChart.tsx`, +sites | build (recharts split per-route) |
| **FE-024** | Push client boundary below the shell: server shell + small client `<MotionProvider>` island; convert zero-interactivity display pages to server components | `components/layout/learner-dashboard-shell.tsx`, static `app/*/page.tsx` (guide/reference/stats) | build (client JS ↓); visual unchanged |
| FE-025 | Remove page or add backend route; graceful-wrap call | `app/admin/ai-assistant/config/page.tsx` | e2e/no-404 |
| FE-026 | PUT→PATCH | `lib/api.ts:9899` | e2e 2xx |
| FE-027 | Add backend edit/delete (authz) OR hide UI controls | `app/community/threads/*` | e2e |
| FE-028 | Route TTS/voice preview through `apiClient` (Blob via proxy) | `lib/api.ts:9357,9403` | e2e (proxy-only) |
| FE-029 | Migrate raw-fetch modules to `apiClient`/shared `ApiError` (status + retry parity) | `lib/materials-api.ts`, `listening-pathway-api.ts`, `reading-pathway-api.ts`, +6 | unit: throws `ApiError` w/ status; retries 5xx |
| FE-043 | `next/dynamic` wavesurfer + `CountryCodeSelect` | `audio-player-waveform.tsx`, `country-code-select.tsx` | build |

---

## Wave 3 — Medium: forms, a11y, responsive, data-layer, placeholders, toasts/modals, tokens
| ID | Fix approach | Files | Test/verify |
|---|---|---|---|
| FE-004 | Visible "Sample data — preview" `Badge`+`InlineAlert` (or feature-flag) | `app/expert/mocks/bookings/page.tsx` | render: label present |
| FE-005 | Wire `fetchSettingsSection('notifications')`/`updateSettingsSection` (fire analytics only after resolve) **or** honest-gate; add `role="switch"`/`aria-checked` | `app/settings/reminders/page.tsx` | unit: save calls API/no fake toast |
| FE-006 | Migrate highest-churn flows to `useQuery`/`useApiMutation` + invalidate | reading/writing/listening/speaking pages | unit: mutation invalidates key |
| FE-010 | Extract `10`→constant; consume DTO fields when present | `app/reading/practice/page.tsx` | unit |
| FE-030 | Delete page-level `<Toaster/>` (keep root) | `app/admin/rulebooks/page.tsx`, `study-plan-templates/page.tsx` | render: single toast |
| FE-031 | Capture/restore previous `overflow` + shared lock refcount | `components/ui/modal.tsx` | unit: stacked open/close keeps lock |
| FE-032 | `max-w-[calc(100vw-…)]`, `grid-cols-1 sm:grid-cols-N`, `overflow-x-auto` | `AiAssistantPanel.tsx`, `notification-center.tsx`, `grammar-cards.tsx`, `metric-grid-2x2.tsx`, `InvoiceTable.tsx`, +3 | live responsive @320/375/768 |
| FE-033 | Section skeletons; leaf `error.tsx`; specific error copy | `app/reading/page.tsx`, `app/billing/page.tsx`, `*/[id]/error.tsx` | render/e2e |
| FE-034 | Validation + inline errors + `aria-invalid`/`aria-live`/Enter/autofocus | reset-password, settings, tutor profile, mfa-challenge, coupons, forgot-password | unit + axe |
| FE-035 | Contrast pairs (`dark:` variants), `subtest-switcher` keyboard nav, label `htmlFor`, heading order, `inert`, `aria-label` | per finding | axe + manual keyboard |
| FE-036 | Replace inline `Card` surfaces with `<Card>`; `<Button asChild>` for links; `AddonPurchaseModal`→`<Modal>`; collapse KPI tiles; shared `Input` | ~50 files (incremental) | visual unchanged + a11y (modal focus-trap) |
| FE-037 | Lift `fetch` out of presentational leaves into hooks/parents | `readiness-delta-banner.tsx`, `dashboard-addons-widget.tsx`, ~30 files (incremental) | unit (pure leaf) |
| FE-039 | Replace hardcoded hexes with Wave-0 tokens; `chartPalette` (fix `#4f46e5` collision) | `addon-purchase-modal.tsx`, `country-code-select.tsx`, chart components | build/visual |
| FE-013 | Per-page `metadata`/`generateMetadata`; `robots`/`sitemap` if public | `app/(auth)/*`, learner module pages | build |
| FE-007 | Add `*.a11y.spec.ts` via `runAxe` for Reading/Writing/Listening/Dashboard/Billing/Auth/Admin | `tests/a11y/*` | axe green |
| FE-008 | Gate framer-motion via `useReducedMotion()`+`lib/motion.ts` | conversation/auth/dashboard components | Playwright `emulateMedia({reducedMotion:'reduce'})` |

---

## Wave 4 — Low / polish / forward-compat
| ID | Fix | Files |
|---|---|---|
| FE-011 | Inline `InlineAlert` errors + `disabled={saving}` | admin coupon/product forms |
| FE-012 | `image.alt` on measurement img; verify dense-table scroll | `reading-pdf-viewer.tsx`, `dense-table.tsx` |
| FE-014 | Add `dark:` variants (or confirm learner surface is light-only) | `learner-breadcrumbs.tsx`, `writing-review-queue.tsx`, `data-table.tsx` |
| FE-016 | Rename `middleware.ts`→`proxy` (Next 16) | `middleware.ts` |
| FE-040 | Standardize auth on `lucide-react`; drop `@tabler/icons-react` | `components/auth/*` |
| FE-041 | Add or remove `public/screenshots/*`; delete dead `oet-square-logo.png` | `public/manifest.json`, `public/brand/*` |
| FE-042 | z-index token scale (overlay<modal<drawer<popover<toast); add `{id}` to de-dupable toasts | `modal.tsx`, `notification-center.tsx`, toast call-sites |
| FE-WATCH | Regression tests for the two Writing V2 crash modes; live marking re-verify | `AiPreAnalysisPanel.tsx`, `RubricPanel.tsx` tests |

---

## Verification strategy
- **Per commit:** `pnpm lint`, `pnpm exec tsc --noEmit`, `pnpm test` (affected).
- **Per wave:** full `pnpm test`, `pnpm build`, targeted **Playwright e2e + a11y** (API is up), live browser spot-check at the viewport matrix.
- **Billing (FE-017/018):** live cart→checkout e2e against the running API; re-confirm DTOs first.
- **Perf (FE-022/023/024/038):** compare `pnpm build` route JS before/after; bundle-analyzer optional.

## Effort/sequencing note
Waves 0–1 are the safe, high-value start (correctness + safety, mostly frontend-only). Waves 2–4 are
larger and partly backend-coordinated (FE-019/025/027) or incremental refactors (FE-024/036/037). Each
wave is independently shippable.
