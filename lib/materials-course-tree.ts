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
