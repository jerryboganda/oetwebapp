import { describe, expect, it } from 'vitest';
import { getVideoReadiness } from '../use-video-readiness';
import { VIDEO_DRAFT_SEED_TITLE } from '../video-wizard-config';
import type { AdminVideoDetail } from '@/lib/api/video-library';

function makeVideo(overrides: Partial<AdminVideoDetail> = {}): AdminVideoDetail {
  return {
    videoId: 'vid-1',
    title: 'Reading strategies',
    description: 'A long enough description of the video content.',
    subtestCode: 'reading',
    language: null,
    tagsCsv: 'reading, strategy',
    difficulty: 'core',
    categoryIds: ['cat-1'],
    categoryNames: ['Exam strategy'],
    accessTier: 'free',
    targetProfessionIds: [],
    bunnyVideoId: 'bunny-1',
    bunnyCollectionId: null,
    encodeStatus: 'ready',
    encodeProgress: 100,
    encodeError: null,
    durationSeconds: 300,
    width: 1920,
    height: 1080,
    thumbnailUrl: 'https://cdn.example/thumb.jpg',
    thumbnailMode: 'auto',
    customThumbnailAssetId: null,
    captions: [{ id: 'cap-1', languageCode: 'en', label: 'English', syncedToBunny: true }],
    chapters: [
      { timeSeconds: 0, title: 'Intro' },
      { timeSeconds: 60, title: 'Walkthrough' },
    ],
    attachments: [],
    isFeatured: false,
    sortOrder: 0,
    status: 'Draft',
    publishAt: null,
    publishedAt: null,
    archivedAt: null,
    viewCount: 0,
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
    ...overrides,
  };
}

describe('getVideoReadiness', () => {
  it('is hardReady for a complete video', () => {
    const readiness = getVideoReadiness(makeVideo());
    expect(readiness.hardReady).toBe(true);
    expect(readiness.items.every((i) => i.ok)).toBe(true);
  });

  it('hard-fails when the encode is not ready', () => {
    const readiness = getVideoReadiness(makeVideo({ encodeStatus: 'encoding' }));
    expect(readiness.hardReady).toBe(false);
    const item = readiness.items.find((i) => i.label.includes('encoded'));
    expect(item).toMatchObject({ ok: false, hard: true });
  });

  it('hard-fails on the seed title and on an empty title', () => {
    expect(getVideoReadiness(makeVideo({ title: VIDEO_DRAFT_SEED_TITLE })).hardReady).toBe(false);
    expect(getVideoReadiness(makeVideo({ title: '   ' })).hardReady).toBe(false);
  });

  it('hard-fails without a category', () => {
    expect(getVideoReadiness(makeVideo({ categoryIds: [] })).hardReady).toBe(false);
  });

  describe('thumbnail rule (auto vs custom)', () => {
    it('passes via auto thumbnail when the encode is ready', () => {
      const readiness = getVideoReadiness(
        makeVideo({ customThumbnailAssetId: null, encodeStatus: 'ready' }),
      );
      const item = readiness.items.find((i) => i.label.includes('Thumbnail'));
      expect(item?.ok).toBe(true);
    });

    it('passes via a custom thumbnail even while encoding', () => {
      const readiness = getVideoReadiness(
        makeVideo({ customThumbnailAssetId: 'asset-1', encodeStatus: 'encoding' }),
      );
      const item = readiness.items.find((i) => i.label.includes('Thumbnail'));
      expect(item?.ok).toBe(true);
      // The overall hard gate still fails because the encode is not ready.
      expect(readiness.hardReady).toBe(false);
    });

    it('fails with no custom thumbnail and a non-ready encode', () => {
      const readiness = getVideoReadiness(
        makeVideo({ customThumbnailAssetId: null, encodeStatus: 'processing' }),
      );
      const item = readiness.items.find((i) => i.label.includes('Thumbnail'));
      expect(item).toMatchObject({ ok: false, hard: true });
    });
  });

  it('soft rules warn without blocking publish', () => {
    const readiness = getVideoReadiness(
      makeVideo({ description: 'short', captions: [], chapters: [], tagsCsv: '' }),
    );
    expect(readiness.hardReady).toBe(true);
    const soft = readiness.items.filter((i) => !i.hard);
    expect(soft).toHaveLength(4);
    expect(soft.every((i) => i.ok === false)).toBe(true);
  });

  it('soft description rule requires at least 20 characters', () => {
    const short = getVideoReadiness(makeVideo({ description: 'x'.repeat(19) }));
    const long = getVideoReadiness(makeVideo({ description: 'x'.repeat(20) }));
    expect(short.items.find((i) => i.label.includes('Description'))?.ok).toBe(false);
    expect(long.items.find((i) => i.label.includes('Description'))?.ok).toBe(true);
  });

  it('soft chapter rule requires at least two chapters', () => {
    const one = getVideoReadiness(makeVideo({ chapters: [{ timeSeconds: 0, title: 'Intro' }] }));
    expect(one.items.find((i) => i.label.includes('chapters'))?.ok).toBe(false);
  });
});
