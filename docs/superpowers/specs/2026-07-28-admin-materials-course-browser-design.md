# Admin Materials — breadcrumb drill-down browser per subtest

**Date:** 2026-07-28
**Status:** Approved (design) — ready for implementation plan

## Context

The admin "Course Materials" page (`/admin/content/materials`, default view) groups
folders into four cards per profession — Listening / Reading / Writing / Speaking.
Within each card, `CourseMaterialsMap` renders `section.folders` as one **flat**
list. The backend (`/v1/admin/materials/course-map`,
`MaterialsAdminEndpoints.cs:92-132`) returns **every folder in that subtest's
subtree at any depth** with no hierarchy info surfaced in the UI — so a folder
named "Listening" three levels deep under "Jahshan" renders as a plain sibling of
the top-level "Listening" folder. Owner screenshot: Medicine → Listening shows 46
flattened names including duplicate "Listening" and "Audio" entries with no way to
tell which nesting they belong to.

This is a **display bug, not a content problem** — the same underlying data
already renders correctly as a clean 3-folder list in the learner mobile app
(`Materials > Listening` → Benchmark Exams / Extra Listening Exams / Jahshan),
which uses a proper breadcrumb drill-down. `MaterialCourseMapFolder` already
carries `parentFolderId` and `MaterialCourseMapItem` already carries `folderId` —
the data needed to build a hierarchy is already in the payload; it's just not used.

Three navigation approaches were presented to the owner (reshape-in-place, modal,
dedicated page). **Owner chose: dedicated page**, matching the mobile app's
pattern exactly.

## Goal

Each subtest card in Course Materials becomes an entry point into a full-width
breadcrumb drill-down browser, scoped to that profession + subtest, showing one
folder level at a time — eliminating the flat/duplicate-name confusion.

## Non-goals / out of scope

- **No backend changes.** `parentFolderId` / `folderId` are already returned;
  the fix is 100% presentational.
- **No changes to production content/folders.** The tree structure is already
  correct (proven by the mobile app); only how the admin *sees* it changes.
- **General English is not converted.** Its `/course-map` folder projection
  (`MaterialsAdminEndpoints.cs:165`) omits `ParentFolderId`, so it can't build a
  hierarchy client-side without a small backend addition. Its folder count is
  small (a handful of folders per the 2026-07-18 verification) and it wasn't part
  of the owner's complaint — its card keeps today's flat list unchanged. Revisit
  if it grows.
- **Publish / Unpublish, Delete, Audience stay on the existing "Advanced / Folder
  Tree" view.** The new browser gets the same four actions Course Materials
  already exposes today (Edit folder, Edit file, New folder, Add file), just
  correctly scoped to wherever the admin is currently browsing. Adding the full
  action set to the new browser is an easy fast-follow, not bundled into this
  change.
- No new route/URL, no deep-linking, no search — matches the current Course
  Materials page's lack of URL state.

## Architecture

No changes to `AdminMaterialsPage`'s `viewMode` (`'course' | 'tree'` — unchanged).
The drill-down is entirely owned by `CourseMaterialsMap`, which already fetches
and holds the course-map data — it's a deeper display mode of the same
component, not a new sibling view the parent page needs to know about.

```
app/admin/content/materials/page.tsx        (small: parentFolderId param + modal hoist)
components/domain/materials/
  course-materials-map.tsx                  (add: local `browsing` state, click-through)
  materials-course-browser.tsx              (NEW: the breadcrumb drill-down UI)
lib/
  materials-course-tree.ts                  (NEW: pure grouping/breadcrumb helpers)
  materials-course-tree.test.ts             (NEW: vitest, mirrors lib/materials-tree.ts)
```

### `lib/materials-course-tree.ts` (new, pure functions)

- `buildChildrenByParent(folders)` — groups `MaterialCourseMapFolder[]` by
  `parentFolderId`. **Edge case:** if a folder's `parentFolderId` is set but does
  not match any id present in this same scoped list (possible if a
  mid-tree folder has its own explicit subtest override, moving its children into
  a different scoped set than their grandparent), treat it as a root — never drop
  a folder from view.
- `buildFilesByFolder(files)` — groups `MaterialCourseMapItem[]` by `folderId`.
- `countDescendants(folderId, childrenByParent, filesByFolder)` — recursive
  folder+file counts for a card's "N folders · N files" line.
- `buildBreadcrumbTrail(folderId, foldersById)` — walks parent chain to root,
  returns ordered `{id, name}[]`.

Same shape/spirit as the existing `lib/materials-tree.ts` (learner side) — not
reused directly because it operates on the learner's `LearnerMaterialFolderDto`
(already-nested) rather than the admin's flat `MaterialCourseMapFolder[]`.

### `components/domain/materials/materials-course-browser.tsx` (new)

Props: `{ section: MaterialCourseMapSection; title: string; onBack: () => void;
onCreateFolder: (parentFolderId: string | null) => void; onAddFile: (folderId:
string) => void; onEditFolder: (folderId: string) => void; onEditFile: (fileId:
string) => void; }`

- Local state: `currentFolderId: string | null` (`null` = subtest root).
- `useMemo` builds `childrenByParent` / `filesByFolder` / `foldersById` once from
  `section.folders` / `section.files`.
- Renders: back button + breadcrumb trail (section `title` as the first crumb,
  then folder names down to `currentFolderId`) → grid of immediate child folder
  cards (name, live descendant folder/file counts, Published/Draft dot, Edit
  button, click-to-drill-in) → rows for files whose `folderId === currentFolderId`
  (title, kind icon, status badge, Edit button).
- "New folder" / "Add file" buttons create **into `currentFolderId`** — fixes an
  existing bug where Course Materials' buttons silently always create at the
  subtest root regardless of context.
- Reuses the subtest color/icon skin already defined in the learner
  `materials-browser.tsx` (`SECTION_SKINS`) so the admin view reads as the same
  visual language as the mobile app.
- Empty states: no child folders and no files at the current level → reuse the
  existing `EmptyState` component ("No files in this folder yet.").

### `course-materials-map.tsx` (edit)

- New local state: `browsing: { professionId: string; professionLabel: string;
  subtestCode: string } | null`.
- Each subtest `<Card>` header becomes clickable → sets `browsing`.
- When `browsing` is set, render `<MaterialsCourseBrowser>` (passing the matching
  `section` from already-fetched `data`, and a `title` string — subtest name only
  when `section.sharing === 'shared'`, or `"{professionLabel} {subtestName}"` when
  profession-specific; same capitalization approach already used for the card
  heading, `<h2 className="capitalize">{section.subtestCode}</h2>`) in place of
  the profession-tabs + card grid. `onBack` clears `browsing` (profession tab
  selection is untouched, so the admin returns to exactly where they were).
- `onCreateFolder` prop gains an optional trailing `parentFolderId?: string |
  null` param, passed through to `openCourseCreateFolder`.

### `page.tsx` (edit)

- `openCourseCreateFolder(professionId, professionLabel, subtestCode,
  parentFolderId = null)` — passes `parentFolderId` to `setParentFolderIdForNew`
  instead of hardcoding `null`.
- **Hoist the folder/file/audience `<Modal>` blocks** out of the
  `viewMode === 'tree'`-only return branch so they render regardless of
  `viewMode`. The state and handlers that back them (`folderModalOpen`,
  `fileModalOpen`, save/delete/publish handlers) already live at the top of
  `AdminMaterialsPage` and don't need to move — only the JSX that mounts the
  `<Modal>` components was trapped in the tree-only branch.
- Remove the now-unnecessary `setViewMode('tree')` calls in
  `openCourseEditFolder` / `openCourseEditFile` / `openCourseAddFile` — they only
  existed to force-mount the modal; once hoisted, editing from Course Materials
  or the new Browse view opens the modal in place without navigating away. (This
  also incidentally fixes an existing wart where editing from Course Materials
  today unexpectedly teleports the admin to the unrelated global Advanced tree.)

## Data flow

No new network calls. `MaterialsCourseBrowser` receives the `section` object
`CourseMaterialsMap` already fetched via `adminGetMaterialCourseMap()` on mount —
drilling in/out and switching folders is pure client-side state, instant.

## Error handling / edge cases

- Folder with `parentFolderId` pointing outside the current scoped set → rendered
  as a root within this view (see `buildChildrenByParent` above) — never silently
  dropped.
- Subtest section with zero folders → `EmptyState` at the root level.
- `/course-map` fetch failure → already handled by `CourseMaterialsMap`'s
  existing `error` state; the browser is simply never entered.

## Testing

1. `pnpm exec vitest run lib/materials-course-tree.test.ts` — grouping,
   orphaned-parent fallback, breadcrumb trail, descendant counts.
2. `pnpm exec tsc --noEmit` — lightweight gate per the ship-it workflow.
3. Manual verification via agent-browser: Medicine → Listening (the screenshotted
   46-folder case) → confirm top level now shows only the real top-level folders
   (Benchmark Exams, Extra Listening Exams, Jahshan), drilling into Jahshan shows
   its own children (including its nested "Listening"/"Audio" folders, now
   disambiguated by breadcrumb), New folder/Add file create into the drilled-down
   location, Edit opens in place without leaving the page.
   - Per existing note: local Next dev on this machine has been unreliable for
     screenshotting (slow disk) — fall back to verifying against the deployed
     prod admin panel if local preview misbehaves.

## Deploy

Commit + push to `main` → `Build & Deploy (web + API)` → blue/green. Frontend-only
change, no migration, no backend redeploy risk.
