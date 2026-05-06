import { beforeEach, describe, expect, it, vi } from 'vitest';

const mockLocalStorage: Record<string, string> = {};

const storageMock = {
  getItem: vi.fn((key: string) => mockLocalStorage[key] ?? null),
  setItem: vi.fn((key: string, value: string) => { mockLocalStorage[key] = value; }),
  removeItem: vi.fn((key: string) => { delete mockLocalStorage[key]; }),
  clear: vi.fn(() => { Object.keys(mockLocalStorage).forEach(k => delete mockLocalStorage[k]); }),
};

Object.defineProperty(globalThis, 'localStorage', { value: storageMock });

const { useExpertStore } = await import('@/lib/stores/expert-store');

describe('expert-store', () => {
  beforeEach(() => {
    storageMock.clear();
    useExpertStore.setState({
      reviewDrafts: {},
      playbackSpeed: 1,
      currentTimestamp: 0,
      isPlaying: false,
    });
  });

  const makeDraft = (overrides: Record<string, unknown> = {}) => ({
    scores: { purpose: 3 },
    criterionComments: {},
    finalComment: 'Good work',
    anchoredComments: [],
    timestampComments: [],
    scratchpad: '',
    checklistItems: [],
    version: 1,
    ...overrides,
  });

  describe('reviewDrafts', () => {
    it('upserts a new draft', () => {
      const store = useExpertStore.getState();
      store.upsertReviewDraft('rev-001', makeDraft({ finalComment: 'Good work' }));

      const draft = store.getReviewDraft('rev-001');
      expect(draft).not.toBeNull();
      expect(draft?.finalComment).toBe('Good work');
    });

    it('updates an existing draft', () => {
      const store = useExpertStore.getState();
      store.upsertReviewDraft('rev-001', makeDraft({ finalComment: 'Good work' }));
      store.upsertReviewDraft('rev-001', makeDraft({ finalComment: 'Excellent', version: 2 }));

      const draft = store.getReviewDraft('rev-001');
      expect(draft?.finalComment).toBe('Excellent');
      expect(draft?.version).toBe(2);
    });

    it('returns null for non-existent draft', () => {
      const draft = useExpertStore.getState().getReviewDraft('nonexistent');
      expect(draft).toBeNull();
    });

    it('clears a draft', () => {
      const store = useExpertStore.getState();
      store.upsertReviewDraft('rev-001', makeDraft());
      store.clearReviewDraft('rev-001');

      const draft = store.getReviewDraft('rev-001');
      expect(draft).toBeNull();
    });

    it('persists to localStorage via partialize', () => {
      const store = useExpertStore.getState();
      store.upsertReviewDraft('rev-002', makeDraft({ finalComment: 'Saved draft' }));

      expect(storageMock.setItem).toHaveBeenCalled();
    });

    it('handles multiple drafts independently', () => {
      const store = useExpertStore.getState();
      store.upsertReviewDraft('rev-a', makeDraft({ finalComment: 'A' }));
      store.upsertReviewDraft('rev-b', makeDraft({ finalComment: 'B' }));

      expect(store.getReviewDraft('rev-a')?.finalComment).toBe('A');
      expect(store.getReviewDraft('rev-b')?.finalComment).toBe('B');

      store.clearReviewDraft('rev-a');
      expect(store.getReviewDraft('rev-a')).toBeNull();
      expect(store.getReviewDraft('rev-b')?.finalComment).toBe('B');
    });
  });

  describe('playback state', () => {
    it('sets and gets playback speed', () => {
      useExpertStore.getState().setPlaybackSpeed(1.5);
      expect(useExpertStore.getState().playbackSpeed).toBe(1.5);
    });

    it('sets and gets current timestamp', () => {
      useExpertStore.getState().setCurrentTimestamp(42.5);
      expect(useExpertStore.getState().currentTimestamp).toBe(42.5);
    });

    it('sets isPlaying', () => {
      useExpertStore.getState().setIsPlaying(true);
      expect(useExpertStore.getState().isPlaying).toBe(true);
      useExpertStore.getState().setIsPlaying(false);
      expect(useExpertStore.getState().isPlaying).toBe(false);
    });
  });
});
