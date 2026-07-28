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

export function buildFilesByFolder(files: MaterialCourseMapItem[]): Map<string | null, MaterialCourseMapItem[]> {
  const map = new Map<string | null, MaterialCourseMapItem[]>();
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
  filesByFolder: Map<string | null, MaterialCourseMapItem[]>,
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
 *
 * `stopAtId` (default null — the true section root) also ends the walk early
 * without including that folder in the trail. Callers pass `resolveVirtualRoot`'s
 * result here so a wrapper folder unwrapped away by that function never
 * reappears as a redundant breadcrumb crumb.
 */
export function buildBreadcrumbTrail(
  folderId: string | null,
  foldersById: Map<string, MaterialCourseMapFolder>,
  stopAtId: string | null = null,
): MaterialCourseMapFolder[] {
  const trail: MaterialCourseMapFolder[] = [];
  let currentId = folderId;
  const seen = new Set<string>();
  while (currentId && currentId !== stopAtId) {
    const current = foldersById.get(currentId);
    if (!current || seen.has(currentId)) break;
    seen.add(currentId);
    trail.unshift(current);
    currentId = current.parentFolderId ?? null;
  }
  return trail;
}

/**
 * A subtest section is conceptually rooted at a single "Listening" /
 * "Reading" / "Writing" / "Speaking" wrapper folder (and, for the
 * profession-specific subtests, a second per-profession wrapper beneath
 * that) — but those wrapper folders exist purely to anchor the section's
 * scope, not as content the admin should have to click through. Repeatedly
 * unwraps any level that holds exactly one child folder and no files of its
 * own, landing on the first level that actually branches (multiple folders)
 * or holds content (any files) — that's what the drill-down browser should
 * treat as its root, matching how the same data already reads one level
 * higher for candidates (tap "Listening" once, see the real folders).
 */
export function resolveVirtualRoot(
  childrenByParent: Map<string | null, MaterialCourseMapFolder[]>,
  filesByFolder: Map<string | null, MaterialCourseMapItem[]>,
): string | null {
  let current: string | null = null;
  const seen = new Set<string>();
  for (;;) {
    const children: MaterialCourseMapFolder[] = childrenByParent.get(current) ?? [];
    const files: MaterialCourseMapItem[] = filesByFolder.get(current) ?? [];
    if (children.length !== 1 || files.length > 0) return current;
    const only: MaterialCourseMapFolder = children[0];
    if (seen.has(only.canonicalFolderId)) return current;
    seen.add(only.canonicalFolderId);
    current = only.canonicalFolderId;
  }
}
