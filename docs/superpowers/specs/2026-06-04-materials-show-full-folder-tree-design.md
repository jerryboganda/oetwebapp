# Materials — show the full folder tree to students (stop pruning empty folders)

**Date:** 2026-06-04
**Status:** Approved (design) — ready for implementation plan

## Context

Admins organise downloadable materials into a nested folder tree
(e.g. `Medicine Profession ▸ Reading ▸ Reading 1`). When a plan-allocated folder
is opened by an entitled student, only the folders that *contain a published
file* render. Empty subfolders are hidden, so the student never sees the
classification structure the admin built — they see just the top folder and any
files directly inside it.

Concretely: `mindreader420123@gmail.com` (premium-yearly) sees `Medicine
Profession` + `file 1f`, but **not** the `Reading / Listening / Writing /
Speaking` subfolders or their nested children, because those subfolders are
empty and get pruned.

The owner wants students to see the **complete folder hierarchy** — every folder
and subfolder at any depth — not only the branches that happen to hold files.

## Goal

A student opening Materials sees the full **published** folder tree they are
**entitled to access**, at any depth, including empty folders. Files remain
where they are, gated as today.

## Non-goals / Out of scope

- No change to access control: folders restricted to a plan the student does not
  have stay hidden; Draft (unpublished) folders stay hidden.
- No change to download authorization (`CanCandidateAccessMaterialFileAsync`).
- No change to the admin side.
- No per-folder/global "show empty" toggle (YAGNI — full tree is always wanted).

## Current behaviour (three pruning layers, all keyed on "has a file")

1. **`MaterialAccessService.GetVisibleTreeAsync`**
   (`backend/src/OetLearner.Api/Services/Content/MaterialAccessService.cs`)
   computes `visibleFolderIds` (folders that pass `IsFolderVisible`: published +
   all ancestors published + audience matches), then further filters root
   folders to `foldersWithContent.Contains(f.Id)`.
2. **`BuildFolderNode`** (same file) recurses only into child folders where
   `foldersWithContent.Contains(f.Id)`.
3. **`FolderNode`** in `app/materials/page.tsx` does `if (!hasContent) return null`
   (hides any folder with no files and no subfolders).

`BuildFoldersWithContent` seeds from folders with a direct published file and
propagates upward, so empty branches never enter the set → pruned server-side
(the UI never even receives them).

## Design (Approach A — switch the pruning key from "has content" to "is accessible")

### Backend — `MaterialAccessService.cs`
- In `GetVisibleTreeAsync`, change the root-folder filter from
  `foldersWithContent.Contains(f.Id)` to `visibleFolderIds.Contains(f.Id)`.
- In `BuildFolderNode`, change the child-folder filter from
  `foldersWithContent.Contains(f.Id)` to use the visible set. Concretely: pass
  `visibleFolderIds` as the set argument at the call site and rename the
  `BuildFolderNode` parameter from `foldersWithContent` to `visibleFolderIds`
  (single internal caller, so this is a safe rename).
- Remove the now-unused `BuildFoldersWithContent` helper.
- `IsFolderVisible` / `ResolveEffectiveAudience` / membership resolution are
  unchanged — they already enforce published + ancestors-published + audience.

Net effect: the student receives every folder that is **published and they are
entitled to**, regardless of whether it holds files.

### Frontend — `app/materials/page.tsx` `FolderNode`
- Remove `if (!hasContent) return null;` so empty folders render.
- When an expanded folder has neither files nor subfolders, render a subtle
  empty state line, e.g. *"No files in this folder yet."*, so an empty folder
  reads as intentional rather than broken.
- Keep current ordering (folders then files, each by `sortOrder`) and the
  existing expand/collapse behaviour.

### Unchanged
Access control, publish gating, download authorization, admin UI, the learner
download flow, analytics.

## Edge cases

- **Published folder with `audienceMode = Inherit` and no concrete ancestor
  audience** → still hidden (`IsFolderVisible` returns false). Admin must set an
  audience (already surfaced by the admin "No audience" diagnostic chip). No
  change.
- **Folder restricted to a different plan** → hidden for this student
  (`IsFolderVisible` audience check). No change.
- **Completely empty but published+entitled top-level folder** → now shows as an
  empty folder (previously pruned). This is the intended new behaviour.
- **Root-level files (`FolderId = null`)** → still not rendered in the tree (the
  tree returns folders; root files are an admin anti-pattern already flagged by
  the "Not in a folder" admin warning). Unchanged by this work.

## Verification

1. **Backend build:** `dotnet build -c Debug` (0 errors).
2. **Frontend typecheck:** `pnpm exec tsc --noEmit`.
3. **DB simulation (prod data):** with `file 1f` in `Medicine Profession` and all
   subfolders published + Inherit, confirm `visibleFolderIds` includes all 9
   folders for the premium-yearly user (recursive audience/publish check).
4. **End-to-end:** log in as `mindreader420123@gmail.com` → Materials → confirm
   the full tree renders: `Medicine Profession ▸ {Reading ▸ Reading 1, Listening
   ▸ Listening 1, Writing, Speaking}`, with `file 1f` under Medicine Profession
   and empty folders shown with the empty-state line.
5. **Access-control regression:** a folder restricted to a different plan, or a
   Draft folder, must NOT appear.

## Deploy

Commit + push to `main` → CI (`.github/workflows/deploy.yml`) builds web + API
off-box, pushes to GHCR, blue/green-deploys with health gate. Verify active slot
+ image SHA + `/health/ready`.
