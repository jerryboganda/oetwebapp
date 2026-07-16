import { describe, it, expect } from 'vitest';
import {
  buildDownloadFilename,
  flattenFiles,
  folderStats,
  searchFiles,
  resolveTrail,
  formatBytes,
} from './materials-tree';
import type { LearnerMaterialFolderDto, LearnerMaterialFileDto } from './materials-api';

function file(partial: Partial<LearnerMaterialFileDto> & { id: string; title: string }): LearnerMaterialFileDto {
  return {
    subtestCode: 'listening',
    kind: 'pdf',
    sortOrder: 0,
    mediaAssetId: `asset-${partial.id}`,
    downloadUrl: `/v1/materials/files/${partial.id}/download`,
    ...partial,
  } as LearnerMaterialFileDto;
}

function folder(
  partial: Partial<LearnerMaterialFolderDto> & { id: string; name: string },
): LearnerMaterialFolderDto {
  return { sortOrder: 0, folders: [], files: [], ...partial } as LearnerMaterialFolderDto;
}

describe('buildDownloadFilename', () => {
  it('uses the title and borrows the extension from the deduplicated asset', () => {
    // Production case: the "Sample test 5" audio shares an asset ingested as "Audio 1.mp3".
    expect(
      buildDownloadFilename({ title: 'Listening Sample Test 5', originalFilename: 'Audio 1.mp3' }),
    ).toBe('Listening Sample Test 5.mp3');
  });

  it('does not double up an extension the title already carries', () => {
    expect(buildDownloadFilename({ title: 'Reading 12.pdf', originalFilename: 'READING_12.pdf' })).toBe(
      'Reading 12.pdf',
    );
  });

  it('matches the existing extension case-insensitively', () => {
    expect(buildDownloadFilename({ title: 'Jane Robinson.pdf', originalFilename: 'Jane Robinson.PDF' })).toBe(
      'Jane Robinson.pdf',
    );
  });

  it('falls back to the bare title when the asset has no extension', () => {
    expect(buildDownloadFilename({ title: 'Scan', originalFilename: 'noextension' })).toBe('Scan');
    expect(buildDownloadFilename({ title: 'Scan', originalFilename: null })).toBe('Scan');
  });

  it('ignores dots that belong to the path rather than the filename', () => {
    expect(buildDownloadFilename({ title: 'Notes', originalFilename: 'v1.2/notes' })).toBe('Notes');
  });
});

describe('formatBytes', () => {
  it('scales units and hides empty sizes', () => {
    expect(formatBytes(0)).toBe('');
    expect(formatBytes(null)).toBe('');
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(2048)).toBe('2 KB');
    expect(formatBytes(34_502_263)).toBe('32.9 MB');
  });
});

const tree: LearnerMaterialFolderDto[] = [
  folder({
    id: 'w',
    name: 'Writing',
    folders: [
      folder({
        id: 'w-n',
        name: 'Nursing',
        files: [file({ id: 'f1', title: 'Alfred Billy', sizeBytes: 1000, subtestCode: 'writing' })],
      }),
    ],
    files: [file({ id: 'f2', title: 'Writing Criteria', sizeBytes: 500, subtestCode: 'writing' })],
  }),
  folder({
    id: 'r',
    name: 'Reading',
    files: [file({ id: 'f3', title: 'READING 12', sizeBytes: 250, subtestCode: 'reading' })],
  }),
];

describe('flattenFiles', () => {
  it('collects every file with its ancestor path', () => {
    const flat = flattenFiles(tree);
    expect(flat.map((f) => f.file.id).sort()).toEqual(['f1', 'f2', 'f3']);
    expect(flat.find((f) => f.file.id === 'f1')?.path).toEqual(['Writing', 'Nursing']);
  });
});

describe('searchFiles', () => {
  const index = flattenFiles(tree);

  it('returns nothing for an empty query rather than everything', () => {
    expect(searchFiles(index, '   ')).toEqual([]);
  });

  it('matches on title', () => {
    expect(searchFiles(index, 'reading 12').map((f) => f.file.id)).toEqual(['f3']);
  });

  it('matches on ancestor folder name', () => {
    expect(searchFiles(index, 'nursing').map((f) => f.file.id)).toEqual(['f1']);
  });

  it('requires every term to match', () => {
    expect(searchFiles(index, 'writing nursing').map((f) => f.file.id)).toEqual(['f1']);
    expect(searchFiles(index, 'writing zzz')).toEqual([]);
  });
});

describe('folderStats', () => {
  it('counts files and bytes recursively but subfolders only at the top level', () => {
    expect(folderStats(tree[0])).toEqual({ files: 2, folders: 1, bytes: 1500 });
  });
});

describe('resolveTrail', () => {
  it('resolves a nested id trail to folder objects', () => {
    expect(resolveTrail(tree, ['w', 'w-n']).map((f) => f.name)).toEqual(['Writing', 'Nursing']);
  });

  it('stops at the last valid ancestor when the trail breaks', () => {
    expect(resolveTrail(tree, ['w', 'missing']).map((f) => f.name)).toEqual(['Writing']);
  });
});
