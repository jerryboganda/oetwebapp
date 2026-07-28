# Quick Grant — one-click access presets

Date: 2026-07-29
Status: Approved

## Problem

Granting a candidate a narrow slice of access (e.g. "Recalls only") today requires
opening the full "Access & Allocation" panel on their profile page, understanding
four independent systems (packages, module toggles, per-module scope pickers,
expiry), and getting all of them right. That panel is powerful but not fast, and
it's the only path — there's no shortcut for the common cases.

## Goal

A "Quick Grant" popup that lets an admin apply one of a small set of presets
(Recalls Only, Materials Only, Videos Only, Full Access) in ~2 clicks, reachable
from both the Users list and a candidate's profile — without removing or
duplicating the existing full panel, which remains available as "Advanced" for
anything the presets don't cover.

## Existing building blocks (reused, not rebuilt)

- `lib/user-access.ts` — `UserAccess`, `UserAccessModuleOverride`, `MODULE_KEYS`,
  `fetchUserAccess`, `putUserAccessScope`, `grantUserPackage`, `fetchAdminBillingPlans`.
- `components/admin/user-access/{recall-set-picker,folder-scope-picker,video-scope-picker}.tsx`
  — the scope multiselects, reused as-is for the optional "Customize scope" expand.
- `components/admin/user-access/package-list.tsx` — source of the profession-mismatch
  check (`isProfessionMismatch`) reused when Quick Grant needs to activate a package.
- Backend semantics already confirmed by reading
  `EffectiveEntitlementResolver.cs` and `RecallsEndpoints.cs`: a per-user
  `UserModuleOverride` row is authoritative over the plan (enable beats plan-off,
  disable beats plan-on/fail-open), and Recalls/Mocks additionally require
  `HasEligibleSubscription` (some active, non-frozen package) regardless of the
  module flag. This is why presets always write all 4 module keys explicitly, and
  why the "no active package" case needs its own step.

## New pieces

### `lib/user-access-presets.ts`

Pure data + one helper, no UI:

```ts
export type PresetId = 'recallsOnly' | 'materialsOnly' | 'videosOnly' | 'fullAccess';

export interface AccessPreset {
  id: PresetId;
  label: string;
  icon: LucideIcon;
  description: string;               // one-line summary shown on the confirm step
  moduleOverrides: Record<ModuleKey, boolean>;  // all 4 keys, always explicit
  scopeField: 'recallSetCodes' | 'materialFolderIds' | 'videoIds' | null;
}

export const ACCESS_PRESETS: AccessPreset[];
export function buildModuleOverrides(preset: AccessPreset): UserAccessModuleOverride[];
```

Definitions:

| Preset | Recalls | Materials | Videos | Mocks | scopeField |
|---|---|---|---|---|---|
| Recalls Only | true | false | false | false | `recallSetCodes` |
| Materials Only | false | true | false | false | `materialFolderIds` |
| Videos Only | false | false | true | false | `videoIds` |
| Full Access | true | true | true | true | `null` |

"Custom" is not a data entry — it's a UI-only escape hatch handled by the modal
(see below).

### `components/admin/user-access/quick-grant-modal.tsx`

Props: `{ userId: string; userLabel: string; open: boolean; onClose: () => void; onGranted: (access: UserAccess) => void }`.

State machine: `'pick' -> 'confirm' -> (saving)`.

1. **On open**: `fetchUserAccess(userId)` to get current subscriptions (for the
   eligibility hint) and the baseline `UserAccess` to merge into.
2. **Pick step**: 5 cards (4 presets + Custom). Custom immediately calls `onClose()`
   with a signal the caller uses to expand its own Advanced section — the modal
   itself never renders custom controls.
3. **Confirm step** (after a real preset is picked):
   - Summary line built from `preset.description`.
   - Eligibility hint: `subscriptions.some(s => !isSuspended(s) && !isExpired(s))`.
     Best-effort UI hint only — the backend remains the real gate, so a wrong
     guess here is a UX inconvenience, not a security issue. If false, render a
     `<Select>` of active plans (from `fetchAdminBillingPlans`) labeled
     "Activate with:", required to proceed. Reuses `isProfessionMismatch` +
     the existing warning/override-checkbox pattern from `PackageList`.
   - Collapsed **"Customize scope ▾"**: only rendered when `preset.scopeField`
     is non-null. Expanding it lazy-loads the matching option list
     (`fetchAdminRecallSetTags` / `adminListMaterialFolders` /
     `fetchAllocatableVideos`) and renders the matching picker component bound
     to local state, defaulting to `[]` (= everything, per the pickers'
     existing convention).
   - **Grant Access** button (disabled until any required plan is chosen):
     1. If a plan was chosen, `await grantUserPackage(userId, { planCode, makePrimary: subscriptions.length === 0 })`.
     2. `await putUserAccessScope(userId, { modules: buildModuleOverrides(preset), recallSetCodes: ..., materialFolderIds: ..., videoIds: ..., accessExpiresAt: baseline.accessExpiresAt })`
        — only the preset's own scope field is set from local state; the other
        two scope arrays are passed through unchanged from the fetched baseline
        so an unrelated existing allow-list isn't silently wiped.
   - On success: toast, `onGranted(savedAccess)`, close. On failure: inline
     `InlineAlert` (via `readErrorMessage`), modal stays open.

## Entry points

### User detail page (`app/admin/users/[id]/page.tsx`)

- Add a "⚡ Quick Grant" button next to the "Access & Allocation" heading, opens
  the modal with `userId = user.id`.
- Wrap the existing `ManageAccessPanel` block in a `<details>` disclosure titled
  "Advanced (full manual control)", collapsed by default. It expands
  automatically when Quick Grant reports a Custom selection, or if the URL has
  `#access` (so a direct link / the Users-list Custom path can jump straight
  there).
- `onGranted` refreshes `access`/`originalAccess` state exactly like
  `handleSaveAccess` already does, so the (collapsed) Advanced panel reflects
  the new state if opened afterward.

### Users list (`app/admin/users/page.tsx`)

- New "Actions" column in the desktop `columns` array: a single icon-only
  button (`⚡`, `aria-label="Quick grant access"`) that opens the modal for
  that row's `user.id` / `user.name`.
- Mobile card: same button placed next to the existing "View profile" link.
- Custom selection from this entry point has nowhere to expand to, so it
  navigates to `/admin/users/${id}#access` instead (reusing the anchor
  behavior above).

## Error handling

- Both API calls already throw normalized `ApiError` (via `apiClient`); reuse
  `readErrorMessage` exactly as `ManageAccessPanel`/`AddUserModal` already do.
- Partial failure (package granted, scope save fails): the modal shows the
  error but does not attempt a rollback of the package grant — matches how the
  existing Access & Allocation save already behaves for its own multi-call
  sequences (packages are separately persisted, scope is a separate PUT).

## Testing

- `lib/user-access-presets.test.ts`: each preset has exactly 4 module keys, the
  right booleans, and `buildModuleOverrides` output shape matches
  `UserAccessModuleOverride[]`. Pure logic, no rendering.
- `pnpm exec tsc --noEmit` on touched files as the lightweight gate (per repo's
  ship-it workflow — no new Playwright/E2E coverage for this change).
- Manual verification via the local dev preview: open Quick Grant from both
  entry points, run the Recalls Only preset end-to-end against a seeded test
  learner.

## Out of scope

- No changes to backend entitlement logic — this is purely an admin-UI
  convenience layer over APIs that already exist and are already correct.
- No bulk/multi-user Quick Grant (one candidate at a time, matching the
  original ask).
