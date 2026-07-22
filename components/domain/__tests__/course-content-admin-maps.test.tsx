import type { ReactNode } from 'react';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CourseMaterialsMap } from '@/components/domain/materials/course-materials-map';
import { CourseVideosMap } from '@/components/domain/video-library/course-videos-map';
import { COURSE_PROFESSIONS, COURSE_SUBTESTS } from '@/lib/course-content-matrix';
import type { MaterialCourseMap } from '@/lib/materials-api';
import type { VideoCourseMap } from '@/lib/api/video-library';

const mocks = vi.hoisted(() => ({
  getMaterials: vi.fn(),
  getVideos: vi.fn(),
}));

vi.mock('@/lib/materials-api', () => ({ adminGetMaterialCourseMap: mocks.getMaterials }));
vi.mock('@/lib/api/video-library', () => ({ adminGetVideoCourseMap: mocks.getVideos }));
vi.mock('@/components/admin/layout/admin-catalog-layout', () => ({
  AdminCatalogLayout: ({ actions, children }: { actions?: ReactNode; children: ReactNode }) => <main>{actions}{children}</main>,
}));
vi.mock('@/components/admin/layout/admin-operations-layout', () => ({
  AdminOperationsLayout: ({ actions, children }: { actions?: ReactNode; children: ReactNode }) => <main>{actions}{children}</main>,
}));

const labels = Object.fromEntries(COURSE_PROFESSIONS.map((profession) => [profession.id, profession.label]));

function videoMap(): VideoCourseMap {
  return {
    canonicalCounts: { totalVideos: 1, activeVideos: 1, archivedVideos: 0, bunnyVideoIds: 1 },
    unmapped: [],
    professions: COURSE_PROFESSIONS.map((profession) => ({
      id: profession.id,
      label: profession.label,
      languages: (['en', 'ar'] as const).map((language) => ({
        code: language,
        label: language === 'en' ? 'English' : 'Arabic',
        sections: COURSE_SUBTESTS.map((subtest) => ({
          subtestCode: subtest,
          available: subtest === 'listening' || subtest === 'reading' || !['dentistry', 'radiography'].includes(profession.id),
          count: language === 'en' && subtest === 'listening' ? 1 : 0,
          items: language === 'en' && subtest === 'listening' ? [{
            canonicalVideoId: 'video-shared-1',
            title: 'Shared listening video',
            subtestCode: 'listening',
            language: 'en',
            sourceLabel: 'Shared English',
            status: 'Published',
            encodeStatus: 'ready',
            bunnyVideoId: 'bunny-1',
          }] : [],
        })),
      })),
    })),
  };
}

function materialMap(): MaterialCourseMap {
  return {
    canonicalCounts: { totalFolders: 2, totalFiles: 1, mediaAssets: 1 },
    unmapped: { folderIds: [], fileIds: [] },
    professions: COURSE_PROFESSIONS.map((profession) => ({
      id: profession.id,
      label: profession.label,
      sections: COURSE_SUBTESTS.map((subtest) => ({
        subtestCode: subtest,
        sharing: subtest === 'listening' || subtest === 'reading' ? 'shared' : 'profession',
        folderCount: subtest === 'listening' ? 1 : 0,
        fileCount: subtest === 'listening' ? 1 : 0,
        folders: subtest === 'listening' ? [{ canonicalFolderId: 'folder-shared-1', name: 'Listening Resources', status: 'Published' }] : [],
        files: subtest === 'listening' ? [{ canonicalFileId: 'file-shared-1', folderId: 'folder-shared-1', title: 'Shared listening PDF', kind: 'pdf', status: 'Published' }] : [],
      })),
    })),
    generalEnglish: {
      id: 'general_english', label: 'General English', folderCount: 1, fileCount: 0,
      folders: [{ canonicalFolderId: 'folder-general-1', name: 'General English', status: 'Published' }], files: [],
    },
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getVideos.mockResolvedValue(videoMap());
  mocks.getMaterials.mockResolvedValue(materialMap());
});

describe('profession-first admin course maps', () => {
  it('renders only the six professions at the Course Videos root and reuses the canonical edit link', async () => {
    const user = userEvent.setup();
    render(<CourseVideosMap onAdvanced={vi.fn()} />);

    const root = await screen.findByRole('list', { name: 'Course video professions' });
    expect(within(root).getAllByRole('listitem')).toHaveLength(6);
    expect(within(root).queryByText('Listening')).not.toBeInTheDocument();
    for (const label of Object.values(labels)) expect(within(root).getByText(label)).toBeInTheDocument();

    expect(screen.getAllByRole('link', { name: 'New' })[0]).toHaveAttribute(
      'href', '/admin/content/videos/new?profession=medicine&language=en&subtest=listening',
    );
    expect(screen.getByRole('link', { name: 'Edit' })).toHaveAttribute('href', '/admin/content/videos/video-shared-1/details');
    await user.click(within(root).getByRole('button', { name: 'Open Nursing' }));
    expect(screen.getAllByRole('link', { name: 'New' })[0]).toHaveAttribute(
      'href', '/admin/content/videos/new?profession=nursing&language=en&subtest=listening',
    );
    expect(screen.getByRole('link', { name: 'Edit' })).toHaveAttribute('href', '/admin/content/videos/video-shared-1/details');
  });

  it('renders professions plus General English at the Materials root and edits the same shared canonical file from every projection', async () => {
    const user = userEvent.setup();
    const onEditFile = vi.fn();
    const onCreateFolder = vi.fn();
    const onAddFile = vi.fn();
    render(<CourseMaterialsMap onAdvanced={vi.fn()} onCreateFolder={onCreateFolder} onAddFile={onAddFile} onEditFolder={vi.fn()} onEditFile={onEditFile} />);

    const root = await screen.findByRole('list', { name: 'Course material areas' });
    expect(within(root).getAllByRole('listitem')).toHaveLength(7);
    expect(within(root).queryByText('Listening')).not.toBeInTheDocument();

    await user.click(screen.getAllByRole('button', { name: 'New folder' })[0]);
    expect(onCreateFolder).toHaveBeenCalledWith('medicine', 'Medicine', 'listening');
    await user.click(screen.getAllByRole('button', { name: 'Add file' })[0]);
    expect(onAddFile).toHaveBeenCalledWith('folder-shared-1', 'listening');
    await user.click(screen.getByRole('button', { name: 'Edit Shared listening PDF' }));
    expect(onEditFile).toHaveBeenLastCalledWith('file-shared-1');
    await user.click(within(root).getByRole('button', { name: 'Open Nursing' }));
    await user.click(screen.getByRole('button', { name: 'Edit Shared listening PDF' }));
    expect(onEditFile).toHaveBeenNthCalledWith(2, 'file-shared-1');
  });
});
