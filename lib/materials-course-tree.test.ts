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
