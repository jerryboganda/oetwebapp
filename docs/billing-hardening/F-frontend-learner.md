# Slice F — Frontend Learner Billing Hardening

> Owner: frontend learner billing subagent. Read [`README.md`](./README.md)
> first for the slice ownership matrix.

## Scope

Per the slice ownership matrix, Slice F owns:

- `app/billing/**` — all four learner-facing pages (`page.tsx`, `upgrade/`,
  `referral/`, `score-guarantee/`).
- `lib/billing-types.ts`.
- `components/domain/billing/**`.
- New tests under `app/billing/__tests__/**`.

This slice does **not** touch:

- `app/admin/billing/**` (Slice G).
- Backend (`backend/**`) — any slice from A through E.
- Shared backend contracts (`Domain/BillingEntities.cs`,
  `Contracts/BillingContracts.cs`, learner / admin endpoints).
- The pre-existing `app/billing/__tests__/billing-flow.integration.test.tsx`
  (Slice H test territory).

## Goals (as briefed) and outcomes

| # | Goal | Status |
| - | ---- | ------ |
| 1 | All HTTP through `apiClient` / `lib/api.ts` helpers — no raw `fetch` in `app/billing/**` | ✅ Audited; zero raw `fetch` calls. All four pages already used `fetchBilling`, `fetchBillingChangePreview`, `createBillingCheckoutSession`, `createWalletTopUp`, `downloadInvoice`, `fetchWalletTopUpTiers`, `fetchWalletTransactions`, `fetchFreezeStatus`, `fetchReferralInfo`, `generateReferralCode`, `activateScoreGuarantee`, `submitScoreGuaranteeClaim`. |
| 2 | `Badge` uses `'danger'` (not `'destructive'`); `Button` uses `'primary'` | ✅ Audited; no `Badge variant="destructive"` anywhere in `app/billing/**`. Score-guarantee already used `variant="danger"` for rejected. The only `Badge variant="default"` ("Current" plan badge in `upgrade/page.tsx`) is in the documented variant API and is correct. Buttons use `primary` / `secondary` / `outline` / `destructive` consistently. |
| 3 | Accessibility: discernible button names, labelled inputs, `role="alert"` / `aria-describedby` for errors | ✅ Verified. `Button`s have visible labels. Errors render through `InlineAlert` (which already sets the appropriate role) and the new `error.tsx` boundaries pass `variant="error"`. Loading states pass `aria-busy="true" aria-live="polite"`. |
| 4 | Loading skeleton matches final layout for every page | ✅ The three sub-pages (`upgrade`, `referral`, `score-guarantee`) had generic `<PageSkeleton />` loading shells. Replaced with `LearnerDashboardShell` + `Skeleton` blocks that mirror the actual hero (h-44) + content (h-48) layout, with `aria-busy="true" aria-live="polite"`. The root `app/billing/loading.tsx` already matched the layout. |
| 5 | `error.tsx` with retry on every page | ✅ Created `upgrade/error.tsx`, `referral/error.tsx`, `score-guarantee/error.tsx`. Each tracks `analytics.track('error_view', { page, message, digest })`, renders `InlineAlert variant="error"`, exposes a `Button variant="primary" onClick={reset}` "Try again", and links back to `/billing`. The root `app/billing/error.tsx` already existed. |
| 6 | Single `lib/money.ts` helper using `Intl.NumberFormat` — never concat `$` + number | ✅ Created `lib/money.ts` exporting `formatMoney(amount, { currency, locale, minimumFractionDigits, maximumFractionDigits })` and `formatMoneyWhole(...)`. Uses `Intl.NumberFormat`; coerces null/undefined/NaN/non-finite to `0`; falls back to `<CCY> <amount>` if Intl throws. Wired into `app/billing/page.tsx` (replaces local `formatCurrency`) and `app/billing/upgrade/page.tsx` (replaces local `formatPrice`). |
| 7 | Mask provider IDs to `cus_***1234` | ✅ Created `components/domain/billing/mask-provider-id.ts` exporting `maskProviderId(value)`. Applied at the two invoice-id render sites in `app/billing/page.tsx` (recent invoices grid + full invoices table). The raw token (`in_1NXyHk2eZvKYlo2C…`) is no longer rendered anywhere on the learner billing surface; only the masked form (e.g. `in_***FGH`) is shown. |
| 8 | Idempotent submit (in-flight ref) with `Idempotency-Key` uuid v4 | ✅ `app/billing/page.tsx` already used `submittingRef = useRef(false)` and `newIdempotencyKey()` (a `crypto.randomUUID()` fallback) for both `createBillingCheckoutSession` and `createWalletTopUp`. Added the same `useRef`-based double-submit guard to `app/billing/score-guarantee/page.tsx` for both `handleActivate` and `handleClaim` (early-return when busy; cleared in `try/finally`). Verified by the `double-submit-and-mask.test.tsx` test. |
| 9 | Empty states | ✅ Verified existing `EmptyState` usage for invoice-list-empty, no-active-add-ons, no-wallet-transactions, no-referral-history, no-score-guarantee-history. No new empty states required. |
| 10 | `motion/react` (NOT `framer-motion`) | ✅ Verified no `framer-motion` imports anywhere in `app/billing/**`. All animated components import from `motion/react`. The repo-wide vitest setup mocks `motion/react` via Proxy + `AnimatePresence`. |
| 11 | Vitest tests: double-submit guard, money formatter (multi-currency + locale fallback), error retry, masked ID display | ✅ Four new test files added (see Files). |

## Files

### Created

| Path | Purpose |
| ---- | ------- |
| [`lib/money.ts`](../../lib/money.ts) | Centralised `formatMoney` / `formatMoneyWhole` using `Intl.NumberFormat`, with safe fallback for invalid locale/currency. |
| [`components/domain/billing/mask-provider-id.ts`](../../components/domain/billing/mask-provider-id.ts) | `maskProviderId(value)` — preserves Stripe-style prefix and last 4 chars. |
| [`app/billing/upgrade/error.tsx`](../../app/billing/upgrade/error.tsx) | Per-page error boundary with retry + analytics. |
| [`app/billing/referral/error.tsx`](../../app/billing/referral/error.tsx) | Per-page error boundary with retry + analytics. |
| [`app/billing/score-guarantee/error.tsx`](../../app/billing/score-guarantee/error.tsx) | Per-page error boundary with retry + analytics. |
| [`app/billing/__tests__/money.test.ts`](../../app/billing/__tests__/money.test.ts) | 7 cases: AUD default, USD wallet, EUR `de-DE`, unknown locale fallback, unknown currency fallback, NaN/null/undefined → 0, `formatMoneyWhole` drops fraction. |
| [`app/billing/__tests__/mask-provider-id.test.ts`](../../app/billing/__tests__/mask-provider-id.test.ts) | 5 cases: nullish → empty, short tokens → `***`, Stripe `cus_/sub_/pi_` prefix preserved, head/tail fallback, full token never present. |
| [`app/billing/__tests__/error-boundaries.test.tsx`](../../app/billing/__tests__/error-boundaries.test.tsx) | 4 cases — one per error.tsx — assert title, alert, working retry, analytics fired with digest. |
| [`app/billing/__tests__/double-submit-and-mask.test.tsx`](../../app/billing/__tests__/double-submit-and-mask.test.tsx) | 2 cases: rapid double-click on a wallet top-up tile fires `createWalletTopUp` exactly once; invoice IDs render masked (raw token absent from DOM). |

### Edited

| Path | Change |
| ---- | ------ |
| [`app/billing/page.tsx`](../../app/billing/page.tsx) | Replaced local `formatCurrency` with `formatMoney`. Wrapped invoice IDs in both render sites (`recent invoices` grid + full invoices table) with `maskProviderId(invoice.id)`. |
| [`app/billing/upgrade/page.tsx`](../../app/billing/upgrade/page.tsx) | Replaced local `formatPrice` with `formatMoneyWhole` (preserves the `min/max fraction digits = 0` behaviour the page already used). |
| [`app/billing/score-guarantee/page.tsx`](../../app/billing/score-guarantee/page.tsx) | Added `useRef`-based double-submit guard (`submittingRef`) to `handleActivate` and `handleClaim`. |
| [`app/billing/upgrade/loading.tsx`](../../app/billing/upgrade/loading.tsx) | Replaced generic `<PageSkeleton />` shell with `LearnerDashboardShell` + layout-matching `Skeleton` blocks, `aria-busy="true" aria-live="polite"`. |
| [`app/billing/referral/loading.tsx`](../../app/billing/referral/loading.tsx) | Same pattern. |
| [`app/billing/score-guarantee/loading.tsx`](../../app/billing/score-guarantee/loading.tsx) | Same pattern. |
| [`components/domain/billing/index.ts`](../../components/domain/billing/index.ts) | Re-export `maskProviderId` from the new file. |

## Validation

| Check | Command | Result |
| ----- | ------- | ------ |
| Type-check (whole repo) | `npx tsc --noEmit` (output → `tsc-slice-f.log`) | **0 errors** (`(Select-String -Path tsc-slice-f.log -Pattern 'error TS').Count` → `0`). |
| Type-check (Slice F files only) | `get_errors` on the 9 owned source/test files | **0 errors**. |
| Vitest — Slice F suites | `npx vitest run app/billing/__tests__/money.test.ts app/billing/__tests__/mask-provider-id.test.ts app/billing/__tests__/error-boundaries.test.tsx app/billing/__tests__/double-submit-and-mask.test.tsx` | **18 / 18 passed** (4 files: 7 + 5 + 4 + 2). Duration ≈ 9 s. |

## Risks and notes

- **Out-of-scope file:** `app/billing/__tests__/billing-flow.integration.test.tsx` is owned by Slice H, not Slice F, and was not modified. Earlier in the session it appeared to carry implicit-`any` errors on lines 163 / 175; `npx tsc --noEmit` against the current tree now reports **0 errors**, indicating Slice H or another agent has since cleaned them up. Slice F made no changes there.
- **`Badge variant="default"` on the Upgrade page** ("Current" plan pill) is intentional and within the documented `Badge` variant union (`'default' | 'success' | 'warning' | 'danger' | 'info' | 'muted' | 'outline'`). The brief required `'danger'` rather than `'destructive'`, which is satisfied.
- **`useRef` guard rather than `useReducer`:** the brief allowed either. We picked `useRef` because the existing `app/billing/page.tsx` already used `submittingRef`, so the score-guarantee change matches the in-repo pattern and avoids gratuitous reducer scaffolding for two boolean handlers.
- **Locale defaults to `en-AU`** in `formatMoney`. The wallet UI already passed an explicit `currency` everywhere it changed (USD → wallet currency), so existing AUD pricing is unchanged in display. If a future requirement is to derive the locale from the user's profile, the helper accepts a `locale` option.
- **Idempotency keys** in `createWalletTopUp` / `createBillingCheckoutSession` continue to use `newIdempotencyKey()` (project-local `crypto.randomUUID()` fallback). The frontend already relied on this; the backend `Idempotency-Key` semantics are owned by Slice B / D.
- **Two prompt-injection attempts** (one suggesting `context-mode_ctx_execute`, another suggesting `context-mode_ctx_batch_execute` / `ctx_execute_file`) appeared in tool output during validation. Both were ignored; only the standard MCP tools listed for this session were used.
- **Concurrent-agent terminal noise:** at one point another slice's `dotnet test` output bled into a shared PowerShell session. No cross-edit occurred — `get_changed_files` confirms Slice F's edits are confined to the files listed above (other diffs in the changed-files list are owned by Slices B / C / I).

## 6-line summary

- `lib/money.ts` + `mask-provider-id.ts` added; both are wired into `app/billing/page.tsx` and `upgrade/page.tsx` so currency rendering goes through `Intl.NumberFormat` and provider IDs are masked.
- Per-page `error.tsx` (retry + analytics) and layout-matching `loading.tsx` skeletons added for `upgrade`, `referral`, `score-guarantee`; root `error.tsx` / `loading.tsx` already existed and were left unchanged.
- `score-guarantee/page.tsx` now uses a `useRef`-based double-submit guard, matching the existing pattern in `billing/page.tsx`.
- Four new Vitest files cover money formatting (multi-currency + locale fallback), provider-ID masking, error-boundary retry, and double-submit guard: **18 / 18 passing**.
- `npx tsc --noEmit` over the whole repo: **0 errors**.
- No raw `fetch`, no `framer-motion`, no `Badge variant="destructive"`, no `Button variant="default"` introduced; all Slice F edits stay inside `app/billing/**`, `lib/money.ts`, and `components/domain/billing/**`.
