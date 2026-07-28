# Admin Materials Course Browser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the flat, depth-blind folder dump inside each Course Materials subtest card with a breadcrumb drill-down browser (one folder level at a time, same pattern as the mobile learner app), scoped to that profession + subtest.

**Architecture:** 100% frontend. The admin `/course-map` endpoint already returns every folder's `parentFolderId` and every file's `folderId` — a new pure-function module groups that flat list into a parent→children tree client-side, a new presentational component renders it as a breadcrumb browser, and `CourseMaterialsMap` gets a small local "browsing" state that swaps a subtest card's flat list for that browser. Two small existing bugs get fixed as part of this: "New folder"/"Add file" from Course Materials always silently created at the subtest root regardless of context, and editing from Course Materials force-navigated away to the unrelated "Advanced / Folder Tree" view (because the edit/create modals were only mounted in that view's render branch).

**Tech Stack:** Next.js 16 App Router, React 19, TypeScript, Tailwind v4, vitest.

**Spec:** `docs/superpowers/specs/2026-07-28-admin-materials-course-browser-design.md`

## Global Constraints

- No backend changes — `MaterialCourseMapFolder.parentFolderId` and `MaterialCourseMapItem.folderId` are already returned by `/v1/admin/materials/course-map`.
- No changes to production content/folders — this only changes how existing data is displayed.
- General English is explicitly out of scope (its `/course-map` folder projection omits `ParentFolderId` server-side) — its card in `CourseMaterialsMap` stays untouched.
- Publish/Unpublish, Delete, and Audience actions stay on the existing "Advanced / Folder Tree" view — not added to the new browser.
- Files stay under 500 lines (repo convention).
- Verification is lightweight per the ship-it workflow: `pnpm exec tsc --noEmit` + the new vitest file, not a full test-suite marathon.
- Stage explicit file paths when committing — never `git add -A`. No `Co-Authored-By` trailer.

---

### Task 1: Export the subtest colour/icon skin from the learner materials browser

**Files:**
- Modify: `components/domain/materials/materials-browser.tsx:54-98`

**Interfaces:**
- Produces: `export type Subtest = 'listening' | 'reading' | 'writing' | 'speaking'`, `export interface SectionSkin { Icon: LucideIcon; tile: string; bar: string; ring: string; glow: string }`, `export const SECTION_SKINS: Record<Subtest, SectionSkin>`, `export const DEFAULT_SKIN: SectionSkin` — all consumed by Task 4's `materials-course-browser.tsx`.

- [ ] **Step 1: Export `Subtest`, `SectionSkin`, `SECTION_SKINS`, `DEFAULT_SKIN`**

In `components/domain/materials/materials-browser.tsx`, find this block (currently unexported):

```ts
const SUBTESTS = ['listening', 'reading', 'writing', 'speaking'] as const;
type Subtest = (typeof SUBTESTS)[number];

/**
 * Each OET subtest owns a signature colour + icon so the library reads at a
 * glance — a learner spots "Listening" by its blue headphones before reading a
 * single label. Folders whose name doesn't map to a subtest fall back to the
 * app's violet so nested folders stay cohesive.
 */
interface SectionSkin {
  Icon: LucideIcon;
  tile: string;   // gradient + foreground for the icon tile
  bar: string;    // left accent bar
  ring: string;   // hover border colour
  glow: string;   // hover background wash
}

const SECTION_SKINS: Record<Subtest, SectionSkin> = {
```

Replace with:

```ts
const SUBTESTS = ['listening', 'reading', 'writing', 'speaking'] as const;
export type Subtest = (typeof SUBTESTS)[number];

/**
 * Each OET subtest owns a signature colour + icon so the library reads at a
 * glance — a learner spots "Listening" by its blue headphones before reading a
 * single label. Folders whose name doesn't map to a subtest fall back to the
 * app's violet so nested folders stay cohesive.
 *
 * Exported so the admin Course Materials drill-down
 * (materials-course-browser.tsx) can reuse the same visual language.
 */
export interface SectionSkin {
  Icon: LucideIcon;
  tile: string;   // gradient + foreground for the icon tile
  bar: string;    // left accent bar
  ring: string;   // hover border colour
  glow: string;   // hover background wash
}

export const SECTION_SKINS: Record<Subtest, SectionSkin> = {
```

Then find:

```ts
const DEFAULT_SKIN: SectionSkin = {
  Icon: Folder,
  tile: 'from-primary/20 to-primary/5 text-primary',
  bar: 'bg-primary', ring: 'hover:border-primary/40', glow: 'hover:bg-primary/[0.03]',
};
```

Replace with:

```ts
export const DEFAULT_SKIN: SectionSkin = {
  Icon: Folder,
  tile: 'from-primary/20 to-primary/5 text-primary',
  bar: 'bg-primary', ring: 'hover:border-primary/40', glow: 'hover:bg-primary/[0.03]',
};
```

Everything else in the file (`SUBTESTS`, `PILL_ACTIVE`, `matchSubtest`, `skinFor`) stays private — only these four symbols need to leave the module.

- [ ] **Step 2: Typecheck**

Run: `pnpm exec tsc --noEmit`
Expected: no new errors (this is an additive export change only).

- [ ] **Step 3: Commit**

```bash
git add "components/domain/materials/materials-browser.tsx"
git commit -m "refactor(materials): export subtest skin config for reuse in admin browser"
```

---

### Task 2: Pure tree-building helpers (`lib/materials-course-tree.ts`)

**Files:**
- Create: `lib/materials-course-tree.ts`
- Test: `lib/materials-course-tree.test.ts`

**Interfaces:**
- Consumes: `MaterialCourseMapFolder { canonicalFolderId: string; name: string; status: MaterialStatus; parentFolderId?: string | null }`, `MaterialCourseMapItem { canonicalFileId: string; folderId: string; title: string; kind: string; status: MaterialStatus }` from `lib/materials-api.ts` (already exist, unchanged).
- Produces: `buildChildrenByParent(folders): Map<string | null, MaterialCourseMapFolder[]>`, `buildFilesByFolder(files): Map<string, MaterialCourseMapItem[]>`, `buildFoldersById(folders): Map<string, MaterialCourseMapFolder>`, `countDescendants(folderId, childrenByParent, filesByFolder): { folderCount: number; fileCount: number }`, `buildBreadcrumbTrail(folderId, foldersById): MaterialCourseMapFolder[]` — all consumed by Task 4's `materials-course-browser.tsx`.

- [ ] **Step 1: Write the failing test**

Create `lib/materials-course-tree.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import {
  buildChildrenByParent,
  buildFilesByFolder,
  buildFoldersById,
  buildBreadcrumbTrail,
  countDescendants,
} from './materials-course-tree';
import type { MaterialCourseMapFolder, MaterialCourseMapItem } from './materials-api';

function folder(
  partial: Partial<MaterialCourseMapFolder> & { canonicalFolderId: string; name: string },
): MaterialCourseMapFolder {
  return { status: 'Published', parentFolderId: null, ...partial } as MaterialCourseMapFolder;
}

function file(
  partial: Partial<MaterialCourseMapItem> & { canonicalFileId: string; folderId: string; title: string },
): MaterialCourseMapItem {
  return { kind: 'pdf', status: 'Published', ...partial } as MaterialCourseMapItem;
}

describe('buildChildrenByParent', () => {
  it('groups top-level folders under the null key', () => {
    const folders = [
      folder({ canonicalFolderId: 'a', name: 'Benchmark Exams' }),
      folder({ canonicalFolderId: 'b', name: 'Jahshan' }),
    ];
    const map = buildChildrenByParent(folders);
    expect(map.get(null)?.map((f) => f.canonicalFolderId)).toEqual(['a', 'b']);
  });

  it('groups nested folders under their real parent', () => {
    const folders = [
      folder({ canonicalFolderId: 'a', name: 'Jahshan' }),
      folder({ canonicalFolderId: 'b', name: 'Listening', parentFolderId: 'a' }),
      folder({ canonicalFolderId: 'c', name: 'Audio', parentFolderId: 'b' }),
    ];
    const map = buildChildrenByParent(folders);
    expect(map.get(null)?.map((f) => f.canonicalFolderId)).toEqual(['a']);
    expect(map.get('a')?.map((f) => f.canonicalFolderId)).toEqual(['b']);
    expect(map.get('b')?.map((f) => f.canonicalFolderId)).toEqual(['c']);
  });

  it('treats a folder whose parentFolderId is outside this scoped set as a root', () => {
    // Regression guard: a folder whose real parent belongs to a different
    // subtest's scoped list must not vanish from view.
    const folders = [
      folder({ canonicalFolderId: 'a', name: 'Orphaned', parentFolderId: 'not-in-this-section' }),
    ];
    const map = buildChildrenByParent(folders);
    expect(map.get(null)?.map((f) => f.canonicalFolderId)).toEqual(['a']);
    expect(map.has('not-in-this-section')).toBe(false);
  });
});

describe('buildFilesByFolder', () => {
  it('groups files by folderId', () => {
    const files = [
      file({ canonicalFileId: 'f1', folderId: 'a', title: 'Part 1' }),
      file({ canonicalFileId: 'f2', folderId: 'a', title: 'Part 2' }),
      file({ canonicalFileId: 'f3', folderId: 'b', title: 'Part 3' }),
    ];
    const map = buildFilesByFolder(files);
    expect(map.get('a')?.map((f) => f.canonicalFileId)).toEqual(['f1', 'f2']);
    expect(map.get('b')?.map((f) => f.canonicalFileId)).toEqual(['f3']);
  });
});

describe('buildFoldersById', () => {
  it('indexes folders by canonicalFolderId', () => {
    const folders = [folder({ canonicalFolderId: 'a', name: 'Jahshan' })];
    const map = buildFoldersById(folders);
    expect(map.get('a')?.name).toBe('Jahshan');
  });
});

describe('countDescendants', () => {
  it('counts nested folders and files at any depth', () => {
    const folders = [
      folder({ canonicalFolderId: 'a', name: 'Jahshan' }),
      folder({ canonicalFolderId: 'b', name: 'Listening', parentFolderId: 'a' }),
      folder({ canonicalFolderId: 'c', name: 'Audio', parentFolderId: 'b' }),
    ];
    const files = [
      file({ canonicalFileId: 'f1', folderId: 'b', title: 'Part 1' }),
      file({ canonicalFileId: 'f2', folderId: 'c', title: 'Part 2' }),
    ];
    const childrenByParent = buildChildrenByParent(folders);
    const filesByFolder = buildFilesByFolder(files);
    expect(countDescendants('a', childrenByParent, filesByFolder)).toEqual({ folderCount: 2, fileCount: 2 });
    expect(countDescendants('b', childrenByParent, filesByFolder)).toEqual({ folderCount: 1, fileCount: 2 });
    expect(countDescendants('c', childrenByParent, filesByFolder)).toEqual({ folderCount: 0, fileCount: 1 });
  });

  it('returns zero counts for a leaf folder with nothing inside', () => {
    const folders = [folder({ canonicalFolderId: 'a', name: 'Empty' })];
    const childrenByParent = buildChildrenByParent(folders);
    const filesByFolder = buildFilesByFolder([]);
    expect(countDescendants('a', childrenByParent, filesByFolder)).toEqual({ folderCount: 0, fileCount: 0 });
  });
});

describe('buildBreadcrumbTrail', () => {
  it('walks from a nested folder back to the root, root-first', () => {
    const folders = [
      folder({ canonicalFolderId: 'a', name: 'Jahshan' }),
      folder({ canonicalFolderId: 'b', name: 'Listening', parentFolderId: 'a' }),
      folder({ canonicalFolderId: 'c', name: 'Audio', parentFolderId: 'b' }),
    ];
    const foldersById = buildFoldersById(folders);
    const trail = buildBreadcrumbTrail('c', foldersById);
    expect(trail.map((f) => f.name)).toEqual(['Jahshan', 'Listening', 'Audio']);
  });

  it('returns an empty trail at the section root', () => {
    const foldersById = buildFoldersById([]);
    expect(buildBreadcrumbTrail(null, foldersById)).toEqual([]);
  });

  it('stops at a folder whose parent is outside this scoped set instead of throwing', () => {
    const folders = [folder({ canonicalFolderId: 'a', name: 'Orphaned', parentFolderId: 'not-in-this-section' })];
    const foldersById = buildFoldersById(folders);
    expect(buildBreadcrumbTrail('a', foldersById).map((f) => f.name)).toEqual(['Orphaned']);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `pnpm exec vitest run lib/materials-course-tree.test.ts`
Expected: FAIL — `Cannot find module './materials-course-tree'`

- [ ] **Step 3: Write the implementation**

Create `lib/materials-course-tree.ts`:

```ts
import type { MaterialCourseMapFolder, MaterialCourseMapItem } from './materials-api';

export interface FolderCounts {
  folderCount: number;
  fileCount: number;
}

/**
 * Groups a subtest section's flat folder list by parent. A folder whose
 * parentFolderId points outside this same scoped list (possible when a
 * mid-tree folder carries its own explicit subtest override, moving its
 * children into a different section than their grandparent) is treated as a
 * root here — never dropped from view.
 */
export function buildChildrenByParent(
  folders: MaterialCourseMapFolder[],
): Map<string | null, MaterialCourseMapFolder[]> {
  const ids = new Set(folders.map((f) => f.canonicalFolderId));
  const map = new Map<string | null, MaterialCourseMapFolder[]>();
  for (const folder of folders) {
    const parentId = folder.parentFolderId && ids.has(folder.parentFolderId) ? folder.parentFolderId : null;
    const bucket = map.get(parentId);
    if (bucket) bucket.push(folder);
    else map.set(parentId, [folder]);
  }
  return map;
}

export function buildFilesByFolder(files: MaterialCourseMapItem[]): Map<string, MaterialCourseMapItem[]> {
  const map = new Map<string, MaterialCourseMapItem[]>();
  for (const file of files) {
    const bucket = map.get(file.folderId);
    if (bucket) bucket.push(file);
    else map.set(file.folderId, [file]);
  }
  return map;
}

export function buildFoldersById(folders: MaterialCourseMapFolder[]): Map<string, MaterialCourseMapFolder> {
  return new Map(folders.map((f) => [f.canonicalFolderId, f]));
}

/** Recursive folder + file counts for everything under `folderId`, at any depth. */
export function countDescendants(
  folderId: string,
  childrenByParent: Map<string | null, MaterialCourseMapFolder[]>,
  filesByFolder: Map<string, MaterialCourseMapItem[]>,
): FolderCounts {
  let folderCount = 0;
  let fileCount = filesByFolder.get(folderId)?.length ?? 0;
  const children = childrenByParent.get(folderId) ?? [];
  for (const child of children) {
    folderCount += 1;
    const nested = countDescendants(child.canonicalFolderId, childrenByParent, filesByFolder);
    folderCount += nested.folderCount;
    fileCount += nested.fileCount;
  }
  return { folderCount, fileCount };
}

/**
 * Ancestor chain for `folderId`, root-first (excludes the section root
 * itself — callers prepend their own section title as the first crumb). The
 * walk stops (rather than throwing) if an ancestor isn't in `foldersById` —
 * the same "outside this scoped set" case `buildChildrenByParent` handles.
 */
export function buildBreadcrumbTrail(
  folderId: string | null,
  foldersById: Map<string, MaterialCourseMapFolder>,
): MaterialCourseMapFolder[] {
  const trail: MaterialCourseMapFolder[] = [];
  let currentId = folderId;
  const seen = new Set<string>();
  while (currentId) {
    const current = foldersById.get(currentId);
    if (!current || seen.has(currentId)) break;
    seen.add(currentId);
    trail.unshift(current);
    currentId = current.parentFolderId ?? null;
  }
  return trail;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `pnpm exec vitest run lib/materials-course-tree.test.ts`
Expected: PASS — 10 tests.

- [ ] **Step 5: Commit**

```bash
git add "lib/materials-course-tree.ts" "lib/materials-course-tree.test.ts"
git commit -m "feat(materials): add pure tree/breadcrumb helpers for admin course browser"
```

---

### Task 3: Fix parentFolderId threading and hoist modals (`app/admin/content/materials/page.tsx`)

**Files:**
- Modify: `app/admin/content/materials/page.tsx:640-1001`

**Interfaces:**
- Produces: `openCourseCreateFolder(professionId: string | null, professionLabel: string, subtestCode: string | null, parentFolderId?: string | null): void` — new 4th param, consumed by Task 5's updated `CourseMaterialsMapProps.onCreateFolder`.
- Unchanged signatures still consumed by Task 5: `openCourseAddFile(folderId: string, subtestCode: string): void`, `openCourseEditFolder(folderId: string): void`, `openCourseEditFile(fileId: string): void`.

This task fixes two bugs together (they must land in the same commit — fixing one without the other leaves editing from Course Materials broken):
1. `openCourseCreateFolder` always creates at the subtest root (`setParentFolderIdForNew(null)` is hardcoded) — the new browser needs it to create into whatever folder is currently open.
2. The folder/file/audience `<Modal>`s are only mounted in the `viewMode === 'tree'` return branch, so every `openCourse*` handler force-calls `setViewMode('tree')` just to make the modal appear — which yanks the admin out of Course Materials (and will yank them out of the new Browse view too) onto the unrelated global Advanced tree. Hoisting the modals so they render regardless of `viewMode` removes the need for that.

- [ ] **Step 1: Replace the four `openCourse*` handlers and the component's return statements**

In `app/admin/content/materials/page.tsx`, replace everything from the start of `openCourseCreateFolder` (currently line 640) through the end of the `AdminMaterialsPage` function body (the closing `}` currently at line 1001) with:

```tsx
  function openCourseCreateFolder(
    professionId: string | null,
    professionLabel: string,
    subtestCode: string | null,
    parentFolderId: string | null = null,
  ) {
    const isGeneralEnglish = professionId === null;
    const isShared = subtestCode === 'listening' || subtestCode === 'reading';
    const sectionLabel = subtestCode ? `${subtestCode[0].toUpperCase()}${subtestCode.slice(1)}` : '';
    // Only auto-name/auto-classify when creating a section's top-level
    // canonical folder. A subfolder created from inside the drill-down
    // browser (parentFolderId set) gets a blank name and inherits its
    // parent's scope — auto-filling "Listening" here would recreate the
    // exact duplicate-name confusion this feature exists to fix.
    const isTopLevel = parentFolderId === null;
    setEditingFolder(null);
    setParentFolderIdForNew(parentFolderId);
    setFolderForm({
      ...defaultFolderForm,
      name: isTopLevel
        ? (isGeneralEnglish ? 'General English' : isShared ? sectionLabel : `${professionLabel} ${sectionLabel}`)
        : '',
      subtestCode: subtestCode ?? '',
      scopeKind: isTopLevel ? (isGeneralEnglish ? 'general_english' : isShared ? 'shared' : 'profession') : '',
      professionId: isTopLevel && !isGeneralEnglish && !isShared ? professionId ?? '' : '',
    });
    setFolderModalOpen(true);
  }

  function openCourseEditFolder(folderId: string) {
    const folder = findFolder(tree, folderId);
    if (!folder) {
      setToast({ variant: 'error', message: 'The canonical folder could not be loaded. Refresh and try again.' });
      setViewMode('tree');
      return;
    }
    setSelectedFolder(folder);
    openEditFolder(folder);
  }

  function openCourseAddFile(folderId: string, subtestCode: string) {
    const folder = findFolder(tree, folderId);
    if (!folder) {
      setToast({ variant: 'error', message: 'Create or refresh the destination folder before uploading a file.' });
      setViewMode('tree');
      return;
    }
    setSelectedFolder(folder);
    setEditingFile(null);
    setFileForm({ ...defaultFileForm, folderId, subtestCode });
    setUploadFile(null);
    setUploadProgress(0);
    setFileModalOpen(true);
  }

  function openCourseEditFile(fileId: string) {
    const file = findFile(tree, fileId);
    if (!file) {
      setToast({ variant: 'error', message: 'The canonical file could not be loaded. Refresh and try again.' });
      setViewMode('tree');
      return;
    }
    setSelectedFolder(file.folderId ? findFolder(tree, file.folderId) : null);
    openEditFile(file);
  }

  // Folder/file/audience modals and the toast render regardless of
  // viewMode, so editing from Course Materials (or the new Browse view)
  // opens the modal in place instead of navigating away.
  const modals = (
    <>
      {/* Folder create/edit modal */}
      <Modal open={folderModalOpen} onClose={() => setFolderModalOpen(false)} title={editingFolder ? 'Edit Folder' : 'New Folder'}>
        <div className="space-y-4 p-4">
          <Input
            label="Name"
            value={folderForm.name}
            onChange={(e) => setFolderForm((f) => ({ ...f, name: e.target.value }))}
            placeholder="e.g. Mock Test 1"
            maxLength={200}
          />
          <Input
            label="Description (optional)"
            value={folderForm.description}
            onChange={(e) => setFolderForm((f) => ({ ...f, description: e.target.value }))}
            placeholder="Short description"
            maxLength={1024}
          />
          <Select
            label="Subtest hint (optional)"
            value={folderForm.subtestCode}
            onChange={(e) => setFolderForm((f) => ({ ...f, subtestCode: e.target.value }))}
            options={SUBTEST_OPTIONS}
          />
          <Select
            label="Course scope"
            value={folderForm.scopeKind}
            onChange={(e) => setFolderForm((f) => ({ ...f, scopeKind: e.target.value as '' | MaterialScopeKind, professionId: e.target.value === 'profession' ? f.professionId : '' }))}
            options={[
              { value: '', label: 'Inherit / legacy' },
              { value: 'shared', label: 'Shared across professions' },
              { value: 'profession', label: 'Profession-specific' },
              { value: 'general_english', label: 'General English' },
            ]}
          />
          {folderForm.scopeKind === 'profession' ? <Select
            label="Profession"
            value={folderForm.professionId}
            onChange={(e) => setFolderForm((f) => ({ ...f, professionId: e.target.value }))}
            options={[
              { value: '', label: 'Select a profession' },
              { value: 'medicine', label: 'Medicine' },
              { value: 'nursing', label: 'Nursing' },
              { value: 'pharmacy', label: 'Pharmacy' },
              { value: 'physiotherapy', label: 'Physiotherapy' },
              { value: 'dentistry', label: 'Dentistry' },
              { value: 'radiography', label: 'Radiography' },
            ]}
          /> : null}
          <div className="flex justify-end gap-2 pt-2">
            <Button variant="outline" onClick={() => setFolderModalOpen(false)}>Cancel</Button>
            <Button onClick={() => void saveFolder()} disabled={savingFolder}>
              {savingFolder ? 'Saving…' : editingFolder ? 'Save changes' : 'Create folder'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* Audience modal */}
      <Modal open={audienceModalOpen} onClose={() => setAudienceModalOpen(false)} title={`Audience — ${audienceTargetFolder?.name ?? ''}`}>
        <div className="p-4 space-y-4">
          <p className="text-xs text-admin-fg-muted">
            Control which learners can see this folder and its contents.
            Child folders set to &quot;Inherit&quot; will use this setting.
          </p>
          <AudiencePicker
            audienceMode={audienceForm.audienceMode}
            audiences={audienceForm.audiences}
            onChange={(mode, audiences) => setAudienceForm({ audienceMode: mode, audiences })}
          />
          <div className="flex justify-end gap-2 pt-2">
            <Button variant="outline" onClick={() => setAudienceModalOpen(false)}>Cancel</Button>
            <Button onClick={() => void saveAudience()} disabled={savingAudience}>
              {savingAudience ? 'Saving…' : 'Save audience'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* File create/edit modal */}
      <Modal open={fileModalOpen} onClose={() => setFileModalOpen(false)} title={editingFile ? 'Edit File' : 'Add File'}>
        <div className="p-4 space-y-4">
          <Input
            label="Title"
            value={fileForm.title}
            onChange={(e) => setFileForm((f) => ({ ...f, title: e.target.value }))}
            placeholder="e.g. Listening Practice — Case 3"
            maxLength={200}
          />
          <Input
            label="Description (optional)"
            value={fileForm.description}
            onChange={(e) => setFileForm((f) => ({ ...f, description: e.target.value }))}
            maxLength={1024}
          />
          <div>
            <Select
              label="Destination folder"
              value={fileForm.folderId}
              onChange={(e) => changeFileFolder(e.target.value)}
              options={folderOptions}
            />
            <p className="mt-1 text-xs text-admin-fg-muted">
              Saving to: <span className="font-medium text-admin-fg">{buildFolderPath(flatFolders, fileForm.folderId || null)}</span>
            </p>
          </div>
          <Select
            label="Subtest (auto-set from folder — change if needed)"
            value={fileForm.subtestCode}
            onChange={(e) => setFileForm((f) => ({ ...f, subtestCode: e.target.value }))}
            options={SUBTEST_OPTIONS.filter((o) => o.value !== '')}
          />
          <div>
            <label className="block text-xs font-semibold text-admin-fg-muted mb-1.5">
              {editingFile ? 'Replace file (leave blank to keep current)' : 'File'}
            </label>
            <input
              ref={fileInputRef}
              type="file"
              accept=".pdf,.doc,.docx,.txt,.csv,.rtf,.xls,.xlsx,.ppt,.pptx,.jpg,.jpeg,.png,.gif,.webp,.mp3,.m4a,.wav,.ogg,.mp4,.webm,.mov"
              onChange={(e) => setUploadFile(e.target.files?.[0] ?? null)}
              className="block w-full text-sm text-admin-fg file:mr-3 file:rounded-md file:border-0 file:bg-admin-hover file:px-3 file:py-1.5 file:text-xs file:font-semibold"
            />
            {uploadFile && (
              <p className="mt-1 text-xs text-admin-fg-muted">{uploadFile.name} ({formatBytes(uploadFile.size)})</p>
            )}
            {savingFile && uploadProgress > 0 && uploadProgress < 1 && (
              <div className="mt-2 h-1.5 rounded-full bg-border overflow-hidden">
                <div className="h-full bg-primary transition-all" style={{ width: `${Math.round(uploadProgress * 100)}%` }} />
              </div>
            )}
          </div>
          <div className="flex justify-end gap-2 pt-2">
            <Button variant="outline" onClick={() => setFileModalOpen(false)}>Cancel</Button>
            <Button onClick={() => void saveFile()} disabled={savingFile}>
              {savingFile ? 'Saving…' : editingFile ? 'Save changes' : 'Add file'}
            </Button>
          </div>
        </div>
      </Modal>

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}
    </>
  );

  if (viewMode === 'course') {
    return (
      <>
        <CourseMaterialsMap
          onAdvanced={() => setViewMode('tree')}
          onCreateFolder={openCourseCreateFolder}
          onAddFile={openCourseAddFile}
          onEditFolder={openCourseEditFolder}
          onEditFile={openCourseEditFile}
        />
        {modals}
      </>
    );
  }

  return (
    <AdminCatalogLayout
      title="Advanced Material Folder Tree"
      description="Upload and organise downloadable study materials for candidates. Assign folders to specific plans or cohorts to control access."
      breadcrumbs={BREADCRUMBS}
      eyebrow="CMS"
      hideViewModeToggle
      actions={<div className="flex gap-2"><Button variant="outline" onClick={() => setViewMode('course')}>Course Materials</Button><Button onClick={() => openCreateFolder(null)}><FolderPlus className="h-4 w-4 mr-1" /> New Root Folder</Button></div>}
    >
      <div className="col-span-full grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Folder tree */}
        <div className="lg:col-span-1 space-y-2">
          <div className="flex items-center justify-between mb-2">
            <h2 className="text-sm font-semibold text-admin-fg-strong">Folders</h2>
            <button
              type="button"
              onClick={() => void loadTree()}
              className="text-xs text-admin-fg-muted hover:text-admin-fg-strong"
              aria-label="Refresh"
            >
              <RotateCcw className="w-3.5 h-3.5" />
            </button>
          </div>

          {/* Publish requirements hint */}
          <div className="rounded-md bg-amber-50 border border-amber-200 px-3 py-2 text-xs text-amber-800 mb-2 space-y-0.5">
            <p className="font-semibold">For a folder to show to candidates:</p>
            <p>🟢 Status = <strong>Published</strong> &nbsp;·&nbsp; 🔒 Audience assigned &nbsp;·&nbsp; 📄 ≥1 published file inside</p>
          </div>

          {loading ? (
            <div className="space-y-2">
              {[1, 2, 3].map((i) => <Skeleton key={i} className="h-10 rounded-lg" />)}
            </div>
          ) : tree.length === 0 ? (
            <div className="rounded-lg border border-dashed border-border p-4 text-center text-xs text-admin-fg-muted">
              No folders yet. Create one to get started.
            </div>
          ) : (
            <div className="space-y-1">
              {/* Root-level "all files" entry */}
              <button
                type="button"
                onClick={() => setSelectedFolder(null)}
                className={[
                  'w-full flex items-center gap-2 rounded-lg px-3 py-2 text-sm text-left transition-colors',
                  selectedFolder === null
                    ? 'bg-primary/10 text-primary-dark font-semibold'
                    : 'text-admin-fg hover:bg-admin-hover',
                ].join(' ')}
              >
                <FolderOpen className="w-4 h-4 shrink-0" /> All files
              </button>
              {renderFolderTree(tree)}
            </div>
          )}
        </div>

        {/* File list */}
        <div className="lg:col-span-2 space-y-3">
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-semibold text-admin-fg-strong truncate">
              {selectedFolder ? buildFolderPath(flatFolders, selectedFolder.id) : 'All files'}
            </h2>
            <Button size="sm" onClick={openCreateFile}>
              <Plus className="h-3.5 w-3.5 mr-1" /> Add file
            </Button>
          </div>

          {filesLoading ? (
            <div className="space-y-2">
              {[1, 2, 3].map((i) => <Skeleton key={i} className="h-16 rounded-lg" />)}
            </div>
          ) : files.length === 0 ? (
            <EmptyState
              illustration={<FileText />}
              title="No files"
              description={selectedFolder ? `No files in "${selectedFolder.name}" yet.` : 'No files yet.'}
            />
          ) : (
            <div className="space-y-2">
              {files.map((file) => (
                <Card key={file.id}>
                  <CardContent className="p-3">
                    <div className="flex items-start gap-3">
                      <span className="mt-0.5 shrink-0 text-muted">
                        <MaterialKindIcon kind={file.kind} />
                      </span>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 flex-wrap">
                          <span className="text-sm font-semibold text-admin-fg-strong truncate">{file.title}</span>
                          <Badge variant={file.status === 'Published' ? 'success' : file.status === 'Archived' ? 'danger' : 'secondary'}>{file.status}</Badge>
                          <Badge variant="outline">{file.subtestCode}</Badge>
                          <Badge variant="outline">{file.kind}</Badge>
                          {!file.folderId && (
                            <Badge variant="warning" title="Files not inside a folder are never shown to candidates. Edit the file and pick a Destination folder.">
                              Not in a folder
                            </Badge>
                          )}
                        </div>
                        {file.description && (
                          <p className="text-xs text-admin-fg-muted mt-0.5 truncate">{file.description}</p>
                        )}
                        {file.media && (
                          <p className="text-xs text-admin-fg-muted mt-0.5">
                            {file.media.originalFilename} &middot; {formatBytes(file.media.sizeBytes)}
                          </p>
                        )}
                      </div>
                      <div className="flex items-center gap-1 shrink-0">
                        {file.status === 'Draft' && (
                          <Button
                            size="sm"
                            disabled={busyId === file.id}
                            onClick={() => void publishFile(file)}
                          >
                            Publish
                          </Button>
                        )}
                        {file.status === 'Published' && (
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={busyId === file.id}
                            onClick={() => void unpublishFile(file)}
                          >
                            Unpublish
                          </Button>
                        )}
                        <Button size="sm" variant="ghost" onClick={() => openEditFile(file)}>
                          <Pencil className="w-3.5 h-3.5" />
                        </Button>
                        <Button
                          size="sm"
                          variant="destructive"
                          disabled={busyId === file.id}
                          onClick={() => void deleteFile(file)}
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </Button>
                      </div>
                    </div>
                  </CardContent>
                </Card>
              ))}
            </div>
          )}
        </div>
      </div>

      {modals}
    </AdminCatalogLayout>
  );
}
```

This is a large, mostly-unchanged block — the only real edits are: `openCourseCreateFolder` gains the `parentFolderId` param and the top-level-only defaulting logic; the four handlers no longer call `setViewMode('tree')` on their happy path (the error-branch calls stay — they're a reasonable "let the admin see the full tree to investigate" recovery, unrelated to the modal-mounting problem); the three `<Modal>` blocks and the `{toast && ...}` block are extracted into a `modals` constant defined once, referenced from both the `'course'` and the `'tree'` branch, instead of only existing in the `'tree'` branch's JSX.

- [ ] **Step 2: Typecheck**

Run: `pnpm exec tsc --noEmit`
Expected: no errors.

- [ ] **Step 3: Manual check — Course Materials still works**

Run `pnpm run dev` (or use the existing local/prod preview setup), sign in as admin, go to `/admin/content/materials`, confirm:
- The page still loads on the "Course Materials" tab with profession cards and subtest cards.
- Clicking a subtest card's "New folder" opens the modal **without leaving the page** (previously this jumped to "Advanced / Folder Tree").
- Creating a folder there still works end-to-end (toast confirms, folder appears under "Advanced / Folder Tree").

- [ ] **Step 4: Commit**

```bash
git add "app/admin/content/materials/page.tsx"
git commit -m "fix(admin): thread parentFolderId through New Folder and stop modals force-navigating to Advanced tree"
```

---

### Task 4: Build the `MaterialsCourseBrowser` component

**Files:**
- Create: `components/domain/materials/materials-course-browser.tsx`

**Interfaces:**
- Consumes: `MaterialCourseMapSection` from `lib/materials-api.ts` (Task 2 target type); `buildChildrenByParent`, `buildFilesByFolder`, `buildFoldersById`, `buildBreadcrumbTrail`, `countDescendants` from `lib/materials-course-tree.ts` (Task 2); `SECTION_SKINS`, `DEFAULT_SKIN`, `Subtest` from `./materials-browser` (Task 1).
- Produces: `export function MaterialsCourseBrowser(props: MaterialsCourseBrowserProps)` with `MaterialsCourseBrowserProps = { section: MaterialCourseMapSection; title: string; onBack: () => void; onCreateFolder: (parentFolderId: string | null) => void; onAddFile: (folderId: string) => void; onEditFolder: (folderId: string) => void; onEditFile: (fileId: string) => void; }` — consumed by Task 5's `course-materials-map.tsx`.

- [ ] **Step 1: Create the component**

Create `components/domain/materials/materials-course-browser.tsx`:

```tsx
'use client';

import { useMemo, useState } from 'react';
import {
  ArrowLeft,
  ChevronRight,
  File as FileIcon,
  FileText,
  FolderOpen,
  FolderPlus,
  Image as ImageIcon,
  Music,
  Pencil,
  Plus,
  Video,
} from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import type { MaterialCourseMapSection } from '@/lib/materials-api';
import {
  buildBreadcrumbTrail,
  buildChildrenByParent,
  buildFilesByFolder,
  buildFoldersById,
  countDescendants,
} from '@/lib/materials-course-tree';
import { DEFAULT_SKIN, SECTION_SKINS, type Subtest } from './materials-browser';

interface MaterialsCourseBrowserProps {
  section: MaterialCourseMapSection;
  title: string;
  onBack: () => void;
  onCreateFolder: (parentFolderId: string | null) => void;
  onAddFile: (folderId: string) => void;
  onEditFolder: (folderId: string) => void;
  onEditFile: (fileId: string) => void;
}

function FileKindIcon({ kind }: { kind: string }) {
  switch (kind) {
    case 'audio':
      return <Music className="h-4 w-4 text-blue-500" />;
    case 'video':
      return <Video className="h-4 w-4 text-fuchsia-500" />;
    case 'image':
      return <ImageIcon className="h-4 w-4 text-emerald-500" />;
    case 'document':
      return <FileIcon className="h-4 w-4 text-amber-500" />;
    default:
      return <FileText className="h-4 w-4 text-red-400" />;
  }
}

export function MaterialsCourseBrowser({
  section,
  title,
  onBack,
  onCreateFolder,
  onAddFile,
  onEditFolder,
  onEditFile,
}: MaterialsCourseBrowserProps) {
  const [currentFolderId, setCurrentFolderId] = useState<string | null>(null);

  const foldersById = useMemo(() => buildFoldersById(section.folders), [section.folders]);
  const childrenByParent = useMemo(() => buildChildrenByParent(section.folders), [section.folders]);
  const filesByFolder = useMemo(() => buildFilesByFolder(section.files), [section.files]);
  const trail = useMemo(
    () => buildBreadcrumbTrail(currentFolderId, foldersById),
    [currentFolderId, foldersById],
  );

  const childFolders = childrenByParent.get(currentFolderId) ?? [];
  const currentFiles = filesByFolder.get(currentFolderId) ?? [];
  const skin = SECTION_SKINS[section.subtestCode as Subtest] ?? DEFAULT_SKIN;
  const Icon = skin.Icon;
  const isEmpty = childFolders.length === 0 && currentFiles.length === 0;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Button size="sm" variant="ghost" onClick={onBack}>
          <ArrowLeft className="mr-1.5 h-4 w-4" />
          Back to Course Materials
        </Button>
        <div className="flex gap-2">
          <Button size="sm" variant="outline" onClick={() => onCreateFolder(currentFolderId)}>
            <FolderPlus className="mr-1.5 h-3.5 w-3.5" />
            New folder
          </Button>
          {currentFolderId ? (
            <Button size="sm" onClick={() => onAddFile(currentFolderId)}>
              <Plus className="mr-1.5 h-3.5 w-3.5" />
              Add file
            </Button>
          ) : null}
        </div>
      </div>

      <div className="flex items-center gap-2">
        <span className={`inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-gradient-to-br ${skin.tile}`}>
          <Icon className="h-4 w-4" />
        </span>
        <nav aria-label="Breadcrumb" className="flex flex-wrap items-center gap-1 text-sm">
          <button
            type="button"
            className={currentFolderId === null ? 'font-semibold text-admin-fg-strong' : 'font-semibold text-admin-fg-muted hover:text-admin-fg-strong'}
            onClick={() => setCurrentFolderId(null)}
          >
            {title}
          </button>
          {trail.map((crumb) => (
            <span key={crumb.canonicalFolderId} className="flex items-center gap-1">
              <ChevronRight className="h-3.5 w-3.5 text-admin-fg-muted" />
              <button
                type="button"
                className={currentFolderId === crumb.canonicalFolderId ? 'font-semibold text-admin-fg-strong' : 'font-semibold text-admin-fg-muted hover:text-admin-fg-strong'}
                onClick={() => setCurrentFolderId(crumb.canonicalFolderId)}
              >
                {crumb.name}
              </button>
            </span>
          ))}
        </nav>
      </div>

      {isEmpty ? (
        <EmptyState illustration={<FileText />} title="No files in this folder yet" size="sm" />
      ) : (
        <div className="space-y-3">
          {childFolders.length > 0 ? (
            <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
              {childFolders.map((folder) => {
                const counts = countDescendants(folder.canonicalFolderId, childrenByParent, filesByFolder);
                return (
                  <Card key={folder.canonicalFolderId}>
                    <CardContent className="space-y-1 p-3">
                      <div className="flex items-start justify-between gap-2">
                        <button
                          type="button"
                          className="flex min-w-0 flex-1 items-center gap-2 text-left"
                          onClick={() => setCurrentFolderId(folder.canonicalFolderId)}
                        >
                          <FolderOpen className="h-4 w-4 shrink-0 text-admin-fg-muted" />
                          <span className="min-w-0 flex-1 truncate text-sm font-semibold text-admin-fg-strong">{folder.name}</span>
                        </button>
                        <Button size="sm" variant="ghost" aria-label={`Edit ${folder.name}`} onClick={() => onEditFolder(folder.canonicalFolderId)}>
                          <Pencil className="h-3.5 w-3.5" />
                        </Button>
                      </div>
                      <div className="flex items-center gap-2 pl-6">
                        <Badge variant={folder.status === 'Published' ? 'success' : 'secondary'}>{folder.status}</Badge>
                        <span className="text-xs text-admin-fg-muted">{counts.folderCount} folders · {counts.fileCount} files</span>
                      </div>
                    </CardContent>
                  </Card>
                );
              })}
            </div>
          ) : null}

          {currentFiles.length > 0 ? (
            <div className="space-y-1.5">
              {currentFiles.map((file) => (
                <div key={file.canonicalFileId} className="flex items-center gap-2 rounded-admin border border-admin-border bg-admin-bg-subtle px-2.5 py-2 text-xs">
                  <FileKindIcon kind={file.kind} />
                  <span className="min-w-0 flex-1 truncate font-medium text-admin-fg-strong">
                    {file.title}
                    <span className="ml-1 font-normal text-admin-fg-muted">· {file.status}</span>
                  </span>
                  <Button size="sm" variant="ghost" aria-label={`Edit ${file.title}`} onClick={() => onEditFile(file.canonicalFileId)}>
                    <Pencil className="h-3.5 w-3.5" />
                  </Button>
                </div>
              ))}
            </div>
          ) : null}
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Typecheck**

Run: `pnpm exec tsc --noEmit`
Expected: no errors. (This component isn't reachable from the UI yet — Task 5 wires it in — so this is a compile-only check.)

- [ ] **Step 3: Commit**

```bash
git add "components/domain/materials/materials-course-browser.tsx"
git commit -m "feat(materials): add MaterialsCourseBrowser breadcrumb drill-down component"
```

---

### Task 5: Wire the browser into `CourseMaterialsMap`

**Files:**
- Modify: `components/domain/materials/course-materials-map.tsx` (full-file rewrite — every section changes)

**Interfaces:**
- Consumes: `MaterialsCourseBrowser` (Task 4) with the exact prop shape defined there.
- Produces: `CourseMaterialsMapProps.onCreateFolder` gains an optional 4th `parentFolderId?: string | null` param — already compatible with Task 3's `openCourseCreateFolder`, no further changes needed there.

- [ ] **Step 1: Replace the file**

Replace the full contents of `components/domain/materials/course-materials-map.tsx` with:

```tsx
'use client';

import { useEffect, useState } from 'react';
import { BookOpen, Database, FileText, FolderPlus, Loader2, Pencil, Plus, Stethoscope } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { adminGetMaterialCourseMap, type MaterialCourseMap } from '@/lib/materials-api';
import { MaterialsCourseBrowser } from './materials-course-browser';

interface CourseMaterialsMapProps {
  onAdvanced: () => void;
  onCreateFolder: (professionId: string | null, professionLabel: string, subtestCode: string | null, parentFolderId?: string | null) => void;
  onAddFile: (folderId: string, subtestCode: string) => void;
  onEditFolder: (folderId: string) => void;
  onEditFile: (fileId: string) => void;
}

function subtestTitle(subtestCode: string): string {
  return `${subtestCode.charAt(0).toUpperCase()}${subtestCode.slice(1)}`;
}

export function CourseMaterialsMap({
  onAdvanced,
  onCreateFolder,
  onAddFile,
  onEditFolder,
  onEditFile,
}: CourseMaterialsMapProps) {
  const [data, setData] = useState<MaterialCourseMap | null>(null);
  const [selectedId, setSelectedId] = useState('medicine');
  const [error, setError] = useState<string | null>(null);
  const [browsing, setBrowsing] = useState<{ professionId: string; professionLabel: string; subtestCode: string } | null>(null);

  useEffect(() => {
    void adminGetMaterialCourseMap().then(setData).catch((reason: Error) => setError(reason.message));
  }, []);

  const selected = data?.professions.find((profession) => profession.id === selectedId);
  const browsingSection = browsing
    ? data?.professions
        .find((profession) => profession.id === browsing.professionId)
        ?.sections.find((section) => section.subtestCode === browsing.subtestCode)
    : undefined;

  if (browsing && browsingSection) {
    const browseTitle = browsingSection.sharing === 'shared'
      ? subtestTitle(browsing.subtestCode)
      : `${browsing.professionLabel} ${subtestTitle(browsing.subtestCode)}`;
    return (
      <AdminCatalogLayout
        title={browseTitle}
        eyebrow="CMS"
        description={`${browsingSection.sharing === 'shared' ? 'Shared canonical' : 'Profession-specific'} · ${browsingSection.folderCount} folders · ${browsingSection.fileCount} files`}
        breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Content', href: '/admin/content' }, { label: 'Course Materials' }]}
        hideViewModeToggle
      >
        <div className="col-span-full">
          <MaterialsCourseBrowser
            section={browsingSection}
            title={browseTitle}
            onBack={() => setBrowsing(null)}
            onCreateFolder={(parentFolderId) => onCreateFolder(browsing.professionId, browsing.professionLabel, browsing.subtestCode, parentFolderId)}
            onAddFile={(folderId) => onAddFile(folderId, browsing.subtestCode)}
            onEditFolder={onEditFolder}
            onEditFile={onEditFile}
          />
        </div>
      </AdminCatalogLayout>
    );
  }

  return (
    <AdminCatalogLayout
      title="Course Materials"
      eyebrow="CMS"
      description="Choose a profession first. Listening and Reading project the same canonical records; Writing and Speaking stay profession-specific."
      breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Content', href: '/admin/content' }, { label: 'Course Materials' }]}
      hideViewModeToggle
      actions={<Button variant="outline" onClick={onAdvanced}><Database className="mr-1.5 h-4 w-4" />Advanced / Folder Tree</Button>}
    >
      <div className="col-span-full space-y-5">
        {!data && !error ? <div className="flex items-center gap-2 text-sm text-admin-fg-muted"><Loader2 className="h-4 w-4 animate-spin" />Loading course map…</div> : null}
        {error ? <EmptyState illustration={<FileText />} title="Course map unavailable" description={error} /> : null}
        {data ? (
          <>
            {data.unmapped.folderIds.length + data.unmapped.fileIds.length > 0 ? <div role="alert" className="rounded-admin border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900">{data.unmapped.folderIds.length} folder(s) and {data.unmapped.fileIds.length} file(s) still need a structured course scope. They remain preserved and available in Advanced.</div> : null}
            <div role="list" aria-label="Course material areas" className="grid gap-3 sm:grid-cols-2 xl:grid-cols-7">
              {data.professions.map((profession) => (
                <div key={profession.id} role="listitem">
                  <button
                    type="button"
                    aria-label={`Open ${profession.label}`}
                    onClick={() => setSelectedId(profession.id)}
                    className={`h-full w-full rounded-admin border p-4 text-left ${selectedId === profession.id ? 'border-admin-primary bg-admin-primary-tint ring-1 ring-admin-primary' : 'border-admin-border bg-admin-bg-surface hover:border-admin-primary'}`}
                  >
                    <Stethoscope className="mb-3 h-5 w-5 text-admin-primary" />
                    <span className="block text-sm font-bold text-admin-fg-strong">{profession.label}</span>
                    <span className="mt-1 block text-xs text-admin-fg-muted">{profession.sections.reduce((count, section) => count + section.fileCount, 0)} files</span>
                  </button>
                </div>
              ))}
              <div role="listitem">
                <button
                  type="button"
                  aria-label="Open General English"
                  onClick={() => setSelectedId('general_english')}
                  className={`h-full w-full rounded-admin border p-4 text-left ${selectedId === 'general_english' ? 'border-admin-primary bg-admin-primary-tint ring-1 ring-admin-primary' : 'border-admin-border bg-admin-bg-surface hover:border-admin-primary'}`}
                >
                  <BookOpen className="mb-3 h-5 w-5 text-admin-primary" />
                  <span className="block text-sm font-bold text-admin-fg-strong">General English</span>
                  <span className="mt-1 block text-xs text-admin-fg-muted">{data.generalEnglish.fileCount} files · independent</span>
                </button>
              </div>
            </div>

            {selectedId === 'general_english' ? (
              <Card>
                <CardContent className="space-y-3 p-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <h2 className="font-bold text-admin-fg-strong">General English</h2>
                      <p className="mt-1 text-xs text-admin-fg-muted">Separate course area · {data.generalEnglish.folderCount} folders · {data.generalEnglish.fileCount} files</p>
                    </div>
                    <div className="flex gap-2">
                      <Button size="sm" variant="outline" onClick={() => onCreateFolder(null, 'General English', null)}><FolderPlus className="mr-1 h-3.5 w-3.5" />New folder</Button>
                      {data.generalEnglish.folders[0] ? <Button size="sm" onClick={() => onAddFile(data.generalEnglish.folders[0].canonicalFolderId, 'listening')}><Plus className="mr-1 h-3.5 w-3.5" />Add file</Button> : null}
                    </div>
                  </div>
                  <div className="grid gap-2 md:grid-cols-2">
                    {data.generalEnglish.folders.map((folder) => (
                      <div key={folder.canonicalFolderId} className="flex items-center gap-2 rounded-admin bg-admin-bg-subtle px-3 py-2 text-sm">
                        <span className="min-w-0 flex-1 truncate font-medium text-admin-fg-strong">{folder.name}</span>
                        <Button size="sm" variant="ghost" aria-label={`Edit ${folder.name}`} onClick={() => onEditFolder(folder.canonicalFolderId)}><Pencil className="h-3.5 w-3.5" /></Button>
                      </div>
                    ))}
                  </div>
                </CardContent>
              </Card>
            ) : null}

            {selected ? (
              <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                {selected.sections.map((section) => (
                  <Card key={section.subtestCode}>
                    <CardContent className="space-y-3 p-4">
                      <div className="flex items-center justify-between gap-2">
                        <h2 className="font-bold capitalize text-admin-fg-strong">{section.subtestCode}</h2>
                        <Badge variant={section.sharing === 'shared' ? 'success' : 'secondary'}>{section.sharing === 'shared' ? 'Shared canonical' : 'Profession-specific'}</Badge>
                      </div>
                      <p className="text-xs text-admin-fg-muted">{section.folderCount} folders · {section.fileCount} files</p>
                      <div className="flex flex-wrap gap-2">
                        <Button size="sm" variant="outline" onClick={() => onCreateFolder(selected.id, selected.label, section.subtestCode)}><FolderPlus className="mr-1 h-3.5 w-3.5" />New folder</Button>
                        <Button
                          size="sm"
                          onClick={() => setBrowsing({ professionId: selected.id, professionLabel: selected.label, subtestCode: section.subtestCode })}
                        >
                          Browse folders
                        </Button>
                      </div>
                    </CardContent>
                  </Card>
                ))}
              </div>
            ) : null}
          </>
        ) : null}
      </div>
    </AdminCatalogLayout>
  );
}
```

The removed piece is the old `section.folders.map(...)` / `section.files.slice(0, 8).map(...)` flat listing inside each subtest card (the source of the bug) — replaced by folder/file counts (already correct, aggregate) plus a "Browse folders" button that enters the new drill-down. General English's own card is untouched (still lists its small flat folder set directly, per the spec's explicit out-of-scope call).

- [ ] **Step 2: Typecheck**

Run: `pnpm exec tsc --noEmit`
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add "components/domain/materials/course-materials-map.tsx"
git commit -m "feat(admin): drill down into subtest folders via breadcrumb browser instead of a flat dump"
```

---

### Task 6: End-to-end verification and ship

**Files:** none (verification only)

- [ ] **Step 1: Full lightweight check**

Run: `pnpm exec tsc --noEmit`
Run: `pnpm exec vitest run lib/materials-course-tree.test.ts`
Expected: both clean.

- [ ] **Step 2: Manual click-through**

Using agent-browser (or the Browser pane) against the running dev server or, if local dev misbehaves (per the known slow-disk GOTCHA — see `docs/superpowers/specs/2026-07-28-admin-materials-course-browser-design.md`), the deployed prod admin panel:

1. Go to `/admin/content/materials` → Course Materials tab → Medicine → Listening card.
2. Click "Browse folders". Confirm the page swaps to a breadcrumb view showing only the real top-level folders (e.g. Benchmark Exams, Extra Listening Exams, Jahshan) — not a 46-item flat list.
3. Click into "Jahshan". Confirm its children render (including any nested "Listening"/"Audio" folders, now disambiguated by the breadcrumb reading e.g. "Listening › Jahshan › Listening").
4. Click "New folder" while inside a nested folder. Confirm the modal opens in place (page doesn't navigate away) and the name field is blank (not pre-filled "Listening").
5. Click "Add file" while inside a folder with files. Confirm it targets that folder (check "Saving to:" in the modal).
6. Click the breadcrumb root ("Listening") to jump back to the top. Click "Back to Course Materials" to return to the card grid.
7. Confirm the "Advanced / Folder Tree" view still works unchanged (Publish/Unpublish/Delete/Audience all still there).

- [ ] **Step 3: Push to main**

```bash
git push origin main
```

This triggers the `Build & Deploy (web + API)` workflow → blue/green deploy. Frontend-only change, no backend/migration risk.

- [ ] **Step 4: Report to owner**

Summarize what shipped (breadcrumb drill-down replaces the flat per-subtest folder dump in Course Materials) and hand off for live verification per the ship-it workflow — owner does the real check on production.
