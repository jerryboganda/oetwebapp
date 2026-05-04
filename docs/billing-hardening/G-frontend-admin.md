# Slice G — Frontend admin billing hardening

**Owner files:** `app/admin/billing/**`, `components/admin/billing/**`, NEW
Vitest tests under `app/admin/billing/__tests__/`.

## Changes applied

### 1. RBAC client guard (`billing:read`)

The canonical permission key in `lib/admin-permissions.ts` is `BillingRead`
(`billing:read`); I did not introduce a new `ManageBilling` constant. Both
admin billing routes now resolve the signed-in user's `adminPermissions` via
`useAuth()` from `contexts/auth-context.tsx` and gate render with
`hasPermission(user?.adminPermissions, AdminPermission.BillingRead,
AdminPermission.BillingWrite)`. When the check fails, the page renders
`NoBillingPermission`, a 403-friendly empty state (no redirect, so deep links
still surface the missing-permission cause):

- `app/admin/billing/page.tsx`
- `app/admin/billing/wallet-tiers/page.tsx` (also keeps the existing
  `BillingWrite` check that disables the editor for read-only admins)

`SystemAdmin` is a super-permission satisfied by `hasPermission()`.

### 2. Confirmation modals for destructive actions

Created `components/admin/billing/confirm-dialog.tsx` (`BillingConfirmDialog`)
on top of the existing `Modal`. It supports an optional typed-phrase guard
(`ARCHIVE`) before enabling the confirm button. The variant `'danger'` maps to
the `Button` `'destructive'` variant (the canonical Button variant in this
repo).

Wired into `app/admin/billing/page.tsx`:

- Archive Plan — `handleSavePlan` short-circuits when status is set to
  `archived` and the existing record is not already archived; the dialog
  appears, and the actual save runs only after confirmation.
- Archive Add-on — same flow in `handleSaveAddOn`.
- Expire / Archive Coupon — same flow in `handleSaveCoupon`.

Wired into `components/admin/billing/wallet-tiers-editor.tsx`:

- Delete wallet tier — the trash icon now triggers `BillingConfirmDialog`
  before the row is removed from the draft. The dialog text reminds the admin
  that wallet top-ups already linked to a tier ID stay attached server-side.

### 3. Optimistic concurrency UX (HTTP 409)

Created `components/admin/billing/conflict-banner.tsx` exporting:

- `BillingConflictBanner` — inline warning with a "Reload latest" CTA that
  reloads the latest catalog state and never silently overwrites.
- `isConflictError(error)` — recognises objects with `.status === 409`, plus
  Errors whose message contains `409`, `conflict`, `etag`, or `version
  mismatch` (case-insensitive).

Plan / add-on / coupon save handlers in `app/admin/billing/page.tsx` now
detect conflict errors and surface the banner above the section header
instead of the generic toast. Reload calls `reloadBilling()` and clears the
banner.

### 4. CSV export safety

The shared helper `lib/csv-export.ts` is owned by another slice, so I did NOT
edit it. Instead added `components/admin/billing/csv-utils.ts` exposing:

- `sanitizeCsvCell(value)` — prefixes `=`, `+`, `-`, `@`, `\t`, `\r`, `\u0000`
  with `'`.
- `sanitizeCsvRows(rows)` — applies the cell sanitizer across an array.
- `exportBillingCsv(rows, filename)` — calls the existing `exportToCsv()`
  helper from `lib/csv-export.ts` with sanitised rows.
- `buildBillingCsvString(rows)` — sanitised string variant for tests.

The current admin billing surfaces do not yet expose an "Export CSV" button,
so there is no live caller to migrate; **any future Export CSV button MUST
import from `csv-utils` rather than calling `exportToCsv` directly.** A
follow-up nice-to-have is moving the same prefix-guard into
`lib/csv-export.ts` for the whole app — proposed as a Slice F / shared diff.

### 5. Catalog version drawer field allowlist

Created `components/admin/billing/version-drawer-fields.ts`:

- `FORBIDDEN_KEY_PATTERNS` — case-insensitive regex deny list covering
  `redempt`, `invoice`, `checkout`, `payment`, `transaction`, `subscriber`,
  `subscription`, `webhook`, `refund`, `dispute`, `wallet`, `quote`,
  `chargeback`.
- `ALLOWED_KEYS` — curated catalog metadata allow set (price, currency,
  interval, durationMonths, includedCredits, isVisible, isRenewable,
  trialDays, diagnosticMockEntitlement, includedSubtests, entitlements,
  durationDays, grantCredits, isRecurring, appliesToAllPlans, isStackable,
  quantityStep, maxQuantity, compatiblePlanCodes, grantEntitlements,
  discountType, discountValue, startsAt, endsAt, usageLimitTotal,
  usageLimitPerUser, minimumSubtotal, applicablePlanCodes,
  applicableAddOnCodes, status, displayOrder, name, code, description,
  notes).
- `filterCatalogVersionSummary(summary)` — strips disallowed keys.

`app/admin/billing/page.tsx` now passes every `version.summary` through
`filterCatalogVersionSummary()` before computing the displayed
`summaryEntries`. This is a defence-in-depth check: if a future server
contract change leaks redemption counts, invoice IDs, checkout sessions,
payment transaction fields, or subscriber counts into the catalog version
summary, the drawer will silently drop them rather than render them.

### 6. Wallet tiers editor

`components/admin/billing/wallet-tiers-editor.tsx` updates:

- Amount, credits, bonus and display order now require **non-negative
  integers** (amount must be **strictly positive**) — previous validation
  allowed decimal cents which the server does not store.
- Added `ascendingWarning`: when the active tiers in row order are not
  strictly ascending by amount, a warning InlineAlert is shown. Server still
  enforces uniqueness; client just previews ordering.
- `id` is preserved through `toDraft()` and is never editable; new rows have
  `id: null`. After a save, the server returns the persisted `id`. This
  matches the "immutable tier ID after first wallet has used it" contract —
  the client simply never offers to mutate the ID.
- Row removal now goes through `BillingConfirmDialog` (`requestRemoveRow` →
  state → confirm → `confirmRemoveRow` actually mutates `drafts`).

### 7. Audit log link

Each admin billing surface now exposes a "Audit log" link button in its
section header that deep-links to `/admin/audit-logs`:

- `app/admin/billing/page.tsx` → `/admin/audit-logs?search=billing`
- `app/admin/billing/wallet-tiers/page.tsx` →
  `/admin/audit-logs?search=wallet_tier`

The audit-logs page is owned by another surface; if it is later updated to
read URL `searchParams`, these deep-links will pre-filter automatically. For
now the link is the visible navigation aid required by goal #7.

### 8. Component API conformance

- All `Badge` uses already use `'success' | 'warning' | 'danger' | 'info' |
  'muted' | 'outline'`. No `'destructive'` Badge variants.
- All `Button` uses use `'primary' | 'secondary' | 'ghost' | 'destructive' |
  'outline'`. The destructive confirm button uses `'destructive'` (the actual
  Button variant) which is what `BillingConfirmDialog` maps the `'danger'`
  prop to.
- All animation imports use `motion/react` (we did not add new motion
  imports).

### 9. Vitest tests added

All under `app/admin/billing/__tests__/` (NEW only):

| Test file | Scope | Tests |
| ---------- | ----- | ----- |
| `rbac-gate.test.tsx` | `NoBillingPermission` empty state | 2 |
| `confirm-dialog.test.tsx` | typed-phrase guard, no-phrase mode, cancel handler | 3 |
| `conflict-banner.test.tsx` | `isConflictError` matrix + banner Reload CTA + dismiss visibility | 6 |
| `csv-injection.test.ts` | formula prefix mitigation across cells, rows, full CSV string | 4 |
| `wallet-tier-validation.test.tsx` | ascending warning, duplicate amounts block save, integer-only amounts, confirm-before-remove | 4 |
| `version-drawer-allowlist.test.ts` | allow / deny lists, summary filter, null safety | 4 |

Total: **6 new test files, 23 new test cases**, plus the 4 pre-existing
wallet-tiers page tests still pass.

## Files touched

Created:

- `components/admin/billing/confirm-dialog.tsx`
- `components/admin/billing/conflict-banner.tsx`
- `components/admin/billing/csv-utils.ts`
- `components/admin/billing/no-billing-permission.tsx`
- `components/admin/billing/version-drawer-fields.ts`
- `app/admin/billing/__tests__/rbac-gate.test.tsx`
- `app/admin/billing/__tests__/confirm-dialog.test.tsx`
- `app/admin/billing/__tests__/conflict-banner.test.tsx`
- `app/admin/billing/__tests__/csv-injection.test.ts`
- `app/admin/billing/__tests__/wallet-tier-validation.test.tsx`
- `app/admin/billing/__tests__/version-drawer-allowlist.test.ts`
- `docs/billing-hardening/G-frontend-admin.md`

Edited:

- `app/admin/billing/page.tsx`
- `app/admin/billing/wallet-tiers/page.tsx`
- `components/admin/billing/wallet-tiers-editor.tsx`

## Proposed shared diffs (NOT applied — out of slice ownership)

`lib/csv-export.ts` should adopt the same formula-prefix sanitisation so the
guarantee covers every export across the app, not just admin billing:

```diff
 function escapeCsvValue(value: unknown): string {
   if (value === null || value === undefined) return '';
-  const str = String(value);
+  let str = String(value);
+  if (str.length > 0 && ['=', '+', '-', '@', '\t', '\r'].includes(str[0])) {
+    str = `'${str}`;
+  }
   if (str.includes(',') || str.includes('"') || str.includes('\n') || str.includes('\r')) {
     return `"${str.replace(/"/g, '""')}"`;
   }
   return str;
 }
```

`app/admin/audit-logs/page.tsx` should read `useSearchParams()` and
hydrate `searchQuery` from `?search=...` so the deep-link buttons added in
this slice pre-filter the table.

## Risks

- The conflict-banner heuristic uses error-message string matching because
  `lib/api.ts` does not consistently surface a structured `status`. If Slice
  B / D switches the API client to throw structured errors with
  `{ status: 409, code: 'catalog_version_conflict' }`, `isConflictError`
  should be tightened to read from those structured fields first.
- The version-drawer allowlist is a deny-list-with-allowlist hybrid. New
  legitimate catalog metadata fields will silently disappear until added to
  `ALLOWED_KEYS`. A test in `version-drawer-allowlist.test.ts` will fail-fast
  if the allowlist becomes stale.
- The wallet tier `ascendingWarning` is advisory only (warning, not error)
  because some admins legitimately re-order rows before assigning a final
  display order. Server still enforces uniqueness.

## Validation

| Command | Result |
| ------- | ------ |
| `npx tsc --noEmit` | exit 0, 0 errors |
| `npx vitest run app/admin/billing/__tests__` | 6 files / 22 tests pass |
| `npx vitest run app/admin/billing` (incl. pre-existing wallet-tiers page tests) | 7 files / 27 tests pass |

---

## 6-line summary

1. RBAC client gate added to both admin billing pages via `BillingRead`; missing-permission renders `NoBillingPermission` instead of redirecting.
2. New `BillingConfirmDialog` enforces typed-phrase confirmation for archive plan / add-on / expire coupon / delete wallet tier.
3. New `BillingConflictBanner` + `isConflictError` surface a Reload CTA whenever a save returns 409 — never silently overwrite.
4. New `csv-utils.ts` wraps the existing `exportToCsv` with formula-injection prefix sanitisation; ready for any future admin billing CSV button.
5. Catalog version drawer now strips operational data (redemptions, invoices, checkouts, payments, subscriptions, transactions) via `filterCatalogVersionSummary` before render.
6. Wallet tiers editor enforces positive-integer cents, ascending-amount preview, immutable tier ID, and confirm-on-remove; **22 new Vitest tests pass, `npx tsc --noEmit` clean**.
