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

  // "VIDEO ACCESS HIERARCHY & ISOLATION RULES" (31 Aug 2026) §1 "hide means hide" + §6.8:
  // the API returns only entitlement-filtered shelves, so a subtest the learner's package
  // excludes must produce NO card at all — not a disabled one, not a 0-video one — and the
  // "X collections · Y videos" line must count only what survived filtering.
  describe('package isolation (31 Aug 2026 spec)', () => {
    it('renders only the Writing card for a Writing Crash learner', () => {
      const modules = groupVideoCategoriesByModule(
        [
          category('w1', 'Writing / Arabic / New Medicine Crash Course / Sessions / Day 1', 4),
          category('w2', 'Writing / Medicine / Arabic / Fast-Track Crash Course', 7),
          category('w3', 'Writing / Medicine / English / Sessions', 11),
          category('w4', 'Writing / Medicine / English / Workshops / Sessions', 3),
        ],
        (item) => item.videos,
      );

      expect(modules.map((module) => module.meta.key)).toEqual(['writing']);
      expect(modules[0].categories.length).toBe(4);
      expect(modules[0].videoCount).toBe(25);
    });

    it('renders only the Speaking card for a Speaking Crash learner', () => {
      const modules = groupVideoCategoriesByModule(
        [category('s1', 'Speaking / Medicine / Arabic / Sessions', 3)],
        (item) => item.videos,
      );
      expect(modules.map((module) => module.meta.key)).toEqual(['speaking']);
    });

    it('renders Writing + Speaking only for a Mega/Double Special learner', () => {
      const modules = groupVideoCategoriesByModule(
        [
          category('w1', 'Writing / Medicine / English / Sessions', 11),
          category('s1', 'Speaking / English / Sessions', 5),
        ],
        (item) => item.videos,
      );
      expect(modules.map((module) => module.meta.key)).toEqual(['writing', 'speaking']);
    });

    it('never renders an empty subtest card when filtering removed every video', () => {
      const modules = groupVideoCategoriesByModule(
        [
          category('l1', 'Listening / Arabic / Workshops', 0),
          category('r1', 'Reading / Sessions / Arabic', 0),
          category('w1', 'Writing / Medicine / Arabic / Fast-Track Crash Course', 2),
        ],
        (item) => item.videos,
      );
      expect(modules.map((module) => module.meta.key)).toEqual(['writing']);
      expect(modules[0].videoCount).toBe(2);
    });

    it('counts only the language-scoped videos the caller passes through', () => {
      const modules = groupVideoCategoriesByModule(
        [category('w1', 'Writing / Medicine / English / Sessions', 11)],
        (item) => item.videos.slice(0, 4),
      );
      expect(modules[0].videoCount).toBe(4);
      expect(modules[0].categories[0].videos.length).toBe(4);
    });
  });
});
