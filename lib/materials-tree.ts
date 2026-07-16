/**
 * Pure helpers for the learner Materials browser.
 *
 * The learner tree arrives fully hydrated from `/v1/materials`, so navigation,
 * search and counts are all derived client-side from that single payload.
 * Keeping the logic here (free of React) makes it unit-testable and keeps the
 * browser components focused on rendering.
 */

import type { LearnerMaterialFolderDto, LearnerMaterialFileDto } from './materials-api';

export interface FlatFile {
  file: LearnerMaterialFileDto;
  /** Ancestor folder names, outermost first — e.g. ['Writing', 'Nursing']. */
  path: string[];
  /** Lowercased haystack of title + path, precomputed for search. */
  haystack: string;
}

export interface FolderStats {
  files: number;
  folders: number;
  bytes: number;
}

/**
 * A media asset can be shared by several MaterialFiles when the uploader
 * deduplicated byte-identical sources. In that case `originalFilename` belongs
 * to whichever file was ingested first, so downloading by it hands the learner
 * a misleading name (e.g. "Listening Sample Test 5" saving as "Audio 1.mp3").
 * The title is authoritative; borrow only the extension from the asset.
 */
export function buildDownloadFilename(file: {
  title: string;
  originalFilename?: string | null;
}): string {
  const title = file.title.trim();
  const ext = file.originalFilename?.match(/\.[^./\\]+$/)?.[0] ?? '';
  if (!ext) return title;
  return title.toLowerCase().endsWith(ext.toLowerCase()) ? title : `${title}${ext}`;
}

export function formatBytes(bytes?: number | null): string {
  if (bytes == null || bytes <= 0) return '';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

/** Depth-first flatten of every file in the tree, tagged with its folder path. */
export function flattenFiles(
  folders: LearnerMaterialFolderDto[],
  path: string[] = [],
): FlatFile[] {
  const out: FlatFile[] = [];
  for (const folder of folders) {
    const next = [...path, folder.name];
    for (const file of folder.files ?? []) {
      out.push({
        file,
        path: next,
        haystack: `${file.title} ${next.join(' ')}`.toLowerCase(),
      });
    }
    out.push(...flattenFiles(folder.folders ?? [], next));
  }
  return out;
}

/** Recursive counts for a folder card: direct subfolders, total files, total bytes. */
export function folderStats(folder: LearnerMaterialFolderDto): FolderStats {
  let files = folder.files?.length ?? 0;
  let bytes = (folder.files ?? []).reduce((sum, f) => sum + (f.sizeBytes ?? 0), 0);
  for (const sub of folder.folders ?? []) {
    const s = folderStats(sub);
    files += s.files;
    bytes += s.bytes;
  }
  return { files, folders: folder.folders?.length ?? 0, bytes };
}

/**
 * Match every whitespace-separated term against the file's title+path.
 * All terms must match (AND), so "reading 12" narrows rather than widens.
 */
export function searchFiles(index: FlatFile[], query: string): FlatFile[] {
  const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
  if (terms.length === 0) return [];
  return index.filter((entry) => terms.every((t) => entry.haystack.includes(t)));
}

/** Resolve a folder-id trail into the folder objects it points at. */
export function resolveTrail(
  folders: LearnerMaterialFolderDto[],
  trail: string[],
): LearnerMaterialFolderDto[] {
  const out: LearnerMaterialFolderDto[] = [];
  let level = folders;
  for (const id of trail) {
    const match = level.find((f) => f.id === id);
    if (!match) break;
    out.push(match);
    level = match.folders ?? [];
  }
  return out;
}
