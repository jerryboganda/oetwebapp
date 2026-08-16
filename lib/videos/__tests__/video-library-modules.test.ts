import { describe, expect, it } from 'vitest';
import type { VideoLibraryCategory, VideoSummary } from '@/lib/types/videos';
import {
  groupVideoCategoriesByModule,
  isBasicEnglishSubtest,
  moduleKeyOf,
  VIDEO_LIBRARY_MODULES,
} from '@/lib/videos/video-library-modules';

function video(id: string): VideoSummary {
  return {
    id,
    title: id,
    description: null,
    durationSeconds: 60,
    thumbnailUrl: null,
    accessTier: 'premium',
    isAccessible: true,
    requiresUpgrade: false,
    lockReason: null,
    subtestCode: null,
    difficulty: null,
    language: 'ar',
    tags: [],
    isFeatured: false,
    publishedAt: '2026-08-01T00:00:00Z',
    viewCount: 0,
    progress: null,
    bookmarked: false,
    categoryIds: [],
  };
}

function category(id: string, title: string, count = 1): VideoLibraryCategory {
  return {
    id,
    title,
    slug: id,
    description: null,
    videos: Array.from({ length: count }, (_, index) => video(`${id}-${index}`)),
  };
}

describe('video library modules', () => {
  it('exposes Basic English Course as a fifth learner box after the four subtests', () => {
    expect(VIDEO_LIBRARY_MODULES.map((module) => module.key)).toEqual([
      'listening',
      'reading',
      'writing',
      'speaking',
      'basic-english',
    ]);
    expect(VIDEO_LIBRARY_MODULES.at(-1)?.label).toBe('Basic English Course');
  });

  it('maps Basic English / General English shelf titles onto the new box', () => {
    expect(moduleKeyOf('Listening / Arabic / Workshops')).toBe('listening');
    expect(moduleKeyOf('Basic English Course / Arabic')).toBe('basic-english');
    expect(moduleKeyOf('Basic English / Grammar')).toBe('basic-english');
    expect(moduleKeyOf('General English / Booklet')).toBe('basic-english');
    expect(isBasicEnglishSubtest('basic-english')).toBe(true);
    expect(isBasicEnglishSubtest('general')).toBe(true);
    expect(isBasicEnglishSubtest('listening')).toBe(false);
  });

  it('groups registered-candidate Basic English shelves into their own module', () => {
    const modules = groupVideoCategoriesByModule(
      [
        category('l1', 'Listening / Arabic', 2),
        category('be1', 'Basic English Course / Arabic / Grammar', 3),
        category('be2', 'General English / Vocabulary', 1),
        category('empty', 'Basic English Course / Unused', 0),
      ],
      (item) => item.videos,
    );

    expect(modules.map((module) => [module.meta.key, module.videoCount, module.categories.length])).toEqual([
      ['listening', 2, 1],
      ['basic-english', 4, 2],
    ]);
  });

  it('hides the Basic English Course box when the learner has no published videos there', () => {
    const modules = groupVideoCategoriesByModule(
      [category('l1', 'Listening / Arabic', 1)],
      (item) => item.videos,
    );
    expect(modules.map((module) => module.meta.key)).toEqual(['listening']);
  });
});
